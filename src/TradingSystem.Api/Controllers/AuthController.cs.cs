using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // -------------------------------------------------------------------------
    // POST /auth/login
    // Body: { "email": "user@example.com", "password": "password" }
    // -------------------------------------------------------------------------
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // TODO: Replace with real user lookup + password verification
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required" });

        return Ok(new AuthResponse
        {
            Token        = "dummy-jwt-token-replace-with-real",
            RefreshToken = "dummy-refresh-token-replace-with-real",
            ExpiresAt    = DateTimeOffset.UtcNow.AddHours(8),
            User         = new UserDto
            {
                Id        = 1,
                Email     = request.Email,
                FirstName = "Demo",
                LastName  = "User",
                Role      = "TRADER",
                CreatedAt = DateTimeOffset.UtcNow
            }
        });
    }

    // -------------------------------------------------------------------------
    // POST /auth/register
    // Body: { "email": "", "password": "", "firstName": "", "lastName": "" }
    // -------------------------------------------------------------------------
    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        // TODO: Replace with real user creation + password hashing
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required" });

        if (request.Password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters" });

        return Ok(new AuthResponse
        {
            Token        = "dummy-jwt-token-replace-with-real",
            RefreshToken = "dummy-refresh-token-replace-with-real",
            ExpiresAt    = DateTimeOffset.UtcNow.AddHours(8),
            User         = new UserDto
            {
                Id        = 1,
                Email     = request.Email,
                FirstName = request.FirstName ?? "User",
                LastName  = request.LastName  ?? "",
                Role      = "TRADER",
                CreatedAt = DateTimeOffset.UtcNow
            }
        });
    }

    // -------------------------------------------------------------------------
    // POST /auth/refresh
    // Body: { "refreshToken": "..." }
    // -------------------------------------------------------------------------
    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshRequest request)
    {
        // TODO: Validate refresh token from DB, issue new JWT
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { error = "Refresh token is required" });

        return Ok(new TokenResponse
        {
            Token        = "dummy-jwt-token-refreshed-replace-with-real",
            RefreshToken = "dummy-refresh-token-new-replace-with-real",
            ExpiresAt    = DateTimeOffset.UtcNow.AddHours(8)
        });
    }

    // -------------------------------------------------------------------------
    // POST /auth/forgot-password
    // Body: { "email": "user@example.com" }
    // -------------------------------------------------------------------------
    [HttpPost("forgot-password")]
    public IActionResult ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        // TODO: Generate reset token, send email
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Email is required" });

        // Always return success — don't reveal if email exists (security best practice)
        return Ok(new
        {
            message = "If an account exists with that email, a reset link has been sent"
        });
    }

    // -------------------------------------------------------------------------
    // POST /auth/reset-password
    // Body: { "token": "...", "newPassword": "..." }
    // -------------------------------------------------------------------------
    [HttpPost("reset-password")]
    public IActionResult ResetPassword([FromBody] ResetPasswordRequest request)
    {
        // TODO: Validate reset token, update password in DB
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest(new { error = "Token and new password are required" });

        if (request.NewPassword.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters" });

        return Ok(new { message = "Password reset successfully" });
    }

    // -------------------------------------------------------------------------
    // POST /auth/logout
    // Header: Authorization: Bearer {token}
    // -------------------------------------------------------------------------
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // TODO: Invalidate refresh token in DB / token blacklist
        return Ok(new { message = "Logged out successfully" });
    }

    // -------------------------------------------------------------------------
    // GET /auth/me
    // Header: Authorization: Bearer {token}
    // -------------------------------------------------------------------------
    [HttpGet("me")]
    public IActionResult Me()
    {
        // TODO: Validate JWT, look up user from claims
        return Ok(new UserDto
        {
            Id        = 1,
            Email     = "demo@trading.com",
            FirstName = "Demo",
            LastName  = "User",
            Role      = "TRADER",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        });
    }
}
