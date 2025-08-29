using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("sync")] 
public class SyncController : ControllerBase
{
    private readonly IServiceProvider _sp;

    public SyncController(IServiceProvider sp)
    {
        _sp = sp;
    }

    [HttpPost("{entity}/{id:int}")]
    public async Task<IActionResult> Sync(string entity, int id, CancellationToken ct)
    {
        var result = await GenericSyncService.SyncAsync(_sp, entity, id, ct);
        return Ok(result);
    }
}

