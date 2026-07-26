namespace Rustex.Api.Dtos;

public record RefreshRequest(string RefreshToken);

public record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public record CurrentUserResponse(
    Guid Id,
    string Username,
    string? AvatarUrl,
    string? Email,
    string? DisplayName,
    string Timezone,
    bool HasPassword,
    bool HasDiscord,
    bool HasSteam);

public record RegisterRequest(string Email, string Password, string Username);

public record LoginRequest(string Email, string Password);
