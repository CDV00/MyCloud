using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Yarp.ReverseProxy.Transforms;

namespace MyCloud.Gateway;

public static class GatewayLimits
{
    /// <summary>Body tối đa qua Gateway: file đi thẳng tới object storage bằng presigned URL (ADR-002).</summary>
    public const long DefaultMaxRequestBodyBytes = 1L * 1024 * 1024;

    /// <summary>
    /// Tạm thời cho upload qua Drive (multipart, ≤ 100 MB) tới khi Phase 2 chuyển sang presigned URL.
    /// </summary>
    public const long LegacyUploadMaxRequestBodyBytes = 110L * 1024 * 1024;
}

/// <summary>Nâng giới hạn body riêng cho route upload cũ của Drive. Xoá ở Phase 2.</summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.StartsWithSegments("/api/drive", StringComparison.OrdinalIgnoreCase)
            && context.Request.Path.Value!.EndsWith("/files", StringComparison.OrdinalIgnoreCase)
            && context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = GatewayLimits.LegacyUploadMaxRequestBodyBytes;
        }

        return next(context);
    }
}

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    /// <summary>Chấp nhận id của client nếu hợp lệ (để client đối chiếu log), nếu không thì sinh mới.</summary>
    public static string Resolve(string? fromClient) =>
        !string.IsNullOrEmpty(fromClient) && fromClient.Length <= MaxLength && fromClient.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? fromClient
            : Guid.CreateVersion7().ToString("N");
}

/// <summary>
/// Gắn <c>X-Correlation-Id</c> vào request đi tới service (YARP chuyển tiếp header), vào response và vào trace.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        string correlationId = CorrelationId.Resolve(context.Request.Headers[CorrelationId.HeaderName]);
        context.Request.Headers[CorrelationId.HeaderName] = correlationId;
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        return next(context);
    }
}

/// <summary>Header chỉ hệ thống nội bộ được đặt — client gửi lên thì bị xoá trước khi chuyển tiếp.</summary>
public static class InternalHeaders
{
    private static readonly string[] Exact = ["X-User-Id", "X-User-Email", "X-Forwarded-User"];
    private const string InternalPrefix = "X-Internal-";

    public static ValueTask RemoveFromClientRequest(RequestTransformContext context)
    {
        var headers = context.ProxyRequest.Headers;
        foreach (string name in Exact)
        {
            headers.Remove(name);
        }

        foreach (string name in headers.Select(h => h.Key).Where(k => k.StartsWith(InternalPrefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            headers.Remove(name);
        }

        return ValueTask.CompletedTask;
    }
}
