using System.Reflection;
using FluentAssertions;
using FluentAssertions.Execution;
using TesouroDireto.API.Http;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Application.Simulador;
using TesouroDireto.Application.Tributos;
using TesouroDireto.Web.Contracts;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Architecture.Tests;

public sealed class ContractParityTests
{
    private static readonly Assembly WebAssembly = typeof(TituloItem).Assembly;

    private static readonly Dictionary<Type, Type> PairMap = new()
    {
        [typeof(TituloItem)] = typeof(TituloResource),
        [typeof(HateoasLinkDto)] = typeof(HateoasLink),
        [typeof(SimulacaoResult)] = typeof(SimulacaoResultadoDto),
        [typeof(TributoResult)] = typeof(TributoAplicadoDto),
        [typeof(CupomResult)] = typeof(FluxoCupomDto),
        [typeof(ProjecaoUtilizadaResult)] = typeof(ProjecaoUtilizadaDto),
        [typeof(PrecoItem)] = typeof(PrecoTaxaDto),
        [typeof(TributoItem)] = typeof(TributoDto),
        [typeof(FaixaItem)] = typeof(FaixaDto),
        [typeof(CenarioResult)] = typeof(CenarioResultadoDto),
    };

    [Fact]
    public void WebContracts_ShouldBeSubsetOfApiContracts()
    {
        using var scope = new AssertionScope();

        var webContractTypes = WebAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsNested
                && t.Namespace == "TesouroDireto.Web.Contracts"
                && IsRecord(t))
            .ToList();

        var naoPareados = webContractTypes
            .Where(t => !PairMap.ContainsKey(t))
            .Select(t => $"DTO do Web '{t.Name}' não pareado — adicione ao mapa de paridade")
            .ToList();

        naoPareados.Should().BeEmpty();

        var falhas = new List<string>();

        foreach (var (webType, apiType) in PairMap)
        {
            var webProps = webType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var apiProps = apiType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var webProp in webProps)
            {
                var apiProp = apiProps.FirstOrDefault(p =>
                    string.Equals(p.Name, webProp.Name, StringComparison.OrdinalIgnoreCase));

                if (apiProp is null)
                {
                    falhas.Add(
                        $"Web {webType.Name}.{webProp.Name} ({FriendlyName(webProp.PropertyType)}) não existe em {apiType.Name}");
                    continue;
                }

                if (!TiposCompativeis(webProp.PropertyType, apiProp.PropertyType))
                {
                    falhas.Add(
                        $"tipo divergente: Web {webType.Name}.{webProp.Name}:{FriendlyName(webProp.PropertyType)} vs API {apiType.Name}:{FriendlyName(apiProp.PropertyType)}");
                }
            }
        }

        falhas.Should().BeEmpty();
    }

    private static bool TiposCompativeis(Type webType, Type apiType)
    {
        webType = UnwrapNullable(webType);
        apiType = UnwrapNullable(apiType);

        var webDictValue = GetDictionaryValueType(webType);
        var apiDictValue = GetDictionaryValueType(apiType);
        if (webDictValue is not null && apiDictValue is not null)
        {
            return TiposCompativeis(webDictValue, apiDictValue);
        }

        if (webType != typeof(string) && apiType != typeof(string))
        {
            var webElement = GetEnumerableElementType(webType);
            var apiElement = GetEnumerableElementType(apiType);
            if (webElement is not null && apiElement is not null)
            {
                return TiposCompativeis(webElement, apiElement);
            }
        }

        if (webType == apiType)
        {
            return true;
        }

        if (apiType.IsEnum && webType == typeof(string))
        {
            return true;
        }

        if (IsRecord(webType) && IsRecord(apiType))
        {
            return PairMap.TryGetValue(webType, out var mappedApiType) && mappedApiType == apiType;
        }

        return false;
    }

    private static Type UnwrapNullable(Type type) =>
        Nullable.GetUnderlyingType(type) ?? type;

    private static Type? GetDictionaryValueType(Type type)
    {
        var dictInterface = type.GetInterfaces().Prepend(type)
            .FirstOrDefault(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

        return dictInterface?.GetGenericArguments()[1];
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        var enumerableInterface = type.GetInterfaces().Prepend(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments()[0];
    }

    private static string FriendlyName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return FriendlyName(underlying) + "?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var genericArgs = string.Join(", ", type.GetGenericArguments().Select(FriendlyName));
        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{genericArgs}>";
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$") is not null;
}
