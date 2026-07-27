namespace Rustex.Domain.Billing;

/// <summary>Capability keys checked by <see cref="Plan.Allows"/>. Kept as constants rather than an
/// enum so a policy string in an attribute and the catalog entry below can never drift apart
/// without the compiler noticing.</summary>
public static class Features
{
    public const string RaidAlarms = "raid_alarms";
    public const string ServerStatus = "server_status";
    public const string TeamTracking = "team_tracking";
    public const string SmartDevices = "smart_devices";
    public const string VendingSearch = "vending_search";
    public const string ShopAlerts = "shop_alerts";
    public const string ChatAssistant = "chat_assistant";
    public const string Analytics = "analytics";
    public const string PhoneEscalation = "phone_escalation";
}

/// <summary>One purchasable tier. Prices are in cents to avoid float rounding on money.
/// <see cref="MonthlyPriceId"/>/<see cref="YearlyPriceId"/> are filled from configuration at
/// startup — they differ per Stripe account and per mode (test vs live), so they cannot be
/// baked in here.</summary>
public sealed record Plan(
    string Tier,
    string Name,
    string Description,
    long MonthlyCents,
    long YearlyCents,
    int ServerLimit,
    int TeamMemberLimit,
    IReadOnlySet<string> FeatureKeys)
{
    public string? MonthlyPriceId { get; init; }
    public string? YearlyPriceId { get; init; }

    public bool Allows(string feature) => FeatureKeys.Contains(feature);

    /// <summary>Rank for comparing tiers. Higher wins; used to tell an upgrade from a downgrade
    /// so the right proration behaviour is requested from the provider.</summary>
    public int Rank => PlanCatalog.RankOf(Tier);
}

/// <summary>The tier definitions themselves — the single source of truth for what each plan
/// unlocks. Deliberately in code rather than a database table: entitlement rules are logic, and
/// a table would let production drift away from what this repo says is enforced.
///
/// Money here must stay in step with the Stripe Price objects it points at. The catalog is the
/// source of truth for *limits and features*; Stripe is the source of truth for *what is actually
/// charged*. If they disagree, the customer is charged Stripe's number.</summary>
public static class PlanCatalog
{
    public const string Scout = "scout";
    public const string Raider = "raider";
    public const string Clan = "clan";

    private static readonly IReadOnlyList<string> Order = [Scout, Raider, Clan];

    private static readonly Dictionary<string, Plan> Plans = new(StringComparer.OrdinalIgnoreCase)
    {
        [Scout] = new Plan(
            Scout, "Scout", "Raid alarms and live status for a couple of servers.",
            MonthlyCents: 499, YearlyCents: 4990,
            ServerLimit: 2, TeamMemberLimit: 5,
            FeatureKeys: new HashSet<string> { Features.RaidAlarms, Features.ServerStatus }),

        [Raider] = new Plan(
            Raider, "Raider", "Everything in Scout plus smart devices and the market tools.",
            MonthlyCents: 999, YearlyCents: 9990,
            ServerLimit: 10, TeamMemberLimit: 20,
            FeatureKeys: new HashSet<string>
            {
                Features.RaidAlarms, Features.ServerStatus, Features.TeamTracking,
                Features.SmartDevices, Features.VendingSearch, Features.ShopAlerts,
            }),

        [Clan] = new Plan(
            Clan, "Clan", "The full toolkit for an organised group, including analytics.",
            MonthlyCents: 1999, YearlyCents: 19990,
            ServerLimit: 25, TeamMemberLimit: 100,
            FeatureKeys: new HashSet<string>
            {
                Features.RaidAlarms, Features.ServerStatus, Features.TeamTracking,
                Features.SmartDevices, Features.VendingSearch, Features.ShopAlerts,
                Features.ChatAssistant, Features.Analytics, Features.PhoneEscalation,
            }),
    };

    public static IReadOnlyCollection<Plan> All => Order.Select(t => Plans[t]).ToList();

    public static Plan? Find(string? tier) =>
        tier is not null && Plans.TryGetValue(tier, out var p) ? p : null;

    public static bool IsKnownTier(string? tier) => Find(tier) is not null;

    /// <summary>-1 for anything unrecognised, so an unknown tier always compares as lower than a
    /// real one rather than accidentally out-ranking it.</summary>
    public static int RankOf(string? tier)
    {
        if (tier is null) return -1;
        for (var i = 0; i < Order.Count; i++)
            if (string.Equals(Order[i], tier, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
