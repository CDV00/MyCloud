using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace MyCloud.BuildingBlocks.Grpc;

/// <summary>
/// Chuyển tiếp header <c>Authorization</c> của request đang xử lý sang lời gọi gRPC: service đích tự validate
/// JWT của chính người dùng (docs/04 §4), không có "tin tưởng mặc định" giữa các service.
/// </summary>
public sealed class ForwardAuthorizationHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? authorization = accessor.HttpContext?.Request.Headers[HeaderNames.Authorization];
        if (!string.IsNullOrEmpty(authorization) && !request.Headers.Contains(HeaderNames.Authorization))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
