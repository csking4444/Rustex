using Microsoft.Extensions.Options;
using Rustex.Domain.Entities;
using Rustex.Infrastructure.Auth;
using Xunit;

namespace Rustex.Api.Tests;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService() => new(Options.Create(new JwtOptions
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "unit-test-signing-key-that-is-long-enough-1234567890",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30,
    }));

    [Fact]
    public void CreateAccessToken_ProducesNonEmptyJwt()
    {
        var service = CreateService();
        var user = new User { DiscordId = "123", Username = "tester" };

        var token = service.CreateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length); // header.payload.signature
    }

    [Fact]
    public void CreateRefreshToken_HashIsDeterministicForSameToken()
    {
        var service = CreateService();
        var (token, hash) = service.CreateRefreshToken();

        var recomputedHash = service.HashRefreshToken(token);

        Assert.Equal(hash, recomputedHash);
    }

    [Fact]
    public void CreateRefreshToken_GeneratesUniqueTokens()
    {
        var service = CreateService();
        var (tokenA, _) = service.CreateRefreshToken();
        var (tokenB, _) = service.CreateRefreshToken();

        Assert.NotEqual(tokenA, tokenB);
    }
}
