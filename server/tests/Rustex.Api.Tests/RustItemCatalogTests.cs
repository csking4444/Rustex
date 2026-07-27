using Rustex.Api.Data;
using Xunit;

namespace Rustex.Api.Tests;

public class RustItemCatalogTests
{
    // The .csproj copies Data/rust-items.json next to the test binary via the project reference's
    // own build output only for Rustex.Api itself — tests run from their own bin folder, so load
    // straight from source here rather than depending on that copy step having also happened for
    // Rustex.Api.Tests.
    private static readonly string CatalogPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Rustex.Api", "Data", "rust-items.json");

    private static RustItemCatalog CreateCatalog() => new(Path.GetFullPath(CatalogPath));

    [Fact]
    public void LoadsRealCatalog_WithSeveralHundredItems()
    {
        var catalog = CreateCatalog();

        Assert.True(catalog.All.Count > 500, $"expected a substantial real item catalog, got {catalog.All.Count}");
    }

    [Fact]
    public void Search_IsCaseInsensitiveAndMatchesName()
    {
        var catalog = CreateCatalog();

        var results = catalog.Search("assault rifle");

        Assert.Contains(results, i => i.Name == "Assault Rifle");
    }

    [Fact]
    public void Search_MatchesShortname()
    {
        var catalog = CreateCatalog();

        var results = catalog.Search("rifle.ak");

        Assert.Contains(results, i => i.Shortname.Contains("rifle.ak"));
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        var catalog = CreateCatalog();

        var results = catalog.Search("a", limit: 5);

        Assert.True(results.Count <= 5);
    }

    [Fact]
    public void Search_BlankQuery_ReturnsEmpty()
    {
        var catalog = CreateCatalog();

        Assert.Empty(catalog.Search(""));
        Assert.Empty(catalog.Search("   "));
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        var catalog = CreateCatalog();

        Assert.Null(catalog.Find(-999999));
    }
}
