namespace Rustex.Domain.Entities;

public class MapData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public RustServer Server { get; set; } = default!;
    public string? ImageUrl { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string MonumentDataJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Marker> Markers { get; set; } = new List<Marker>();
}

public class Marker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MapId { get; set; }
    public MapData Map { get; set; } = default!;
    public Guid CreatedBy { get; set; }
    public MarkerType Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string? Label { get; set; }
    public string? Color { get; set; }
    public bool IsShared { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
