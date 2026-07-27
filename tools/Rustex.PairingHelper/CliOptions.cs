namespace Rustex.PairingHelper;

internal sealed class CliOptions
{
    public string? ApiBaseUrl { get; private set; }
    public string? Code { get; private set; }
    public string? ChromePath { get; private set; }
    public string? SaveLocalPath { get; private set; }
    public bool PrintOnly { get; private set; }

    public static CliOptions? Parse(string[] args)
    {
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--api":
                    if (++i >= args.Length) return null;
                    options.ApiBaseUrl = args[i].TrimEnd('/') + "/";
                    break;
                case "--code":
                    if (++i >= args.Length) return null;
                    options.Code = args[i];
                    break;
                case "--chrome":
                    if (++i >= args.Length) return null;
                    options.ChromePath = args[i];
                    break;
                case "--save-local":
                    options.SaveLocalPath = i + 1 < args.Length && !args[i + 1].StartsWith("--")
                        ? args[++i]
                        : "rustplus.config.json";
                    break;
                case "--print-only":
                    options.PrintOnly = true;
                    break;
                case "--help":
                case "-h":
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return null;
            }
        }

        if (options.PrintOnly) return options; // doesn't need --api/--code
        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl) || string.IsNullOrWhiteSpace(options.Code)) return null;
        return options;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            rustex-pair — one-time local setup for Rustex's Rust+ auto-pairing

            Usage:
              rustex-pair --api <url> --code <code> [options]

            Required:
              --api <url>          Your Rustex server, e.g. https://api.rustex.example
              --code <code>        The one-time setup code from Rustex Settings -> Rust+

            Options:
              --chrome <path>      Path to a Chrome/Chromium executable, if auto-detection fails
              --save-local [path]  Also save credentials locally (default: rustplus.config.json)
              --print-only         Acquire credentials and print them — don't contact Rustex at all

            Your Steam password is entered in your own browser and never sent to Rustex; only the
            resulting push credentials (not a password or session token) are uploaded.
            """);
    }
}
