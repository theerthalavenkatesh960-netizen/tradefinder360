using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Upstox;

namespace TradingSystem.Data.Services;

public class UserProfileService : IUserProfileService
{
    private readonly UpstoxClient _upstoxClient;
    private readonly TradingDbContext _context;

    public UserProfileService(
        UpstoxClient upstoxClient,
        TradingDbContext context)
    {
        _upstoxClient = upstoxClient;
        _context = context;
    }

    public async Task StoreUserTokenAsync(string code)
    {
        var tokenResponse = await _upstoxClient.FetchTokenFromUpstoxAsync(code);

        if (tokenResponse == null)
            throw new InvalidOperationException("Failed to fetch token from Upstox");

        var userId = "default_user";

        var existing = await _context.UserProfiles
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (existing != null)
        {
            existing.UpstoxAccessToken = tokenResponse.AccessToken;
            existing.UpstoxRefreshToken = tokenResponse.RefreshToken;
            existing.TokenIssuedAt = DateTime.UtcNow;
            existing.UpdatedOn = DateTime.UtcNow;
            _context.UserProfiles.Update(existing);
        }
        else
        {
            var user = new UserProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UpstoxAccessToken = tokenResponse.AccessToken,
                UpstoxRefreshToken = tokenResponse.RefreshToken,
                TokenIssuedAt = DateTime.UtcNow,
                CreatedOn = DateTime.UtcNow,
                UpdatedOn = DateTime.UtcNow
            };

            await _context.UserProfiles.AddAsync(user);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<UserProfile?> GetUserProfileAsync(string userId)
    {
        return await _context.UserProfiles
            .FirstOrDefaultAsync(x => x.UserId == userId);
    }

    public async Task<string?> GetAccessTokenAsync(string userId)
    {
        var profile = await GetUserProfileAsync(userId);
        return profile?.UpstoxAccessToken;
    }
}
