using Grpc.AspNetCore.Server;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using MyCloud.BuildingBlocks.Authentication;

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
    /// gRPC client gọi tới service khác. <paramref name="serviceUri"/> dạng <c>http://_grpc.storage</c> — endpoint
    /// tên "grpc" của service "storage", phân giải qua service discovery (Aspire/Kubernetes).
    /// Deadline mặc định 2 s; chuyển tiếp token của người dùng; KHÔNG retry tự động ở tầng HTTP
    /// (lệnh ghi như CompleteUpload không được gửi lặp — docs/04 §2.3).
    /// </summary>
    public static IHttpClientBuilder AddGrpcClientDefaults<TClient>(this IServiceCollection services, Uri serviceUri)
        where TClient : class
    {
        services.AddHttpContextAccessor();
        services.AddTransient<ForwardAuthorizationHandler>();

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers còn ở dạng thử nghiệm; thay thế khi API ổn định
        return services
            .AddGrpcClient<TClient>(o => o.Address = serviceUri)
            .AddInterceptor(InterceptorScope.Client, () => new ClientDeadlineInterceptor())
            .AddHttpMessageHandler<ForwardAuthorizationHandler>()
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }

    /// <summary>
    /// Như <see cref="AddGrpcClientDefaults{TClient}"/> nhưng gọi bằng <b>định danh của service</b> (client credentials)
    /// thay vì token người dùng — cho các lời gọi thay mặt người khác (chủ sở hữu file, khách mở link). Service phải gọi
    /// <c>AddServiceIdentity()</c>.
    /// </summary>
    public static IHttpClientBuilder AddGrpcServiceClient<TClient>(this IServiceCollection services, Uri serviceUri)
        where TClient : class
    {
#pragma warning disable EXTEXP0001
        return services
            .AddGrpcClient<TClient>(o => o.Address = serviceUri)
            .AddInterceptor(InterceptorScope.Client, () => new ClientDeadlineInterceptor())
            .AddHttpMessageHandler<ServiceTokenHandler>()
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }
}
