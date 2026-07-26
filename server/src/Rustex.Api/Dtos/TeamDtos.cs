namespace Rustex.Api.Dtos;

public record TeamResponse(Guid Id, string Name, string Slug, string? IconUrl, DateTimeOffset CreatedAt, string RoleName);

public record CreateTeamRequest(string Name);
