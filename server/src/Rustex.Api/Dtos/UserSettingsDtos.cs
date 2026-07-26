namespace Rustex.Api.Dtos;

public record UserSettingsResponse(
    bool SoundEnabled,
    bool DesktopEnabled,
    bool BrowserEnabled,
    bool DiscordEnabled,
    bool PushEnabled,
    bool CallEnabled,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    string QuietHoursTimezone,
    DateTimeOffset UpdatedAt);

/// <summary>QuietHoursStart/End are plain "HH:mm" strings (not TimeOnly) so the wire format is
/// unambiguous in both directions — parsed/formatted explicitly in the controller instead of
/// relying on System.Text.Json's default TimeOnly converter.</summary>
public record UpdateUserSettingsRequest(
    bool SoundEnabled,
    bool DesktopEnabled,
    bool BrowserEnabled,
    bool DiscordEnabled,
    bool PushEnabled,
    bool CallEnabled,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    string QuietHoursTimezone);
