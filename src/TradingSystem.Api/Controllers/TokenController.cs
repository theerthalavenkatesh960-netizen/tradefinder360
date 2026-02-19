using Microsoft.AspNetCore.Mvc;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Api.Controllers
{
    [Route("auth")]
    [ApiController]
    public class TokenController : ControllerBase
    {
        private readonly IUserProfileService _userProfileService;
        public TokenController(IUserProfileService userProfileService)
        {
            _userProfileService = userProfileService;
        }

        [HttpGet("callback")]
        public async Task<IActionResult> StoreToken([FromQuery] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest("Code missing");

            await _userProfileService.StoreUserTokenAsync(code);

            return Ok("Authorization code stored successfully.");
        }
    }
}
