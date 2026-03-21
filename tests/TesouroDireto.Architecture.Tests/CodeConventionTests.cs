using System.Reflection;
using FluentAssertions;
using FluentAssertions.Execution;
using MediatR;

namespace TesouroDireto.Architecture.Tests;

public sealed class CodeConventionTests
{
    private static readonly Assembly ApplicationAssembly = typeof(Application.Importacao.ImportCsvCommand).Assembly;

    [Fact]
    public void Handlers_ShouldHaveSingleConstructor()
    {
        var handlerTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract
                && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)));

        using var scope = new AssertionScope();

        foreach (var handler in handlerTypes)
        {
            var constructors = handler.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            constructors.Should().HaveCount(1,
                "handler {0} should use a primary constructor (single constructor)",
                handler.Name);
        }
    }

    [Fact]
    public void Commands_ShouldBeRecords()
    {
        var commandTypes = ApplicationAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract
                && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

        using var scope = new AssertionScope();

        foreach (var command in commandTypes)
        {
            var isRecord = command.GetMethod("<Clone>$") is not null;

            isRecord.Should().BeTrue(
                "command {0} should be a record",
                command.Name);
        }
    }

    [Fact]
    public void ApplicationClasses_ShouldBeSealed()
    {
        var nonSealedClasses = ApplicationAssembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract && !t.IsNested
                && !t.IsInterface
                && !IsStaticClass(t))
            .Where(t => !t.IsSealed)
            .Select(t => t.FullName)
            .ToList();

        nonSealedClasses.Should().BeEmpty(
            "all Application classes must be sealed, but found: {0}",
            string.Join(", ", nonSealedClasses));
    }

    [Fact]
    public void RepositoryInterfaces_ShouldReturnResult()
    {
        var repositoryInterfaces = ApplicationAssembly.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic
                && t.Name.EndsWith("Repository", StringComparison.Ordinal));

        var resultType = typeof(Domain.Common.Result);
        var resultGenericType = typeof(Domain.Common.Result<>);

        using var scope = new AssertionScope();

        foreach (var repoInterface in repositoryInterfaces)
        {
            var methods = repoInterface.GetMethods();

            foreach (var method in methods)
            {
                var returnType = method.ReturnType;

                var isValid = IsResultReturnType(returnType, resultType, resultGenericType);

                isValid.Should().BeTrue(
                    "method {0}.{1} should return Task<Result> or Task<Result<T>>, but returns {2}",
                    repoInterface.Name, method.Name, returnType.Name);
            }
        }
    }

    private static bool IsResultReturnType(Type returnType, Type resultType, Type resultGenericType)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return false;
        }

        var taskInner = returnType.GetGenericArguments()[0];

        if (taskInner == resultType)
        {
            return true;
        }

        return taskInner.IsGenericType && taskInner.GetGenericTypeDefinition() == resultGenericType;
    }

    private static bool IsStaticClass(Type type) =>
        type.IsAbstract && type.IsSealed;
}
