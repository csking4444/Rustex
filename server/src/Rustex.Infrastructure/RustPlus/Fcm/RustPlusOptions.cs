namespace Rustex.Infrastructure.RustPlus.Fcm;

public sealed class RustPlusOptions
{
    public const string SectionName = "RustPlus";

    /// <summary>Holds an FCM push connection per user who has completed the one-time
    /// <c>rustex-pair</c> setup, so pressing "Pair With Server" in game registers the server here
    /// automatically. Manual pairing works regardless of this setting.</summary>
    public bool EnableFcmListener { get; set; }

    /// <summary>How long a one-time <c>rustex-pair</c> setup code stays valid.</summary>
    public int PairingCodeTtlMinutes { get; set; } = 10;

    /// <summary>Facepunch issues a Steam token valid roughly this long. There is no server-side
    /// refresh — renewal needs the Chrome + Steam interaction that only happens on the user's own
    /// machine — so we warn ahead of expiry instead. Note that expiry only stops *future* pairing
    /// and alarm pushes; already-stored (playerId, playerToken) pairs keep working indefinitely.</summary>
    public int CredentialLifetimeDays { get; set; } = 14;

    /// <summary>Vending-machine poll interval. getMapMarkers returns a marker per online player,
    /// so this is hundreds of KB on a full server — treat 60s as the floor and only poll servers
    /// that actually have enabled shop alerts.</summary>
    public int VendingPollSeconds { get; set; } = 60;
}
