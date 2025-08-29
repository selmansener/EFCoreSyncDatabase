using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("seed")]
public class SeedController : ControllerBase
{
    private readonly IServiceProvider _sp;

    public SeedController(IServiceProvider sp)
    {
        _sp = sp;
    }

    [HttpPost]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var result = await SeedService.ResetAndSeedAsync(_sp, ct);
        return Ok(result);
    }
}

