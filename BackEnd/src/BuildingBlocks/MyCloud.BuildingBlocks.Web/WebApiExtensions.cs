using System.Reflection;
using Asp.Versioning;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyCloud.BuildingBlocks.Application.Abstractions;
using MyCloud.BuildingBlocks.Authentication;
using Scalar.AspNetCore;

namespace MyCloud.BuildingBlocks.Web;

public static class WebApiExtensions
{
    /// <summary>
    /// ProblemDetails, xử lý exception, xác thực JWT (mặc định mọi endpoint yêu cầu đăng nhập), ICurrentUser,
    /// API versioning, OpenAPI và Carter.
    /// </summary>
    /// <param name="builder">Host builder.</param>
    /// <param name="endpointsAssembly">
    /// Assembly chứa các ICarterModule của service. Phải đăng ký tường minh: Carter mặc định chỉ quét
    /// assembly tham chiếu TRỰC TIẾP gói Carter, còn service chỉ tham chiếu gián tiếp qua BuildingBlocks.Web
    /// (hậu quả: mọi endpoint trả 404 — đã được integration test phát hiện).
    /// </param>
    public static IHostApplicationBuilder AddWebApiDefaults(this IHostApplicationBuilder builder, Assembly endpointsAssembly)
    {
        IServiceCollection services = builder.Services;

        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        builder.AddJwtAuthentication();

        services
            .AddApiVersioning(o =>
            {
                o.DefaultApiVersion = new ApiVersion(1);
                o.ReportApiVersions = true;
            })
            .AddApiExplorer(o =>
            {
                o.GroupNameFormat = "'v'VVV";
                o.SubstituteApiVersionInUrl = true;
            })
            .AddOpenApi(o => o.Document.AddDocumentTransformer<BearerSecuritySchemeTransformer>()); // /openapi/v1.json

        Type[] modules = endpointsAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ICarterModule).IsAssignableFrom(t))
            .ToArray();
        services.AddCarter(configurator: c => c
            .WithModules(modules)
            .WithEmptyValidators()); // validator do FluentValidation DI quản lý (AddApplicationDefaults)
        return builder;
    }

    public static WebApplication MapWebApiDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapCarter();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();
            app.MapScalarApiReference().AllowAnonymous(); // UI: /scalar
        }

        return app;
    }
}
