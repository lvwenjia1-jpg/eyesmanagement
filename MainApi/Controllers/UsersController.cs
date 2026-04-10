using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using MainApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly UserRepository _users;
    private readonly PasswordHasher _passwordHasher;

    public UsersController(UserRepository users, PasswordHasher passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> List(CancellationToken cancellationToken)
    {
        var users = await _users.ListAsync(cancellationToken);
        return Ok(users.Select(ToResponse).ToArray());
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var existingUser = await _users.FindByLoginNameAsync(request.LoginName, cancellationToken);
        if (existingUser is not null)
        {
            ModelState.AddModelError(nameof(request.LoginName), "登录账号已存在。");
            return ValidationProblem(ModelState);
        }

        var (salt, hash) = _passwordHasher.HashPassword(request.Password);
        var userId = await _users.CreateAsync(
            request.LoginName,
            request.DisplayName,
            salt,
            hash,
            request.ErpId,
            request.WecomId,
            request.Role,
            cancellationToken);

        var createdUser = await _users.FindByIdAsync(userId, cancellationToken);
        return Created($"/api/users/{userId}", ToResponse(createdUser!));
    }

    [HttpPut("{id:long}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<UserResponse>> Update(long id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        string? salt = null;
        string? hash = null;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            (salt, hash) = _passwordHasher.HashPassword(request.Password);
        }

        await _users.UpdateAsync(
            id,
            request.DisplayName,
            request.ErpId,
            request.WecomId,
            request.Role,
            request.IsActive,
            salt,
            hash,
            cancellationToken);

        var updatedUser = await _users.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updatedUser!));
    }

    [HttpDelete("{id:long}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        await _users.SoftDeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private static UserResponse ToResponse(UserRecord user)
    {
        return new UserResponse
        {
            Id = user.Id,
            LoginName = user.LoginName,
            DisplayName = user.DisplayName,
            ErpId = user.ErpId,
            WecomId = user.WecomId,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
