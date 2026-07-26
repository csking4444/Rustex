using Rustex.Infrastructure.Security;
using Xunit;

namespace Rustex.Api.Tests;

public class AesGcmEncryptionServiceTests
{
    private static AesGcmEncryptionService CreateService() =>
        new(Convert.ToBase64String(new byte[32])); // deterministic zero key, fine for a unit test

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var service = CreateService();
        const string original = "+15555550123";

        var ciphertext = service.Encrypt(original);
        var decrypted = service.Decrypt(ciphertext);

        Assert.Equal(original, decrypted);
        Assert.NotEqual(original, ciphertext);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var service = CreateService();
        const string original = "+15555550123";

        var first = service.Encrypt(original);
        var second = service.Encrypt(original);

        Assert.NotEqual(first, second); // random nonce per call
    }
}
