using System.Text;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Rustex.Api.Auth;
using Rustex.Api.HealthChecks;
using Rustex.Api.Hubs;
using Rustex.Api.Middleware;
using Rustex.Api.Startup;
using Rustex.Domain.Abstractions;
using Rustex.Domain.RustPlus;
using Rustex.Infrastructure.Auth;
using Rustex.Infrastructure.Caching;
using Rustex.Infrastructure.Emergency;
using Rustex.Infrastructure.EventIngestion;
using Rustex.Infrastructure.Notifications;
using Rustex.Infrastructure.Persistence;
using Rustex.Infrastructure.Realtime;
using Rustex.Infrastructure.RustPlus;
using Rustex.Infrastructure.RustPlus.Fcm;
using Rustex.Infrastructure.Security;
using Rustex.Infrastructure.ServerQuery;
using Serilog;
using StackExchange.Redis;

DotEnvLoader.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env"));

var builder = WebApplication.CreateBuilder(args);

// Managed hosts (Railway, Render, Fly, App Service) assign the port at run time via $PORT rather
// than letting the process choose. Honour it when present so the same image runs unchanged both
// locally (where the Dockerfile's ASPNETCORE_URLS applies) and on a platform.
var assignedPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(assignedPort) && int.TryParse(assignedPort, out _))
    builder.WebHost.UseUrls($"http://+:{assignedPort}");

builder.Configuration.AddEnvironmentVariables();

builder.Host.UseSerilog((ctx, cfg) => cfg
    .MinimumLevel.Is(ctx.HostingEnvironment.IsDevelopment() ? Serilog.Events.LogEventLevel.Debug : Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console()
    .Enrich.FromLogContext());

// ---------- Options ----------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<DiscordOAuthOptions>(builder.Configuration.GetSection(DiscordOAuthOptions.SectionName));
builder.Services.Configure<GoogleOAuthOptions>(builder.Configuration.GetSection(GoogleOAuthOptions.SectionName));
builder.Services.Configure<SteamAuthOptions>(builder.Configuration.GetSection(SteamAuthOptions.SectionName));
builder.Services.Configure<RustPlusOptions>(builder.Configuration.GetSection(RustPlusOptions.SectionName));

// ---------- Persistence ----------
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));
builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();

// ---------- Security ----------
// Fail fast rather than registering nothing: without this key the Rust+ credential store cannot
// encrypt, and the old conditional registration turned that into an obscure DI resolution error
// at the moment a user tried to pair — long after the misconfiguration could have been caught.
var encryptionKey = builder.Configuration["Encryption:FieldKey"];
if (string.IsNullOrWhiteSpace(encryptionKey))
    throw new InvalidOperationException(
        "Encryption:FieldKey is not configured. Rust+ credentials are stored encrypted and cannot be " +
        "handled without it. Generate one with: openssl rand -base64 32 (see .env.example).");
builder.Services.AddSingleton<IEncryptionService>(_ => new AesGcmEncryptionService(encryptionKey));

