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
    public async Task<ActionResult<PagedResponse<UserResponse>>> Query([FromQuery] QueryUsersRequest request, CancellationToken cancellationToken)
    {
        var result = await _users.QueryAsync(new UserQuery
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Keyword = request.Keyword,
            Role = request.Role,
            IsActive = request.IsActive
        }, cancellationToken);

        return Ok(new PagedResponse<UserResponse>
        {
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            Items = result.Items.Select(ToResponse).ToArray()
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<UserResponse>> GetById(long id, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(ToResponse(user));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var existingUser = await _users.FindByLoginNameAsync(request.LoginName, cancellationToken);
        if (existingUser is not null)
        {
            ModelState.AddModelError(nameof(request.LoginName), "账号已存在。");
            return ValidationProblem(ModelState);
        }

        var (salt, hash) = _passwordHasher.HashPassword(request.Password);
        var userId = await _users.CreateAsync(
            request.LoginName,
            salt,
            hash,
            request.ErpId,
            "user",
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

        if (!string.Equals(request.LoginName.Trim(), user.LoginName, StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await _users.FindByLoginNameAsync(request.LoginName, cancellationToken);
            if (existingUser is not null && existingUser.Id != id)
            {
                ModelState.AddModelError(nameof(request.LoginName), "账号已存在。");
                return ValidationProblem(ModelState);
            }
        }

        string? salt = null;
        string? hash = null;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            (salt, hash) = _passwordHasher.HashPassword(request.Password);
        }

        await _users.UpdateAsync(
            id,
            request.LoginName,
            request.ErpId,
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
            ErpId = user.ErpId,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
