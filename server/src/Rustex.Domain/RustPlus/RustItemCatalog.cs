using System.Text.Json;

namespace Rustex.Domain.RustPlus;

public sealed record RustItem(int Id, string Shortname, string Name);

/// <summary>Rust item id → name/shortname lookup, used by vending search so results show
/// "Assault Rifle" instead of raw item id 4046. Loaded once from Data/rust-items.json, sourced
/// from the community rustplusplus project's item dump — needs refreshing whenever a Rust
/// content update adds items; there's no live source for this, Facepunch doesn't publish one.
/// Lives in Domain (not Api) so Infrastructure's vending poll worker can resolve item names for
/// Shop Alert bodies without a circular project reference.</summary>
public interface IRustItemCatalog
{
    RustItem? Find(int itemId);

    /// <summary>Case-insensitive substring match against name and shortname, ordered by name.</summary>
    IReadOnlyList<RustItem> Search(string query, int limit = 20);

    IReadOnlyList<RustItem> All { get; }
}

public sealed class RustItemCatalog : IRustItemCatalog
{
    private readonly Dictionary<int, RustItem> _byId;
    private readonly IReadOnlyList<RustItem> _all;

    public RustItemCatalog(string jsonPath)
    {
        using var stream = File.OpenRead(jsonPath);
        using var doc = JsonDocument.Parse(stream);

        var items = new List<RustItem>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var id)) continue;
            var shortname = property.Value.GetProperty("shortname").GetString() ?? "";
            var name = property.Value.GetProperty("name").GetString() ?? shortname;
            items.Add(new RustItem(id, shortname, name));
        }

        _all = items;
        _byId = items.ToDictionary(i => i.Id);
    }

    public IReadOnlyList<RustItem> All => _all;

    public RustItem? Find(int itemId) => _byId.GetValueOrDefault(itemId);

    public IReadOnlyList<RustItem> Search(string query, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return _all
            .Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        i.Shortname.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }
}