// ---------- Auth ----------
builder.Services.AddHttpClient<IDiscordOAuthService, DiscordOAuthService>();
builder.Services.AddHttpClient<IGoogleOAuthService, GoogleOAuthService>();
builder.Services.AddHttpClient<ISteamAuthService, SteamAuthService>();
builder.Services.AddSingleton<ISteamOpenIdStateStore, SteamOpenIdStateStore>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPasswordAuthService, PasswordAuthService>();

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
    })
    // A second, narrower scheme for the rustex-pair link-code flow — a redeemed setup code gets a
    // token on THIS scheme only, so it's rejected outright by every endpoint using the default
    // scheme above (a completely separate audience, not just a claim check).
    .AddJwtBearer(RustPlusPairingAuthConstants.SchemeName, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = RustPlusPairingAuthConstants.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(RustPlusPairingAuthConstants.CredentialWritePolicy, policy => policy
        .AddAuthenticationSchemes(RustPlusPairingAuthConstants.SchemeName)
        .RequireClaim("scope", RustPlusPairingAuthConstants.CredentialWriteScope));

    // Authenticated by default across the whole API. Previously every controller had to remember
    // its own [Authorize], so a new one that forgot was silently public; now anything reachable
    // without a login has to say so explicitly with [AllowAnonymous], which is reviewable.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

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

builder.Services.AddSingleton<RustPlusConnectionManager>();
builder.Services.AddHostedService<RustPlusSessionWarmupWorker>();
builder.Services.AddHostedService<RustPlusTeamTrackingWorker>();
builder.Services.AddHostedService<RustPlusVendingPollWorker>();
builder.Services.AddHostedService<RustPlusChatAssistantWorker>();
builder.Services.AddSingleton<IRustItemCatalog>(_ =>
    new RustItemCatalog(Path.Combine(AppContext.BaseDirectory, "Data", "rust-items.json")));
builder.Services.AddScoped<IRustPlusCredentialStore, RustPlusCredentialStore>();
builder.Services.AddSingleton<RustPlusFcmEventBus>();
builder.Services.AddHostedService<RustPlusFcmListenerWorker>();
builder.Services.AddHostedService<RustPlusSmartDevicesWorker>();
// The FCM auto-pairing listener (Phase 5, gated on RustPlus:EnableFcmListener) registers itself
// here once it exists. The old hand-rolled checkin/MCS stack was deleted — it was documented as
// unverified and known-wrong (it sent a raw FCM token where Facepunch expects an Expo token).
// See docs/RUSTPLUS.md and the plan at zany-napping-seal.md for the replacement design.

// ---------- Live synchronisation ----------
// Snapshot cache + fan-out + retry. Producers (status poller, team tracker) call
// ILiveSyncPublisher; connected clients receive "LiveUpdate", and reconnecting ones read the
// cached snapshot back through DashboardHub.SubscribeScope so they are current immediately.
builder.Services.AddSingleton<ILiveStateStore, RedisLiveStateStore>();
builder.Services.AddSingleton<ILiveBroadcaster, SignalRLiveBroadcaster>();
builder.Services.AddSingleton<SyncRetryQueue>();
builder.Services.AddSingleton<ILiveSyncPublisher, LiveSyncPublisher>();
builder.Services.AddHostedService<SyncRetryWorker>();
builder.Services.AddScoped<ILiveScopeAuthorizer, LiveScopeAuthorizer>();


builder.Services.AddSingleton<IClientConnectionRegistry, InMemoryClientConnectionRegistry>();
builder.Services.AddHttpClient<IDiscordWebhookSender, DiscordWebhookSender>();
builder.Services.Configure<WebPushOptions>(builder.Configuration.GetSection(WebPushOptions.SectionName));
builder.Services.AddSingleton<IWebPushSender, WebPushSender>();
builder.Services.AddScoped<IEmergencyAlertDispatcher, EmergencyAlertDispatcher>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

// ---------- API ----------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        // Steam64 ids overflow JS's safe integer range — see UlongStringConverter's doc comment.
        options.JsonSerializerOptions.Converters.Add(new Rustex.Api.Serialization.UlongStringConverter());
        options.JsonSerializerOptions.Converters.Add(new Rustex.Api.Serialization.NullableUlongStringConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres")
    .AddCheck<RedisHealthCheck>("redis");

var app = builder.Build();

// Apply pending migrations on boot. Off by default and opt-in via Database:AutoMigrate, because
// two replicas starting together would race each other through the same migration. On a
// single-instance host (Railway, Fly, Render) turning it on removes the separate migration step
// that is otherwise the most common reason a first deploy comes up 500ing on every query.
if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
        }
        else
        {
            logger.LogInformation("Applying {Count} pending migration(s): {Names}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied");
        }
    }
    catch (Exception ex)
    {
        // Refuse to serve on a schema we could not bring up to date — a half-migrated database
        // fails later, per-request, in ways that are far harder to diagnose than a failed boot.
        logger.LogCritical(ex, "Database migration failed — refusing to start");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseSecurityHeaders();
// Before rate limiting and auth so anything either of those throws is also shaped into the same
// JSON envelope rather than escaping as an unhandled 500 with a stack trace.
app.UseApiExceptionHandling();
app.UseIpRateLimiting();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");
// Explicitly anonymous: the fallback policy above otherwise applies here too, and a health
// endpoint that 401s defeats the point — container and load-balancer probes cannot authenticate.
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
