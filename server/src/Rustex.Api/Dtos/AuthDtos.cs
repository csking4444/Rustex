namespace Rustex.Api.Dtos;

public record RefreshRequest(string RefreshToken);

public record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public record CurrentUserResponse(
    Guid Id,
    string DiscordUsername,
    string? DiscordAvatar,
    string? Email,
    string? DisplayName,
    string Timezone);
