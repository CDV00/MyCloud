using Auth.Api.Services;
using Auth.Api.Services.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Auth.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class AuthorizationController : ControllerBase {

        private readonly ILogger<AuthorizationController> _logger;
        private readonly IAccoutService _accoutService;

        public AuthorizationController(ILogger<AuthorizationController> logger, IAccoutService accoutService) {
            _logger = logger;
            _accoutService = accoutService;
        }

        [HttpPost]
        public async Task<IResult> Login(LoginRequestDto request) {

            var data = await _accoutService.Login(request);
            

            return Results.Ok(data);
        }
        [HttpPost]
        public async Task<IResult> Register(RegisterRequest request)
        {
            var data = await _accoutService.Register(request);
            return Results.Ok(data);
        }
    }
}
