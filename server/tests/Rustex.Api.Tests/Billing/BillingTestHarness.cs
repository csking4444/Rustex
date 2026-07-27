using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rustex.Domain.Billing;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Billing;
using Rustex.Infrastructure.Caching;
using Rustex.Infrastructure.Persistence;

namespace Rustex.Api.Tests.Billing;

/// <summary>Spins up a real SubscriptionService over SQLite plus in-memory doubles.
///
/// SQLite rather than EF's InMemory provider on purpose: the webhook idempotency and
/// one-subscription-per-user rules are enforced by <b>unique indexes</b>, and InMemory silently
/// ignores those — a test suite built on it would pass while the real behaviour was broken.</summary>
public sealed class BillingTestHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }
    public FakePaymentProvider Provider { get; } = new();
    public FakeCache Cache { get; } = new();
    public SubscriptionService Service { get; }
    public User User { get; }

    public BillingTestHarness()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        Db.Database.EnsureCreated();

        User = new User { Username = "tester", Email = "tester@example.com" };
        Db.Users.Add(User);
        Db.SaveChanges();

        var options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_fake",
            WebhookSecret = "whsec_fake",
            Prices = new Dictionary<string, PlanPriceIds>(StringComparer.OrdinalIgnoreCase)
            {
                ["scout"] = new() { Monthly = "price_scout_m", Yearly = "price_scout_y" },
                ["raider"] = new() { Monthly = "price_raider_m", Yearly = "price_raider_y" },
                ["clan"] = new() { Monthly = "price_clan_m", Yearly = "price_clan_y" },
            },
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:FrontendUrl"] = "https://app.test" })
            .Build();

        Service = new SubscriptionService(
            Db, Provider, Cache, options, config, NullLogger<SubscriptionService>.Instance);
    }

    public Subscription AddPaidSubscription(
        string tier = PlanCatalog.Raider,
        SubscriptionStatus status = SubscriptionStatus.Active,
        BillingInterval interval = BillingInterval.Monthly)
    {
        var sub = new Subscription
        {
            UserId = User.Id,
            PlanTier = tier,
            Interval = interval,
            Status = status,
            Source = SubscriptionSource.Stripe,
            ProviderCustomerId = "cus_test",
            ProviderSubscriptionId = "sub_test",
            CurrentPeriodStart = DateTimeOffset.UtcNow.AddDays(-5),
            CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(25),
        };
        Db.Subscriptions.Add(sub);
        Db.SaveChanges();
        return sub;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

/// <summary>Records what was asked of the provider so tests can assert on proration choices
/// without talking to Stripe.</summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public List<(string SubscriptionId, string PriceId, bool Prorate)> PlanChanges { get; } = [];
    public List<(string SubscriptionId, bool AtPeriodEnd)> Cancellations { get; } = [];

    public ProviderSubscription Next { get; set; } = new(
        "sub_test", "cus_test", "price_raider_m", SubscriptionStatus.Active,
        DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(25), null, false, null);

    public Task<string> EnsureCustomerAsync(Guid userId, string? email, string? username, CancellationToken ct) =>
        Task.FromResult("cus_test");

    public Task<CheckoutSession> CreateCheckoutSessionAsync(
        Guid userId, string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct) =>
        Task.FromResult(new CheckoutSession("cs_test", $"https://checkout.test/{priceId}"));

    public Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl, CancellationToken ct) =>
        Task.FromResult("https://portal.test/session");

    public Task<ProviderSubscription> ChangePlanAsync(string subscriptionId, string newPriceId, bool prorate, CancellationToken ct)
    {
        PlanChanges.Add((subscriptionId, newPriceId, prorate));
        Next = Next with { PriceId = newPriceId };
        return Task.FromResult(Next);
    }

    public Task<ProviderSubscription> CancelAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct)
    {
        Cancellations.Add((subscriptionId, atPeriodEnd));
        Next = atPeriodEnd
            ? Next with { CancelAtPeriodEnd = true }
            : Next with { Status = SubscriptionStatus.Canceled, CanceledAt = DateTimeOffset.UtcNow };
        return Task.FromResult(Next);
    }

    public Task<ProviderSubscription> ResumeAsync(string subscriptionId, CancellationToken ct)
    {
        Next = Next with { CancelAtPeriodEnd = false };
        return Task.FromResult(Next);
    }

    public Task<ProviderSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct) =>
        Task.FromResult<ProviderSubscription?>(Next);

    public Task<IReadOnlyList<ProviderInvoice>> ListInvoicesAsync(string customerId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProviderInvoice>>([]);

    public Task<ProviderPaymentMethod?> GetDefaultPaymentMethodAsync(string customerId, CancellationToken ct) =>
        Task.FromResult<ProviderPaymentMethod?>(null);
}

/// <summary>Dictionary-backed stand-in for Redis. Round-trips through JSON like the real one so
/// serialisation problems in the cached entitlement shape still surface.</summary>
public sealed class FakeCache : IRedisCacheService
{
    private readonly Dictionary<string, string> _store = [];

    public int Removals { get; private set; }

    public Task<T?> GetAsync<T>(string key) =>
        Task.FromResult(_store.TryGetValue(key, out var json)
            ? System.Text.Json.JsonSerializer.Deserialize<T>(json)
            : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        _store[key] = System.Text.Json.JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        Removals++;
        _store.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key) => Task.FromResult(_store.ContainsKey(key));

    public Task<bool> TrySetIfAbsentAsync<T>(string key, T value, TimeSpan ttl)
    {
        if (_store.ContainsKey(key)) return Task.FromResult(false);
        _store[key] = System.Text.Json.JsonSerializer.Serialize(value);
        return Task.FromResult(true);
    }

    public Task<T?> GetAndDeleteAsync<T>(string key)
    {
        if (!_store.Remove(key, out var json)) return Task.FromResult<T?>(default);
        return Task.FromResult(System.Text.Json.JsonSerializer.Deserialize<T>(json));
    }
}
