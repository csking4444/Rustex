namespace Rustex.Api.Startup;

/// <summary>Minimal .env loader (no external package) — reads KEY=VALUE lines into process
/// environment variables if not already set, so `dotnet run` behaves like docker-compose's
/// env_file without adding a dependency for something this small.</summary>
public static class DotEnvLoader
{
    public static void Load(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
