namespace Rustex.Api.Auth;

/// <summary>Shared between Program.cs (scheme/policy setup) and RustPlusAccountController (token
/// issuance) so the audience/scope/policy names can't drift out of sync between the two.</summary>
public static class RustPlusPairingAuthConstants
{
    public const string SchemeName = "Pairing";
    public const string Audience = "rustex-pairing";
    public const string CredentialWriteScope = "rustplus.credentials.write";
    public const string CredentialWritePolicy = "RustPlusCredentialWrite";
}
