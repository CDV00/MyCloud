using Auth.Api.Database.Models;
using Auth.Api.Infrastructure.Authentication;
using Auth.Api.Services.Dtos;
using Microsoft.AspNetCore.Identity;
using Result = MyDrive.BuidingBlock.Contract.Abstractions.Shared.Result;
using System.Security.Claims;
using MyDrive.BuidingBlock.Contract.Abstractions.Shared;
using AutoMapper;
using Auth.Api.Database.Asbtractions;

namespace Auth.Api.Services;

public interface IAccoutService
{
    Task<Result<AuthenticatedDto>> Login(LoginRequestDto request);
    Task<Result> Register(RegisterRequest registerRequest);
}

public class AccoutService : IAccoutService
{
    private readonly UserManager<AppUser> _userManager;
    //private readonly SignInManager<AppUser> _signInManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IMapper _mapper;
    //private readonly ICacheService _cacheService;
    //private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<AppUser,Guid> _repository;
    public AccoutService(UserManager<AppUser> userManager, IRepository<AppUser, Guid> repository ,/*SignInManager<AppUser> signInManager,*/ IJwtTokenService jwtTokenService, IMapper mapper/*, ICacheService cacheService*/)
    {
        _userManager = userManager;
        //_signInManager = signInManager;
        _jwtTokenService = jwtTokenService;
        //_cacheService = cacheService;
        _mapper = mapper;
        _repository = repository;
    }

    public async Task<Result<AuthenticatedDto>> Login(LoginRequestDto request)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(request.UserName) ?? await _userManager.FindByNameAsync(request.UserName);
            if (user is not null)
            {
                if (await _userManager.GetAccessFailedCountAsync(user) >= 5)
                    throw new Exception("Can,t find User");
            }
            else
            {
                throw new Exception("Can't find User " + request.UserName);
            }
            var checkLogin = await _userManager.CheckPasswordAsync(user, request.Password);

            if (!checkLogin)
            {
                await _userManager.AccessFailedAsync(user);
                throw new Exception("Can,t find User");
            }
            else
            {
                await _userManager.ResetAccessFailedCountAsync(user);
            }

            var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, "Senior Leader"),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
        };
            var accessToken = _jwtTokenService.GenerateAccessToken(claims);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            var response = new AuthenticatedDto()
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                RefreshTokenExpiryTime = DateTime.Now.AddMinutes(5)
            };

            //await _cacheService.SetAsync(user.Email, response);

            return Result.Success(response);
        }
        catch (Exception ex)
        {

            throw;
        }

    }

    public async Task<Result> Register(RegisterRequest registerRequest)
    {

        try
        {
            AppUser? user = await _userManager.FindByEmailAsync(registerRequest.Email);
            if (user is not null)
            {
                return Result.Failure(new Error("ERROR.DataExisted", $"User {user.Email} is existed"));
            }

            _mapper.Map(registerRequest, user);
            //user.FullName = user.FirstName + " " + user.LastName;
            //user.Description = registerRequest.Description;
            //GetAvartarUser(user);
            //user.UpdatedAt = DateTime.Now;

            var result = await _userManager.CreateAsync(user, registerRequest.Password);
            //await _userManager.AddPasswordAsync(user, registerRequest.Password);

            //if (!result.Succeeded)
            //{
            //    return new BaseResponse(false, result.Errors.ToList()[0].Description, result.Errors.ToList()[0].Code);
            //}

            //if (registerRequest.CategoryId == null)
            //{
            //    await AddStudentRole(user);
            //}

            //if (registerRequest.CategoryId != null)
            //{
            //    await AddInstructorRole(user);
            //}

            //var userResponse = _mapper.Map<UserDTO>(user);

            //var roles = await _userManager.GetRolesAsync(user);
            //userResponse.Role = string.Join(",", roles);

            //return new BaseResponse(true);

            return Result.Success();
        }
        catch (Exception ex)
        {

            throw;
        }
    }

}
