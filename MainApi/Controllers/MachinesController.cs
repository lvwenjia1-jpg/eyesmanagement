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
    public async Task<ActionResult<IReadOnlyList<MachineResponse>>> List(CancellationToken cancellationToken)
    {
        var machines = await _machines.ListAsync(cancellationToken);
        return Ok(machines.Select(ToResponse).ToArray());
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
        var machine = await _machines.FindByCodeAsync(request.Code, cancellationToken);
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
