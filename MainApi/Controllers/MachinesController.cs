using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MachinesController : ControllerBase
{
    private readonly MachineRepository _machines;

    public MachinesController(MachineRepository machines)
    {
        _machines = machines;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<MachineResponse>>> Query([FromQuery] QueryMachinesRequest request, CancellationToken cancellationToken)
    {
        var result = await _machines.QueryAsync(new MachineQuery
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Keyword = request.Keyword,
            IsActive = request.IsActive
        }, cancellationToken);

        return Ok(new PagedResponse<MachineResponse>
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            Items = result.Items.Select(ToResponse).ToArray()
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<MachineResponse>> GetById(long id, CancellationToken cancellationToken)
    {
        var machine = await _machines.FindByIdAsync(id, cancellationToken);
        return machine is null ? NotFound() : Ok(ToResponse(machine));
    }

    [HttpGet("exists")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<MachineExistsResponse>> Exists([FromQuery] string code, CancellationToken cancellationToken)
    {
        var normalizedCode = code?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return Ok(new MachineExistsResponse
            {
                Code = string.Empty,
                Exists = false,
                IsActive = false
            });
        }

        var machine = await _machines.FindByCodeAsync(normalizedCode, cancellationToken);
        return Ok(new MachineExistsResponse
        {
            Code = normalizedCode,
            Exists = machine is not null,
            IsActive = machine?.IsActive ?? false,
            Id = machine?.Id,
            Description = machine?.Description ?? string.Empty
        });
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<MachineResponse>> Create(CreateMachineRequest request, CancellationToken cancellationToken)
    {
        var existing = await _machines.FindByCodeAsync(request.Code, cancellationToken);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(request.Code), "机器码已存在。");
            return ValidationProblem(ModelState);
        }

        var id = await _machines.CreateAsync(request.Code, request.Description, cancellationToken);
        var machine = await _machines.FindByIdAsync(id, cancellationToken);
        return Created($"/api/machines/{id}", ToResponse(machine!));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<MachineResponse>> Update(long id, UpdateMachineRequest request, CancellationToken cancellationToken)
    {
        var machine = await _machines.FindByIdAsync(id, cancellationToken);
        if (machine is null)
        {
            return NotFound();
        }

        var existing = await _machines.FindByCodeAsync(request.Code, cancellationToken);
        if (existing is not null && existing.Id != id)
        {
            ModelState.AddModelError(nameof(request.Code), "机器码已存在。");
            return ValidationProblem(ModelState);
        }

        await _machines.UpdateAsync(id, request.Code, request.Description, request.IsActive, cancellationToken);
        var updatedMachine = await _machines.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updatedMachine!));
    }

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var machine = await _machines.FindByIdAsync(id, cancellationToken);
        if (machine is null)
        {
            return NotFound();
        }

        await _machines.SoftDeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static MachineResponse ToResponse(MachineCodeRecord machine)
    {
        return new MachineResponse
        {
            Id = machine.Id,
            Code = machine.Code,
            Description = machine.Description,
            IsActive = machine.IsActive,
            CreatedAtUtc = machine.CreatedAtUtc
        };
    }
}
