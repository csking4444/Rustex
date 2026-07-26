namespace Rustex.Infrastructure.RustPlus.Fcm;

public sealed class RustPlusOptions
{
    public const string SectionName = "RustPlus";

    /// <summary>The Rust+ companion app's Firebase sender/project ID. Required for auto-pairing;
    /// there is no safe default to guess — see docs/RUSTPLUS.md.</summary>
    public string? FcmSenderId { get; set; }

    public bool EnableAutoPairing { get; set; }
}
