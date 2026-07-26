namespace Rustex.Domain.Abstractions;

/// <summary>
/// Provider abstraction for the emergency phone-call system (Phase 4). Implementations
/// (Twilio first, then Vonage/Plivo) live in Rustex.Infrastructure.Voice.
/// A failure here must never block other notification channels — callers should catch
/// and record failures, not throw across the notification fan-out boundary.
/// </summary>
public interface IVoiceCallProvider
{
    string ProviderName { get; } // "twilio" | "vonage" | "plivo"

    Task<VoiceCallResult> PlaceCallAsync(VoiceCallRequest request, CancellationToken cancellationToken);
}

public sealed record VoiceCallRequest(
    string ToE164Number,
    string TtsMessage,
    string VoiceLanguage,
    string? VoiceName,
    double SpeechSpeed);

public sealed record VoiceCallResult(
    bool Accepted,
    string? ProviderCallId,
    string? Error);
