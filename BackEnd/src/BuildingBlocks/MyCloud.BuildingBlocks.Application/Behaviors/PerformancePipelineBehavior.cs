using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MyCloud.BuildingBlocks.Application.Behaviors;

/// <summary>Cảnh báo request chạy lâu. Trace chi tiết do OpenTelemetry đảm nhiệm.</summary>
public sealed class PerformancePipelineBehavior<TRequest, TResponse>(ILogger<PerformancePipelineBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(500);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        TResponse response = await next(cancellationToken);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        if (elapsed > Threshold)
        {
            // Không log nguyên request vì có thể chứa dữ liệu cá nhân.
            logger.LogWarning("Long running request {RequestName} took {ElapsedMilliseconds} ms",
                typeof(TRequest).Name, (long)elapsed.TotalMilliseconds);
        }

        return response;
    }
}
