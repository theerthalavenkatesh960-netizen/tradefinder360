# Upstox Token Integration Guide

## Overview
This document explains the Upstox authentication token management system integrated into the trading system.

## Components Added

### 1. Database Model
**File**: `src/TradingSystem.Core/Models/UserProfile.cs`
- Stores user authentication tokens in the database
- Fields:
  - `UserId`: User identifier (default: "default_user")
  - `UpstoxAccessToken`: OAuth access token from Upstox
  - `UpstoxRefreshToken`: Refresh token for renewing access
  - `TokenIssuedAt`: Timestamp when token was issued

### 2. Token Response Model
**File**: `src/TradingSystem.Upstox/Models/TokenResponse.cs`
- Represents the response from Upstox token API
- Contains access token, token type, expiry, and refresh token

### 3. Upstox Configuration
**File**: `src/TradingSystem.Upstox/Models/UpstoxConfig.cs`
- Added client credentials:
  - `ClientId`: Upstox API client ID
  - `ClientSecret`: Upstox API client secret
  - `RedirectUri`: OAuth callback URL

**File**: `src/TradingSystem.Api/appsettings.json`
- Configuration section for Upstox settings
- Set `RedirectUri` to match your callback endpoint

### 4. Token Exchange Service
**File**: `src/TradingSystem.Upstox/UpstoxClient.cs`
- `FetchTokenFromUpstoxAsync(code)`: Exchanges authorization code for access token
- `SetAccessToken(token)`: Updates the HTTP client with new token

### 5. User Profile Service
**File**: `src/TradingSystem.Data/Services/UserProfileService.cs`
**Interface**: `src/TradingSystem.Data/Services/Interfaces/IUserProfileService.cs`

Methods:
- `StoreUserTokenAsync(code)`: Fetches token from Upstox and stores in database
- `GetUserProfileAsync(userId)`: Retrieves user profile
- `GetAccessTokenAsync(userId)`: Gets stored access token

### 6. Token Controller
**File**: `src/TradingSystem.Api/Controllers/TokenController.cs`
- **Route**: `GET /auth/callback?code={code}`
- Receives authorization code from Upstox OAuth redirect
- Exchanges code for tokens and stores in database

### 7. Database Context
**File**: `src/TradingSystem.Data/TradingDbContext.cs`
- Added `UserProfiles` DbSet
- Configured entity mapping for user_profiles table

### 8. Service Registration
**File**: `src/TradingSystem.Api/Program.cs`
- Registered `IUserProfileService` and `UserProfileService`
- Configured `UpstoxClient` to automatically load token from database
- When UpstoxClient is created, it queries the database for the stored token

## How It Works

### Initial Authentication Flow

1. **User initiates login** → Navigate to Upstox OAuth URL:
   ```
   https://api.upstox.com/v2/login/authorization/dialog?client_id={ClientId}&redirect_uri={RedirectUri}&response_type=code
   ```

2. **User authorizes** → Upstox redirects to your callback:
   ```
   http://localhost:5000/auth/callback?code={AUTHORIZATION_CODE}
   ```

3. **TokenController receives code** → Calls `UserProfileService.StoreUserTokenAsync(code)`

4. **Service exchanges code** → Calls `UpstoxClient.FetchTokenFromUpstoxAsync(code)`

5. **Tokens stored** → Access token and refresh token saved to `user_profiles` table

### Using Stored Token

When any service needs to call Upstox API:

1. `UpstoxClient` is requested from DI container
2. Factory method loads token from database (userId: "default_user")
3. Token is set in HTTP client headers
4. All Upstox API calls automatically use stored token

## Configuration Required

Add to `.env` or `appsettings.json`:

```json
{
  "Upstox": {
    "ClientId": "YOUR_UPSTOX_CLIENT_ID",
    "ClientSecret": "YOUR_UPSTOX_CLIENT_SECRET",
    "RedirectUri": "http://localhost:5000/auth/callback",
    "BaseUrl": "https://api.upstox.com/v2"
  }
}
```

## API Endpoints

### Store Token (OAuth Callback)
```http
GET /auth/callback?code={authorization_code}
```

**Response**:
```json
"Authorization code stored successfully."
```

## Database Schema

The `user_profiles` table already exists in your Supabase database with the following structure:
- `id` (uuid, primary key)
- `user_id` (varchar, unique)
- `upstox_access_token` (text, nullable)
- `upstox_refresh_token` (text, nullable)
- `token_issued_at` (timestamptz, nullable)
- `created_on` (timestamptz)
- `updated_on` (timestamptz)

## Token Lifecycle

1. **No token**: First time user needs to authenticate via OAuth
2. **Token stored**: Automatically used for all Upstox API calls
3. **Token expires**: Implement token refresh logic (TODO)
4. **Token refresh**: Use refresh token to get new access token

## Next Steps

1. Set Upstox ClientId and ClientSecret in configuration
2. Test OAuth flow by navigating to Upstox login URL
3. Implement token refresh mechanism
4. Add token expiry checking
5. Add support for multiple users (currently uses "default_user")

## Security Notes

- Store `ClientSecret` securely (use environment variables)
- Tokens are stored in database (ensure database is secure)
- Consider encrypting tokens in database
- Implement token rotation and refresh
