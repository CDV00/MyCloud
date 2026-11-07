using Grpc.AspNetCore.Server;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;

namespace MyCloud.BuildingBlocks.Grpc;

public static class GrpcExtensions
{
    /// <summary>gRPC server với interceptor map lỗi nghiệp vụ → status code.</summary>
    public static IGrpcServerBuilder AddGrpcServerDefaults(this IServiceCollection services) =>
        services.AddGrpc(o =>
        {
            o.Interceptors.Add<ServerExceptionInterceptor>();
            o.EnableDetailedErrors = false;
        });

    /// <summary>
    /// gRPC client gọi tới service khác. <paramref name="serviceUri"/> dạng <c>https://storage</c>
    /// — phân giải qua service discovery (Aspire/Kubernetes).
    /// </summary>
    public static IHttpClientBuilder AddGrpcClientDefaults<TClient>(this IServiceCollection services, Uri serviceUri)
        where TClient : class =>
        services
            .AddGrpcClient<TClient>(o => o.Address = serviceUri)
            .AddInterceptor(InterceptorScope.Client, () => new ClientDeadlineInterceptor());
}
