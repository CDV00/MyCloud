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
            DomainException e => (StatusCodes.Status400BadRequest, e.Title, e.Message),
            BadHttpRequestException e => (e.StatusCode, "Bad Request", e.Message),
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
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
