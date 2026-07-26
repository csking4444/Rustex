using System.Text;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Rustex.Api.HealthChecks;
using Rustex.Api.Hubs;
using Rustex.Api.Middleware;
using Rustex.Api.Startup;
using Rustex.Domain.Abstractions;
using Rustex.Infrastructure.Auth;
using Rustex.Infrastructure.Caching;
using Rustex.Infrastructure.Emergency;
using Rustex.Infrastructure.EventIngestion;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.Realtime;
using Rustex.Infrastructure.Security;
using Rustex.Infrastructure.ServerQuery;
using Serilog;
using StackExchange.Redis;

DotEnvLoader.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Is(ctx.HostingEnvironment.IsDevelopment() ? Serilog.Events.LogEventLevel.Debug : Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console()
    .Enrich.FromLogContext());

// ---------- Options ----------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<DiscordOAuthOptions>(builder.Configuration.GetSection(DiscordOAuthOptions.SectionName));

// ---------- Persistence ----------
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));
builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();

// ---------- Security ----------
var encryptionKey = builder.Configuration["Encryption:FieldKey"];
if (!string.IsNullOrWhiteSpace(encryptionKey))
    builder.Services.AddSingleton<IEncryptionService>(_ => new AesGcmEncryptionService(encryptionKey));

// ---------- Auth ----------
builder.Services.AddHttpClient<IDiscordOAuthService, DiscordOAuthService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey))
    throw new InvalidOperationException("Jwt:SigningKey is not configured. Set Jwt__SigningKey in your .env (see .env.example).");

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // SignalR sends the access token via query string on the WebSocket handshake.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// ---------- CORS ----------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// ---------- Rate limiting ----------
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ---------- Real-time / ingestion ----------
builder.Services.AddSignalR();
builder.Services.AddScoped<IRaidEventBroadcaster, SignalRRaidEventBroadcaster>();

if (builder.Configuration.GetValue<bool>("Ingestion:EnableSimulator"))
    builder.Services.AddHostedService<EventIngestionWorker>();

builder.Services.AddSingleton<IServerQueryClient, A2sQueryClient>();
builder.Services.AddHostedService<ServerStatusPollingWorker>();

builder.Services.AddSingleton<IClientConnectionRegistry, InMemoryClientConnectionRegistry>();
builder.Services.AddScoped<IEmergencyAlertDispatcher, EmergencyAlertDispatcher>();
builder.Services.AddSingleton<IRustPlusNotificationListener, RustPlusNotificationListener>();

// ---------- API ----------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres")
    .AddCheck<RedisHealthCheck>("redis");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseSecurityHeaders();
app.UseIpRateLimiting();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHealthChecks("/health");

app.Run();
