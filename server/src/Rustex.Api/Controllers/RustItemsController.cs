using Microsoft.AspNetCore.Mvc;
using Rustex.Domain.RustPlus;

namespace Rustex.Api.Controllers;

/// <summary>Public reference data (Rust item names) — no auth needed, same as looking up an item
/// on a wiki. Backs autocomplete for vending search and shop alerts.</summary>
[ApiController]
[Route("api/rust-items")]
public class RustItemsController : ControllerBase
{
    private readonly IRustItemCatalog _catalog;

    public RustItemsController(IRustItemCatalog catalog) => _catalog = catalog;

    [HttpGet]
    public ActionResult<IReadOnlyList<RustItem>> Search([FromQuery] string q, [FromQuery] int limit = 20) =>
        Ok(_catalog.Search(q, Math.Clamp(limit, 1, 50)));

    [HttpGet("{itemId:int}")]
    public ActionResult<RustItem> GetById(int itemId)
    {
        var item = _catalog.Find(itemId);
        return item is null ? NotFound() : Ok(item);
    }
}
