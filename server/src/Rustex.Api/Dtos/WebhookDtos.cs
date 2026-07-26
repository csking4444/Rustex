namespace Rustex.Api.Dtos;

public record WebhookResponse(
    Guid Id,
    Guid? ServerId,
    string Url,
    List<string> EventTypes,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateWebhookRequest(string Url, List<string>? EventTypes);
