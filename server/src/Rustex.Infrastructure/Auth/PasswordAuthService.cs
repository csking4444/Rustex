using Microsoft.AspNetCore.Identity;
using Rustex.Domain.Abstractions;
using Rustex.Domain.Entities;

namespace Rustex.Infrastructure.Auth;

public class PasswordAuthService : IPasswordAuthService
{
    // PasswordHasher<TUser>'s default implementation doesn't actually use the `user` argument
    // (it's part of the interface for extensibility, not required by the PBKDF2 scheme itself),
    // so a single shared instance with no per-call user object is the standard pattern.
    private readonly PasswordHasher<User> _hasher = new();

    public string HashPassword(string password) => _hasher.HashPassword(null!, password);

    public bool VerifyPassword(string passwordHash, string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(null!, passwordHash, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
