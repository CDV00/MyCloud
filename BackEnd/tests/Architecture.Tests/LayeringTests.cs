using System.Reflection;
using Drive.Application;
using Drive.Infrastructure;
using MyCloud.BuildingBlocks.Domain.Primitives;
using MyCloud.Contracts.Events;
using NetArchTest.Rules;
using DriveDomainMarker = Drive.Domain.Entities.Folder;

namespace Architecture.Tests;

/// <summary>Giữ đúng hướng phụ thuộc của Clean Architecture và tính độc lập của hợp đồng.</summary>
public sealed class LayeringTests
{
    private static readonly Assembly DriveDomain = typeof(DriveDomainMarker).Assembly;
    private static readonly Assembly DriveApplicationAssembly = DriveApplication.Assembly;
    private static readonly Assembly DriveInfrastructure = typeof(DependencyInjection).Assembly;

    [Fact]
    public void DriveDomain_DoesNotDependOn_OuterLayersOrFrameworks()
    {
        var result = Types.InAssembly(DriveDomain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Drive.Application",
                "Drive.Infrastructure",
                "Drive.Api",
                "Microsoft.EntityFrameworkCore",
                "MassTransit",
                "MediatR",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void DriveApplication_DoesNotDependOn_InfrastructureOrFrameworks()
    {
        var result = Types.InAssembly(DriveApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Drive.Infrastructure",
                "Drive.Api",
                "Microsoft.EntityFrameworkCore",
                "MassTransit",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void DriveInfrastructure_DoesNotDependOn_Api()
    {
        var result = Types.InAssembly(DriveInfrastructure)
            .ShouldNot()
            .HaveDependencyOn("Drive.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void BuildingBlocksDomain_ReferencesNoPackages()
    {
        AssertOnlyFrameworkReferences(typeof(Entity<>).Assembly);
    }

    [Fact]
    public void ContractsEvents_ReferencesNoPackages()
    {
        // Hợp đồng event được mọi service dùng chung: không được kéo theo thư viện nào.
        AssertOnlyFrameworkReferences(typeof(IntegrationEvent).Assembly);
    }

    [Fact]
    public void ContractsEvents_AreSealedRecordsDerivingFromIntegrationEvent()
    {
        var eventTypes = typeof(IntegrationEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsPublic);

        foreach (Type type in eventTypes)
        {
            type.IsSubclassOf(typeof(IntegrationEvent)).ShouldBeTrue($"{type.FullName} must derive from IntegrationEvent");
            type.IsSealed.ShouldBeTrue($"{type.FullName} must be sealed");
        }
    }

    private static void AssertOnlyFrameworkReferences(Assembly assembly)
    {
        string[] nonFramework = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                        && n != "netstandard"
                        && n != "mscorlib"
                        && n != "MyCloud.BuildingBlocks.Domain")
            .ToArray();

        nonFramework.ShouldBeEmpty($"{assembly.GetName().Name} references: {string.Join(", ", nonFramework)}");
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful ? string.Empty : "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
