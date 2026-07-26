namespace Rustex.Domain.Templating;

/// <summary>Placeholder substitution for team-chat automation templates. Pure string
/// manipulation — no framework dependencies — so it's trivially unit-testable and reusable for
/// both the preview endpoint and (once a delivery bridge exists) actual message sending.</summary>
public static class TemplateRenderer
{
    public static readonly IReadOnlyList<string> SupportedPlaceholders = new[]
    {
        "server", "grid", "time", "event", "player", "count", "team", "weapon",
    };

    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var result = template;
        foreach (var placeholder in SupportedPlaceholders)
        {
            var value = values.TryGetValue(placeholder, out var v) ? v ?? "" : "";
            result = result.Replace($"{{{placeholder}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    /// <summary>Sample values used for the preview/test-send endpoints so a template can be
    /// checked without a real event having happened.</summary>
    public static IReadOnlyDictionary<string, string?> SampleValues(string eventType) => new Dictionary<string, string?>
    {
        ["server"] = "Rustex Test Server",
        ["grid"] = "K14",
        ["time"] = DateTimeOffset.UtcNow.ToString("HH:mm"),
        ["event"] = eventType,
        ["player"] = "ExamplePlayer",
        ["count"] = "3",
        ["team"] = "Alpha Squad",
        ["weapon"] = "rocket",
    };
}

/// <summary>The event types team-chat templates can be configured for — matches the trigger
/// list from the original spec. Stored as plain strings on MessageTemplate/CallAlertSetting
/// rather than an enum so new event types don't require a migration.</summary>
public static class ChatEventTypes
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "RaidDetected", "MultipleExplosions", "RocketFired", "HvRocketFired", "IncendiaryRocketFired",
        "C4Explosion", "SatchelExplosion", "ExplosiveAmmoFired", "MlrsStrike",
        "PatrolHelicopterSpawned", "BradleySpawned", "CargoShipSpawned", "CargoPlaneSpawned",
        "LockedCrateSpawned", "HackableCrateSpawned", "OilRigCrateSpawned", "HeavyScientistsSpawned",
        "ExcavatorActive", "MissileSiloActive",
        "PlayerConnected", "PlayerDisconnected",
        "TeamMemberOnline", "TeamMemberOffline", "TeamMemberDown", "TeamMemberRevived", "TeamMemberSleeping",
        "ServerRestartWarning", "ServerRestart",
        "BaseUnderAttack", "RaidEnded", "RaidQuiet",
    };
}
