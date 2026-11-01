using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BetterPosters.Api;

/// <summary>
/// The poster run's live progress, for the settings page (admins only).
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("BetterPosters")]
public class StatusController : ControllerBase
{
    /// <summary>
    /// How the poster run is going.
    /// </summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    public IActionResult Status() => Ok(RunStatus.Snapshot());
}
