using Microsoft.EntityFrameworkCore;
using TradingSystem.Upstox.Services;

namespace TradingSystem.Data.Services;

public class UpstoxTokenProvider : IUpstoxTokenProvider
{
    private readonly TradingDbContext _context;

    public UpstoxTokenProvider(TradingDbContext context)
    {
        _context = context;
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        try
        {
            var profile = await _context.UserProfiles
                .Where(x => x.UserId == "default_user")
                .Select(x => x.UpstoxAccessToken)
                .FirstOrDefaultAsync();

            return profile;
        }
        catch (Exception ex)
        {                           
            return null;
        }
    }
}
