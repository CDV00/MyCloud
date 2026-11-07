using Grpc.Core;
using Grpc.Core.Interceptors;

namespace MyCloud.BuildingBlocks.Grpc;

/// <summary>Đặt deadline mặc định cho lời gọi gRPC nếu caller chưa đặt (docs/04 §2.3).</summary>
public sealed class ClientDeadlineInterceptor(TimeSpan defaultDeadline) : Interceptor
{
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(2);

    public ClientDeadlineInterceptor()
        : this(DefaultDeadline)
    {
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (context.Options.Deadline is null)
        {
            var options = context.Options.WithDeadline(DateTime.UtcNow.Add(defaultDeadline));
            context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
        }

        return continuation(request, context);
    }
}
