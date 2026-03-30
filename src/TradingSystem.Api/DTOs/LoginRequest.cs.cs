using System;

namespace TradingSystem.Api.DTOs;
public class LoginRequest
{
    public string Email    { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string  Email     { get; set; } = string.Empty;
    public string  Password  { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName  { get; set; }
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Token       { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

// =============================================================================
// RESPONSE DTOs
// =============================================================================

public class AuthResponse
{
    public string        Token        { get; set; } = string.Empty;
    public string        RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt   { get; set; }
    public UserDto       User         { get; set; } = null!;
}

public class TokenResponse
{
    public string        Token        { get; set; } = string.Empty;
    public string        RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt   { get; set; }
}

public class UserDto
{
    public int             Id        { get; set; }
    public string          Email     { get; set; } = string.Empty;
    public string          FirstName { get; set; } = string.Empty;
    public string          LastName  { get; set; } = string.Empty;
    public string          Role      { get; set; } = string.Empty;
    public DateTimeOffset  CreatedAt { get; set; }
}