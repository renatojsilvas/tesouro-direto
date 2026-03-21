using System.Reflection;
using FluentAssertions;
using FluentAssertions.Execution;

namespace TesouroDireto.Architecture.Tests;

public sealed class DomainConventionTests
{
    private static readonly Assembly DomainAssembly = typeof(Domain.Common.Entity<>).Assembly;

    private static IEnumerable<Type> GetDomainClasses() =>
        DomainAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.IsNested);

    [Fact]
    public void AllDomainClasses_ShouldBeSealed()
    {
        var nonSealedClasses = GetDomainClasses()
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        nonSealedClasses.Should().BeEmpty(
            "all Domain classes must be sealed, but found: {0}",
            string.Join(", ", nonSealedClasses));
    }

    [Fact]
    public void DomainEntities_ShouldNotExposeListProperties()
    {
        var entityTypes = DomainAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract
                && t.BaseType is { IsGenericType: true }
                && t.BaseType.GetGenericTypeDefinition() == typeof(Domain.Common.Entity<>));

        using var scope = new AssertionScope();

        foreach (var entity in entityTypes)
        {
            var listProperties = entity.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                {
                    var propType = p.PropertyType;
                    if (propType.IsGenericType)
                    {
                        var genericDef = propType.GetGenericTypeDefinition();
                        return genericDef == typeof(List<>) || genericDef == typeof(IList<>);
                    }
                    return false;
                })
                .Select(p => $"{entity.Name}.{p.Name}")
                .ToList();

            listProperties.Should().BeEmpty(
                "Domain entities must use IReadOnlyCollection<T>, not List<T>");
        }
    }

    [Fact]
    public void DomainRecords_ShouldBeSealed()
    {
        var nonSealedRecords = DomainAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsNested
                && IsRecord(t)
                && !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        nonSealedRecords.Should().BeEmpty(
            "all Domain records must be sealed, but found: {0}",
            string.Join(", ", nonSealedRecords));
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$") is not null;
}
