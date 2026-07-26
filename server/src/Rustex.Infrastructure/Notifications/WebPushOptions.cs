namespace Rustex.Infrastructure.Notifications;

/// <summary>VAPID keypair for Web Push (RFC 8292). Generate with the WebPush package's
/// `VapidHelper.GenerateVapidKeys()` (a tiny throwaway console snippet, or via
/// `npx web-push generate-vapid-keys` if you have Node) — there is no default here, since a
/// hardcoded keypair would be a shared secret checked into source. See .env.example.</summary>
public class WebPushOptions
{
    public const string SectionName = "WebPush";

    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }

    /// <summary>Required by the Push spec — a contact URL or mailto: so push services can reach
    /// you if your server misbehaves. Defaults to a placeholder; set a real one in production.</summary>
    public string Subject { get; set; } = "mailto:admin@example.com";
}
