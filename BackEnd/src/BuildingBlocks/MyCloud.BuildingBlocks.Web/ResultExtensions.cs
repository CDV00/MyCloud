using Microsoft.AspNetCore.Http;
using MyCloud.BuildingBlocks.Domain.Results;

namespace MyCloud.BuildingBlocks.Web;

public static class ResultExtensions
{
    /// <summary>Chuyển Result thất bại thành ProblemDetails 400 (kèm danh sách lỗi validation nếu có).</summary>
    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Cannot convert a successful result to a problem.");
        }

        var extensions = new Dictionary<string, object?> { ["code"] = result.Error.Code };
        if (result is IValidationResult validation)
        {
            extensions["errors"] = validation.Errors;
        }

        return Results.Problem(
            title: result is IValidationResult ? "Validation Error" : "Bad Request",
            detail: result.Error.Message,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: extensions);
    }

    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.ToProblem();

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();
}
