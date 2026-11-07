using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MyCloud.BuildingBlocks.Domain.Exceptions;

namespace MyCloud.BuildingBlocks.Web;

/// <summary>Chuyển exception thành ProblemDetails (RFC 9457). Lỗi hệ thống không lộ chi tiết ra client.</summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string title, string? detail) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, e.Title, e.Message),
            BadRequestException e => (StatusCodes.Status400BadRequest, e.Title, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Title, e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Title, e.Message),
            PreconditionFailedException e => (StatusCodes.Status412PreconditionFailed, e.Title, e.Message),
            LimitExceededException e => (StatusCodes.Status413PayloadTooLarge, e.Title, e.Message),
            DomainException e => (StatusCodes.Status400BadRequest, e.Title, e.Message),
            BadHttpRequestException e => (e.StatusCode, "Bad Request", e.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", null),
            _ => (StatusCodes.Status500InternalServerError, "Server Error", null),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = Problem(exception, status, title, detail),
        });
    }

    private static ProblemDetails Problem(Exception exception, int status, string title, string? detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        if (exception is DomainException { Code: { } code })
        {
            problem.Extensions["code"] = code;
        }

        return problem;
    }
}
