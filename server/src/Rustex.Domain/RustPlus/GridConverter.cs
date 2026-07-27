namespace Rustex.Domain.RustPlus;

/// <summary>Converts in-game world coordinates to Rust's on-screen grid reference (e.g. "K14").
/// Ported from the community rustplusplus Discord bot's map utility
/// (github.com/alexemanuelol/rustplusplus/blob/master/src/util/map.js), the closest thing to an
/// authoritative reference for this — Facepunch doesn't document it, and the commonly-assumed
/// 150-unit grid cell is wrong; it's actually 146.25.</summary>
public static class GridConverter
{
    public const float GridDiameter = 146.25f;

    /// <summary>Returns null if the position falls outside the grid system (off the edge of the
    /// map, in the fringe area past the last full grid cell).</summary>
    public static string? ToGrid(float x, float y, int mapSize)
    {
        var correctedMapSize = GetCorrectedMapSize(mapSize);
        if (IsOutsideGridSystem(x, y, correctedMapSize)) return null;

        var letters = GetGridPosLettersX(x, correctedMapSize);
        var number = GetGridPosNumberY(y, correctedMapSize);
        return letters is null || number is null ? null : $"{letters}{number}";
    }

    /// <summary>Rust's actual world size isn't always a clean multiple of the grid cell size —
    /// this snaps it to the nearest grid boundary the same way the in-game overlay does.</summary>
    public static float GetCorrectedMapSize(int mapSize)
    {
        var remainder = mapSize % GridDiameter;
        var offset = GridDiameter - remainder;
        return remainder < 120 ? mapSize - remainder : mapSize + offset;
    }

    internal static string? GetGridPosLettersX(float x, float mapSize)
    {
        var counter = 1;
        for (float startGrid = 0; startGrid < mapSize; startGrid += GridDiameter)
        {
            if (x >= startGrid && x <= startGrid + GridDiameter) return NumberToLetters(counter);
            counter++;
        }
        return null;
    }

    internal static int? GetGridPosNumberY(float y, float mapSize)
    {
        var counter = 1;
        var numberOfGrids = (int)(mapSize / GridDiameter);
        for (float startGrid = 0; startGrid < mapSize; startGrid += GridDiameter)
        {
            if (y >= startGrid && y <= startGrid + GridDiameter) return numberOfGrids - counter;
            counter++;
        }
        return null;
    }

    /// <summary>Bijective base-26: 1→A, 26→Z, 27→AA, 52→AZ, 53→BA — not the same as naive
    /// base-26, which breaks at multiples of 26 (would give 26→"Z" but 52→"BZ" instead of "AZ").</summary>
    public static string NumberToLetters(int num)
    {
        var mod = num % 26;
        var pow = num / 26;
        string result;
        if (mod != 0)
        {
            result = ((char)(64 + mod)).ToString();
        }
        else
        {
            pow--;
            result = "Z";
        }
        return pow > 0 ? NumberToLetters(pow) + result : result;
    }

    internal static bool IsOutsideGridSystem(float x, float y, float mapSize, float offset = 0) =>
        x < -offset || x > mapSize + offset || y < -offset || y > mapSize + offset;
}
