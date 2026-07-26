namespace Rustex.Api.Dtos;

public record VapidPublicKeyResponse(string? PublicKey);

public record SubscribeRequest(string Endpoint, string P256dhKey, string AuthKey);

public record UnsubscribeRequest(string Endpoint);
