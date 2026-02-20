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

### 8. Token Provider Service
**File**: `src/TradingSystem.Upstox/Services/IUpstoxTokenProvider.cs`
**File**: `src/TradingSystem.Data/Services/UpstoxTokenProvider.cs`
- Abstraction for retrieving stored tokens
- Queries database for "default_user" access token
- Used by both API and WorkerService

### 9. Service Registration
**File**: `src/TradingSystem.Api/Program.cs`
**File**: `src/TradingSystem.WorkerService/Program.cs`
- Registered `IUserProfileService` and `UserProfileService`
- Registered `IUpstoxTokenProvider` and `UpstoxTokenProvider`
- Configured `UpstoxClient` factory to automatically load token from provider
- When UpstoxClient is created, it retrieves token via `IUpstoxTokenProvider`

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

1. `IUpstoxTokenProvider` retrieves token from database
2. `UpstoxClient` is created via factory method with the token
3. Token is set in HTTP client headers
4. All Upstox API calls automatically use stored token

**Key Component**: `IUpstoxTokenProvider`
- Interface defined in `TradingSystem.Upstox/Services`
- Implementation in `TradingSystem.Data/Services/UpstoxTokenProvider.cs`
- Queries database for "default_user" token
- Used by both API and WorkerService

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

## Architecture Benefits

**Centralized Token Management**
- Single source of truth for tokens (database)
- Both API and WorkerService use the same token
- No need to duplicate token configuration

**Automatic Token Loading**
- `IUpstoxTokenProvider` abstracts token retrieval
- `UpstoxClient` factory automatically injects current token
- Background jobs get authenticated client without manual setup

**Separation of Concerns**
- Token storage logic in `Data` layer
- Token retrieval interface in `Upstox` layer
- Clean dependency injection in both API and WorkerService

## Next Steps

1. Set Upstox ClientId and ClientSecret in configuration (both API and WorkerService)
2. Test OAuth flow by navigating to Upstox login URL
3. Verify WorkerService jobs can access Upstox API
4. Implement token refresh mechanism
5. Add token expiry checking
6. Add support for multiple users (currently uses "default_user")

## Security Notes

- Store `ClientSecret` securely (use environment variables)
- Tokens are stored in database (ensure database is secure)
- Consider encrypting tokens in database
- Implement token rotation and refresh
