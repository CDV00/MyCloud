using Microsoft.AspNetCore.Http;
using MyCloud.BuildingBlocks.Domain.Results;
using FluentValidationResult = FluentValidation.Results.ValidationResult;

namespace MyCloud.BuildingBlocks.Web;

public static class ValidationProblemExtensions
{
    /// <summary>Kết quả FluentValidation thất bại → ProblemDetails 400 cùng định dạng với pipeline MediatR.</summary>
    public static IResult ToProblem(this FluentValidationResult validation) =>
        ValidationResult.WithErrors(validation.Errors.Select(e => new Error(e.PropertyName, e.ErrorMessage)).ToArray()).ToProblem();
}
