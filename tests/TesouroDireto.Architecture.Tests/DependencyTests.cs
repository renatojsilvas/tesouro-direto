using System.Reflection;
using FluentAssertions;

namespace TesouroDireto.Architecture.Tests;

public sealed class DependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(Domain.Common.Entity<>).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.Importacao.ImportCsvCommand).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.Persistence.AppDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_ShouldNotReference_Application()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();

        referencedAssemblies
            .Should().NotContain(a => a.Name == ApplicationAssembly.GetName().Name,
                "Domain must not depend on Application");
    }

    [Fact]
    public void Domain_ShouldNotReference_Infrastructure()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();

        referencedAssemblies
            .Should().NotContain(a => a.Name == InfrastructureAssembly.GetName().Name,
                "Domain must not depend on Infrastructure");
    }

    [Fact]
    public void Domain_ShouldNotReference_Api()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();

        referencedAssemblies
            .Should().NotContain(a => a.Name == ApiAssembly.GetName().Name,
                "Domain must not depend on API");
    }

    [Fact]
    public void Application_ShouldNotReference_Infrastructure()
    {
        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();

        referencedAssemblies
            .Should().NotContain(a => a.Name == InfrastructureAssembly.GetName().Name,
                "Application must not depend on Infrastructure");
    }

    [Fact]
    public void Application_ShouldNotReference_Api()
    {
        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();

        referencedAssemblies
            .Should().NotContain(a => a.Name == ApiAssembly.GetName().Name,
                "Application must not depend on API");
    }

    [Fact]
    public void Infrastructure_ShouldNotReference_Api()
    {
        var referencedAssemblies = InfrastructureAssembly.GetReferencedAssemblies();

        referencedAssemblies
            .Should().NotContain(a => a.Name == ApiAssembly.GetName().Name,
                "Infrastructure must not depend on API");
    }
}
