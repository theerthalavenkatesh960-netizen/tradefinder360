using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface IUserProfileService
{
    Task StoreUserTokenAsync(string code);
    Task<UserProfile?> GetUserProfileAsync(string userId);
    Task<string?> GetAccessTokenAsync(string userId);
}
