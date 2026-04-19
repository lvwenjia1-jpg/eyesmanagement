using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using MainApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly UserRepository _users;
    private readonly MachineRepository _machines;
    private readonly PasswordHasher _passwordHasher;

    public AuthController(
        UserRepository users,
        MachineRepository machines,
        PasswordHasher passwordHasher)
    {
        _users = users;
        _machines = machines;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(PasswordLoginRequest request, CancellationToken cancellationToken)
    {
        return await LoginCoreAsync(request.LoginName, request.Password, null, requireMachineCode: false, cancellationToken);
    }

    [HttpPost("password-login")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<LoginResponse>> PasswordLogin(PasswordLoginRequest request, CancellationToken cancellationToken)
    {
        return await LoginCoreAsync(request.LoginName, request.Password, null, requireMachineCode: false, cancellationToken);
    }

    [HttpPost("machine-login")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<LoginResponse>> MachineLogin(LoginRequest request, CancellationToken cancellationToken)
    {
        return await LoginCoreAsync(request.LoginName, request.Password, request.MachineCode?.Trim(), requireMachineCode: true, cancellationToken);
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me([FromQuery] string loginName, CancellationToken cancellationToken)
    {
        var normalizedLoginName = loginName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedLoginName))
        {
            return BadRequest(new { message = "loginName is required when JWT auth is disabled." });
        }

        var user = await _users.FindByLoginNameAsync(normalizedLoginName, cancellationToken);
        return user is null ? NotFound() : Ok(ToUserResponse(user));
    }

    private static UserResponse ToUserResponse(UserRecord user)
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

    private async Task<ActionResult<LoginResponse>> LoginCoreAsync(
        string loginNameInput,
        string password,
        string? machineCodeInput,
        bool requireMachineCode,
        CancellationToken cancellationToken)
    {
        var loginName = loginNameInput.Trim();
        var machineCode = machineCodeInput?.Trim() ?? string.Empty;

        var user = await _users.FindByLoginNameAsync(loginName, cancellationToken);
        if (user is null)
        {
            await _users.AddLoginLogAsync(null, loginName, machineCode, false, "Account does not exist.", cancellationToken);
            return Unauthorized(new { message = "Invalid login name or password." });
        }

        if (!user.IsActive)
        {
            await _users.AddLoginLogAsync(user.Id, loginName, machineCode, false, "Account is disabled.", cancellationToken);
            return Unauthorized(new { message = "Account is disabled." });
        }

        if (requireMachineCode)
        {
            if (string.IsNullOrWhiteSpace(machineCode))
            {
                await _users.AddLoginLogAsync(user.Id, loginName, machineCode, false, "Machine code is required.", cancellationToken);
                return Unauthorized(new { message = "Machine code is required." });
            }

            var machine = await _machines.FindByCodeAsync(machineCode, cancellationToken);
            if (machine is null || !machine.IsActive)
            {
                await _users.AddLoginLogAsync(user.Id, loginName, machineCode, false, "Machine is not authorized.", cancellationToken);
                return Unauthorized(new { message = "Machine is not authorized." });
            }
        }

        if (!_passwordHasher.Verify(password, user.PasswordSalt, user.PasswordHash))
        {
            await _users.AddLoginLogAsync(user.Id, loginName, machineCode, false, "Incorrect password.", cancellationToken);
            return Unauthorized(new { message = "Invalid login name or password." });
        }

        await _users.AddLoginLogAsync(user.Id, loginName, machineCode, true, "Login succeeded.", cancellationToken);

        return Ok(new LoginResponse
        {
            // Kept for compatibility with existing clients; JWT auth is disabled.
            Token = string.Empty,
            ExpiresAtUtc = DateTime.UtcNow,
            User = ToUserResponse(user)
        });
    }
}
