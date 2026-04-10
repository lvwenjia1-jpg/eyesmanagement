using MainApi.Contracts;
using MainApi.Data;
using MainApi.Domain;
using MainApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MainApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly UserRepository _users;
    private readonly MachineRepository _machines;
    private readonly PasswordHasher _passwordHasher;
    private readonly JwtTokenService _jwtTokenService;

    public AuthController(
        UserRepository users,
        MachineRepository machines,
        PasswordHasher passwordHasher,
        JwtTokenService jwtTokenService)
    {
        _users = users;
        _machines = machines;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var loginName = request.LoginName.Trim();
        var machineCode = request.MachineCode.Trim();

        var user = await _users.FindByLoginNameAsync(loginName, cancellationToken);
        if (user is null)
        {
            await _users.AddLoginLogAsync(null, loginName, machineCode, false, "账号不存在。", cancellationToken);
            return Unauthorized(new { message = "账号或密码错误。" });
        }

        if (!user.IsActive)
        {
            await _users.AddLoginLogAsync(user.Id, loginName, machineCode, false, "账号已禁用。", cancellationToken);
            return Unauthorized(new { message = "账号已禁用。" });
        }

        var machine = await _machines.FindByCodeAsync(machineCode, cancellationToken);
        if (machine is null || !machine.IsActive)
        {
            await _users.AddLoginLogAsync(user.Id, loginName, machineCode, false, "机器码未授权。", cancellationToken);
            return Unauthorized(new { message = "机器码未授权。" });
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordSalt, user.PasswordHash))
        {
            await _users.AddLoginLogAsync(user.Id, loginName, machineCode, false, "密码错误。", cancellationToken);
            return Unauthorized(new { message = "账号或密码错误。" });
        }

        var accessToken = _jwtTokenService.CreateToken(user, machine.Code);
        await _users.AddLoginLogAsync(user.Id, loginName, machineCode, true, "登录成功。", cancellationToken);

        return Ok(new LoginResponse
        {
            Token = accessToken.Token,
            ExpiresAtUtc = accessToken.ExpiresAtUtc,
            User = ToUserResponse(user)
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var loginName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return Unauthorized();
        }

        var user = await _users.FindByLoginNameAsync(loginName, cancellationToken);
        return user is null ? Unauthorized() : Ok(ToUserResponse(user));
    }

    private static UserResponse ToUserResponse(UserRecord user)
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
