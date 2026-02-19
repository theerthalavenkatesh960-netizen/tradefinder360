
using SwingLyne.Api.Services.Interfaces;
using SwingLyne.Domain.Models;
using SwingLyne.Domain.Repositories.Interfaces;
using SwingLyne.ExternalServices.Services;

namespace SwingLyne.Api.Services
{
    public class UserProfileService : IUserProfileService
    {
        private readonly IUpstoxService _upstoxService;
        private readonly ICommonRepository<UserProfile> _userRepository;

        public UserProfileService(
            IUpstoxService upstoxService,
            ICommonRepository<UserProfile> userRepository)
        {
            _upstoxService = upstoxService;
            _userRepository = userRepository;
        }

        public async Task StoreUserTokenAsync(string code, CancellationToken cancellationToken = default)
        {
            var tokenResponse = await _upstoxService.FetchTokenFromUpstoxAsync(code);

            if (tokenResponse is null)
                throw new InvalidOperationException("Failed to fetch token from Upstox");

            var existing = await _userRepository
                .FirstOrDefaultAsync(x => x.UserId == tokenResponse.UserId, cancellationToken);

            if (existing is not null)
            {
                existing.AccessToken = tokenResponse.AccessToken;
                existing.ExtendedToken = tokenResponse.ExtendedToken; 
                existing.UpdatedOn = DateTime.UtcNow;
                await _userRepository.UpdateAsync(existing);
            }
            else
            {
                var user = new UserProfile
                {
                    Email = tokenResponse?.Email ?? string.Empty,
                    AccessToken = tokenResponse?.AccessToken ?? string.Empty,
                    ExtendedToken = tokenResponse?.ExtendedToken ?? string.Empty,
                    UserId = tokenResponse?.UserId ?? string.Empty,
                    UserName = tokenResponse?.UserName ?? string.Empty,
                    Exchanges = tokenResponse?.Exchanges.ToArray() ?? Array.Empty<string>(),
                    Products = tokenResponse?.Products.ToArray() ?? Array.Empty<string>(),
                    OrderTypes = tokenResponse?.OrderTypes.ToArray() ?? Array.Empty<string>(),
                    Broker = tokenResponse?.Broker ?? string.Empty,
                    UserType = tokenResponse?.UserType ?? string.Empty,
                    IsActive = tokenResponse?.IsActive ?? true,
                    Ddpi = tokenResponse?.Ddpi ?? true,
                    Poa = tokenResponse?.Poa ?? false,
                };

                await _userRepository.InsertAsync(user, cancellationToken);
            }

            await _userRepository.SaveAsync(cancellationToken);
        }
    }
}
