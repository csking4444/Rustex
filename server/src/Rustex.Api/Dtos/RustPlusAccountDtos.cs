namespace Rustex.Api.Dtos;

public record CreateLinkCodeResponse(string Code, DateTimeOffset ExpiresAt);

public record RedeemLinkCodeRequest(string Code);

public record RedeemLinkCodeResponse(string Token, int ExpiresInSeconds);

/// <summary>Mirrors RustPlusApi.Fcm.Data.Credentials — this is what `rustex-pair` uploads after
/// AcquireCredentialsAsync/RegisterWithRustPlusAsync complete on the user's own machine.</summary>
public record UploadCredentialsRequest(GcmIdentity Gcm, string FcmToken, string ExpoPushToken, string? SteamId);

public record GcmIdentity(ulong AndroidId, ulong SecurityToken);

public record RustPlusCredentialStatusResponse(
    bool HasCredentials,
    string? Status,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset? EstimatedExpiresAt,
    DateTimeOffset? LastNotificationAt,
    string? SteamId);
