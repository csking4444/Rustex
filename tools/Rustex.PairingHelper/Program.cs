using System.Net.Http.Headers;
using System.Net.Http.Json;
using RustPlusApi.Fcm.Registration;

namespace Rustex.PairingHelper;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options is null)
        {
            CliOptions.PrintUsage();
            return 1;
        }

        if (options.ChromePath is not null)
            Environment.SetEnvironmentVariable("CHROME_PATH", options.ChromePath);

        Console.WriteLine("Rustex Rust+ setup");
        Console.WriteLine("===================");
        Console.WriteLine();
        Console.WriteLine("This links your Rustex account to Rust+ so pairing a server in-game");
        Console.WriteLine("registers it automatically from now on.");
        Console.WriteLine();
        Console.WriteLine(">> Your Steam password is entered in YOUR browser and never sent to Rustex. <<");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var ct = cts.Token;

        try
        {
            Console.WriteLine("[1/4] Registering with Google push services...");
            var registration = new FcmRegistration();
            var credentials = await registration.AcquireCredentialsAsync(ct);

            Console.WriteLine("[2/4] Opening Chrome for Steam login — a browser window should appear...");
            var steamAuthToken = await registration.RegisterWithRustPlusAsync(credentials, ct);
            Console.WriteLine("      Steam login complete.");

            if (options.SaveLocalPath is not null)
            {
                CredentialsStore.Save(options.SaveLocalPath, credentials);
                Console.WriteLine($"      Saved a local copy to {options.SaveLocalPath} (treat it like a password file).");
            }

            if (options.PrintOnly)
            {
                Console.WriteLine();
                Console.WriteLine("--print-only was set — not contacting Rustex. Credentials JSON:");
                Console.WriteLine(CredentialsStore.Serialize(credentials));
                return 0;
            }

            using var http = new HttpClient { BaseAddress = new Uri(options.ApiBaseUrl!) };

            Console.WriteLine("[3/4] Redeeming your Rustex setup code...");
            var redeemResponse = await http.PostAsJsonAsync("api/rustplus/link-codes/redeem", new { Code = options.Code }, ct);
            if (!redeemResponse.IsSuccessStatusCode)
            {
                var body = await redeemResponse.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"Couldn't redeem that code ({(int)redeemResponse.StatusCode}): {body}");
                Console.Error.WriteLine("Generate a fresh code on the Rustex Rust+ settings page and try again.");
                return 1;
            }

            var redeemed = await redeemResponse.Content.ReadFromJsonAsync<RedeemResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Rustex returned an empty response redeeming the code.");

            if (credentials.Fcm?.Token is null)
                throw new InvalidOperationException("Credential acquisition didn't return an FCM token — try again.");

            Console.WriteLine("[4/4] Uploading credentials to Rustex...");
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, "api/rustplus/credentials")
            {
                Content = JsonContent.Create(new
                {
                    Gcm = new { credentials.Gcm.AndroidId, credentials.Gcm.SecurityToken },
                    FcmToken = credentials.Fcm.Token,
                    credentials.ExpoPushToken,
                    SteamId = (string?)null,
                }),
            };
            uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", redeemed.Token);

            var uploadResponse = await http.SendAsync(uploadRequest, ct);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var body = await uploadResponse.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"Rustex rejected the credential upload ({(int)uploadResponse.StatusCode}): {body}");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("Done! In game: press ESC, open the Rust+ tab, and tap \"Pair With Server\".");
            Console.WriteLine("Your server will appear in the Rustex dashboard within a few seconds.");
            Console.WriteLine();
            Console.WriteLine("Note: this setup is valid for roughly two weeks (a Steam/Facepunch limit,");
            Console.WriteLine("not a Rustex one). Re-run rustex-pair if auto-pairing stops working later —");
            Console.WriteLine("servers you've already paired keep working regardless.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Setup failed: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("This step talks to live Google/Expo/Facepunch/Chrome services and can fail");
            Console.Error.WriteLine("for reasons outside Rustex's control. Common fixes:");
            Console.Error.WriteLine("  - Make sure Chrome or Chromium is installed (or pass --chrome <path>).");
            Console.Error.WriteLine("  - Check your internet connection and try again.");
            Console.Error.WriteLine("  - Manual pairing (paste playerId/playerToken) always works as a fallback.");
            return 1;
        }
    }

    private sealed record RedeemResponse(string Token, int ExpiresInSeconds);
}
