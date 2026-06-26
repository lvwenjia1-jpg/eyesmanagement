using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using MainApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UsersController : ControllerBase
{
    private const string DashboardLoginHeaderName = "X-Dashboard-LoginName";
    private const string ProtectedAdminLoginName = "admin";

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
        var accessDeniedResult = await EnsureSuperAdminAsync(cancellationToken);
        if (accessDeniedResult is not null)
        {
            return accessDeniedResult;
        }

        var result = await _users.QueryAsync(new UserQuery
        {
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            Keyword = request.Keyword,
            Role = request.Role ?? string.Empty
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
        var accessDeniedResult = await EnsureSuperAdminAsync(cancellationToken);
        if (accessDeniedResult is not null)
        {
            return accessDeniedResult;
        }

        var user = await _users.FindByIdAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(ToResponse(user));
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var accessDeniedResult = await EnsureSuperAdminAsync(cancellationToken);
        if (accessDeniedResult is not null)
        {
            return accessDeniedResult;
        }

        if (!TryValidateRoleAndErp(request.Role, request.ErpId))
        {
            return ValidationProblem(ModelState);
        }

        var existingUser = await _users.FindByLoginNameAsync(request.LoginName, cancellationToken);
        if (existingUser is not null)
        {
            ModelState.AddModelError(nameof(request.LoginName), "璐﹀彿宸插瓨鍦ㄣ€?");
            return ValidationProblem(ModelState);
        }

        var (salt, hash) = _passwordHasher.HashPassword(request.Password);
        var userId = await _users.CreateAsync(
            request.LoginName,
            salt,
            hash,
            request.ErpId,
            request.Role,
            cancellationToken);

        var createdUser = await _users.FindByIdAsync(userId, cancellationToken);
        return Created($"/api/users/{userId}", ToResponse(createdUser!));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<UserResponse>> Update(long id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var accessDeniedResult = await EnsureSuperAdminAsync(cancellationToken);
        if (accessDeniedResult is not null)
        {
            return accessDeniedResult;
        }

        if (!TryValidateRoleAndErp(request.Role, request.ErpId))
        {
            return ValidationProblem(ModelState);
        }

        var user = await _users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (IsProtectedAdminUser(user.LoginName) && !CanUpdateProtectedAdmin(request, user))
        {
            return BadRequest(new { message = "admin 账号只允许修改密码。" });
        }

        if (!string.Equals(request.LoginName.Trim(), user.LoginName, StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await _users.FindByLoginNameAsync(request.LoginName, cancellationToken);
            if (existingUser is not null && existingUser.Id != id)
            {
                ModelState.AddModelError(nameof(request.LoginName), "璐﹀彿宸插瓨鍦ㄣ€?");
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
            request.Role,
            salt,
            hash,
            cancellationToken);

        var updatedUser = await _users.FindByIdAsync(id, cancellationToken);
        return Ok(ToResponse(updatedUser!));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var accessDeniedResult = await EnsureSuperAdminAsync(cancellationToken);
        if (accessDeniedResult is not null)
        {
            return accessDeniedResult;
        }

        var user = await _users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        if (IsProtectedAdminUser(user.LoginName))
        {
            return BadRequest(new { message = "admin 账号不能删除。" });
        }

        await _users.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult?> EnsureSuperAdminAsync(CancellationToken cancellationToken)
    {
        var requester = await FindRequesterAsync(cancellationToken);
        if (requester is null || !requester.IsActive)
        {
            return Unauthorized(new { message = "请先登录后台。" });
        }

        if (!IsSuperAdminUser(requester))
        {
            return StatusCode(403, new { message = "只有 admin 账号可以管理用户。" });
        }

        return null;
    }

    private async Task<UserRecord?> FindRequesterAsync(CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(DashboardLoginHeaderName, out var headerValues))
        {
            return null;
        }

        var loginName = headerValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return null;
        }

        return await _users.FindByLoginNameAsync(loginName, cancellationToken);
    }

    private static bool IsSuperAdminUser(UserRecord user)
    {
        return IsProtectedAdminUser(user.LoginName) &&
               string.Equals(UserRoles.Normalize(user.Role), UserRoles.Manager, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProtectedAdminUser(string? loginName)
    {
        return string.Equals(
            loginName?.Trim(),
            ProtectedAdminLoginName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanUpdateProtectedAdmin(UpdateUserRequest request, UserRecord user)
    {
        return string.Equals(request.LoginName.Trim(), user.LoginName, StringComparison.OrdinalIgnoreCase)
               && string.Equals(NormalizeNullable(request.ErpId), NormalizeNullable(user.ErpId), StringComparison.OrdinalIgnoreCase)
               && string.Equals(UserRoles.Normalize(request.Role), UserRoles.Normalize(user.Role), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private bool TryValidateRoleAndErp(string role, string? erpId)
    {
        var normalizedRole = UserRoles.Normalize(role);
        if (!UserRoles.IsValid(normalizedRole))
        {
            ModelState.AddModelError(nameof(role), "瑙掕壊鏃犳晥銆?");
            return false;
        }

        if (UserRoles.RequiresErpId(normalizedRole) && string.IsNullOrWhiteSpace(erpId))
        {
            ModelState.AddModelError(nameof(erpId), "瀹㈡埛绔处鍙峰繀椤诲～鍐?ERP ID銆?");
            return false;
        }

        return true;
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
