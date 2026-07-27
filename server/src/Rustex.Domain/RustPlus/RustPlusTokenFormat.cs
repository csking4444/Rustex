namespace Rustex.Domain.RustPlus;

public static class RustPlusTokenFormat
{
    /// <summary>Rust+ player tokens are signed 32-bit values, negative roughly half the time,
    /// but community pairing tools sometimes print the same token's unsigned 32-bit rendering
    /// instead (e.g. 4292345678 for -2621618). Accept either on input and return the canonical
    /// signed value that the rest of the app (RustPlusClient, the wire protocol itself) expects.</summary>
    public static bool TryNormalize(long raw, out int signedToken)
    {
        if (raw is >= int.MinValue and <= int.MaxValue)
        {
            signedToken = (int)raw;
            return true;
        }
        if (raw is > int.MaxValue and <= uint.MaxValue)
        {
            signedToken = unchecked((int)(uint)raw);
            return true;
        }

        signedToken = 0;
        return false;
    }
}
