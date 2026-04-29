using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TradingSystem.Api.DTOs;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, AuthUserRecord> UsersByEmail = new();
    private static int _idSequence = 1;

    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // -------------------------------------------------------------------------
    // POST /auth/login
    // Body: { "email": "user@example.com", "password": "password" }
    // -------------------------------------------------------------------------
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required" });

        var email = request.Email.Trim().ToLowerInvariant();
        if (!UsersByEmail.TryGetValue(email, out var user) || !VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid credentials" });
        }

        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = expiresAt.AddDays(7);

        return Ok(new AuthResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User         = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role,
                CreatedAt = user.CreatedAt
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
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required" });

        if (request.Password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters" });

        var email = request.Email.Trim().ToLowerInvariant();
        if (UsersByEmail.ContainsKey(email))
        {
            return Conflict(new { error = "User already exists" });
        }

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        if (string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(request.Name))
        {
            var parts = request.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            firstName = parts.Length > 0 ? parts[0] : "User";
            lastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;
        }

        var user = new AuthUserRecord
        {
            Id = Interlocked.Increment(ref _idSequence),
            Email = email,
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "User" : firstName,
            LastName = lastName ?? string.Empty,
            Role = "TRADER",
            CreatedAt = DateTimeOffset.UtcNow,
            PasswordHash = HashPassword(request.Password)
        };

        if (!UsersByEmail.TryAdd(email, user))
        {
            return Conflict(new { error = "User already exists" });
        }

        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = expiresAt.AddDays(7);

        return Ok(new AuthResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User         = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role,
                CreatedAt = user.CreatedAt
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
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { error = "Refresh token is required" });

        var user = UsersByEmail.Values.FirstOrDefault(u =>
            string.Equals(u.RefreshToken, request.RefreshToken, StringComparison.Ordinal));

        if (user == null || user.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Unauthorized(new { error = "Invalid refresh token" });
        }

        var token = GenerateJwtToken(user);
        var newRefreshToken = GenerateRefreshToken();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiresAt = expiresAt.AddDays(7);

        return Ok(new TokenResponse
        {
            Token = token,
            RefreshToken = newRefreshToken,
            ExpiresAt = expiresAt
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
    [Authorize]
    public IActionResult Logout()
    {
        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("email")
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(email) && UsersByEmail.TryGetValue(email, out var user))
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
        }

        return Ok(new { message = "Logged out successfully" });
    }

    // -------------------------------------------------------------------------
    // GET /auth/me
    // Header: Authorization: Bearer {token}
    // -------------------------------------------------------------------------
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("user_id");

        var email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("email")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userIdRaw) || string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized(new { error = "Invalid token context" });
        }

        if (!UsersByEmail.TryGetValue(email, out var user))
        {
            return NotFound(new { error = "User not found" });
        }

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        });
    }

    private string GenerateJwtToken(AuthUserRecord user)
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is required.");
        var issuer = _configuration["Jwt:Issuer"] ?? "TradingSystem.Api";
        var audience = _configuration["Jwt:Audience"] ?? "TradingSystem.Client";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("sub", user.Id.ToString()),
            new("user_id", user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("email", user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private sealed class AuthUserRecord
    {
        public int Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string Role { get; init; } = "TRADER";
        public DateTimeOffset CreatedAt { get; init; }
        public string PasswordHash { get; init; } = string.Empty;
        public string? RefreshToken { get; set; }
        public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    }
}
