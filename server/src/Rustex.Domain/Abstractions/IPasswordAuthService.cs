namespace Rustex.Domain.Abstractions;

/// <summary>Hashing/verification for email+password accounts. Implemented over ASP.NET Core's
/// own PasswordHasher (PBKDF2, well-reviewed, already part of the framework) rather than a
/// homemade scheme — see Infrastructure for the implementation.</summary>
public interface IPasswordAuthService
{
    string HashPassword(string password);
    bool VerifyPassword(string passwordHash, string providedPassword);
}
