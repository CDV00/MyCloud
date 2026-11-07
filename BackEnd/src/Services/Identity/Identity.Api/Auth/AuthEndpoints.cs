using Carter;
using FluentValidation;
using Identity.Api.Data;
using MyCloud.BuildingBlocks.Domain.Results;
using MyCloud.BuildingBlocks.Web;

namespace Identity.Api.Auth;

public sealed class AuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.NewVersionedApi("Auth")
            .MapGroup("/api/v{version:apiVersion}/auth")
            .HasApiVersion(1);

        group.MapPost("/register", RegisterAsync).WithName("Register");
        group.MapPost("/login", LoginAsync).WithName("Login");
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IValidator<RegisterRequest> validator,
        AccountService accounts,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        Result<Guid> result = await accounts.RegisterAsync(request, ct);
        if (result.IsFailure)
        {
            return result.Error == IdentityErrors.EmailTaken
                ? Results.Problem(title: "Conflict", detail: result.Error.Message, statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code })
                : result.ToProblem();
        }

        return Results.Created($"/api/v1/users/{result.Value}", new { id = result.Value });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        AccountService accounts,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        Result<TokenResponse> result = await accounts.LoginAsync(request);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(title: "Unauthorized", detail: result.Error.Message, statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
    }

    private static IResult ToValidationProblem(FluentValidation.Results.ValidationResult validation) =>
        ValidationResult.WithErrors(validation.Errors.Select(e => new Error(e.PropertyName, e.ErrorMessage)).ToArray()).ToProblem();
}

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(AppUser.DisplayNameMaxLength);
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
