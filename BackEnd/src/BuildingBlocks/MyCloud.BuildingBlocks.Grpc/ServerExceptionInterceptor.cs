using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using MyCloud.BuildingBlocks.Domain.Exceptions;

namespace MyCloud.BuildingBlocks.Grpc;

/// <summary>Chuyển exception nghiệp vụ thành gRPC status chuẩn; không làm lộ chi tiết lỗi hệ thống.</summary>
public sealed class ServerExceptionInterceptor(ILogger<ServerExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            throw ToRpcException(ex, context.Method);
        }
    }

    public RpcException ToRpcException(Exception exception, string method)
    {
        StatusCode code = MapStatusCode(exception);
        if (code == StatusCode.Internal)
        {
            logger.LogError(exception, "Unhandled exception in gRPC method {Method}", method);
            return new RpcException(new Status(code, "An internal error occurred."));
        }

        // Mã lỗi ổn định (vd STORAGE.QUOTA_EXCEEDED) đi kèm trong trailer để phía gọi chuyển tiếp cho client.
        var trailers = new Metadata();
        if (exception is DomainException { Code: { } errorCode })
        {
            trailers.Add(ErrorCodeTrailer, errorCode);
        }

        return new RpcException(new Status(code, exception.Message), trailers);
    }

    /// <summary>Tên trailer chứa mã lỗi nghiệp vụ.</summary>
    public const string ErrorCodeTrailer = "x-error-code";

    public static StatusCode MapStatusCode(Exception exception) => exception switch
    {
        NotFoundException => StatusCode.NotFound,
        BadRequestException => StatusCode.InvalidArgument,
        ConflictException => StatusCode.FailedPrecondition,
        PreconditionFailedException => StatusCode.Aborted,
        LimitExceededException => StatusCode.ResourceExhausted,
        UnauthorizedAccessException => StatusCode.PermissionDenied,
        OperationCanceledException => StatusCode.Cancelled,
        _ => StatusCode.Internal,
    };
}
