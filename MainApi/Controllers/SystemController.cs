using MainApi.Contracts;
using MainApi.Data;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class SystemController : ControllerBase
{
    private readonly SystemRepository _systemRepository;

    public SystemController(SystemRepository systemRepository)
    {
        _systemRepository = systemRepository;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(SystemStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemStatusResponse>> Status(CancellationToken cancellationToken)
    {
        var status = await _systemRepository.GetStatusAsync(cancellationToken);
        return Ok(status);
    }
}
