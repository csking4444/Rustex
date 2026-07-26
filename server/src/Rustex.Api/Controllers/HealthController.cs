using Microsoft.AspNetCore.Mvc;

namespace Rustex.Api.Controllers;

// Liveness/readiness is served by the built-in health check middleware at GET /health
// (mapped in Program.cs via app.MapHealthChecks). This controller intentionally left as a
// placeholder for future custom health/status payloads (e.g. version, uptime).
[ApiController]
[Route("api/version")]
public class VersionController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { name = "Rustex API", phase = "Phase 1 - Foundation" });
}
