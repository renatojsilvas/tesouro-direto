using System.Text;
using FluentAssertions;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class ConditionalGetStoreTests
{
    private static ConditionalCacheEntry CreateEntry(string suffix)
        => new($"\"etag-{suffix}\"", Encoding.UTF8.GetBytes($"body-{suffix}"), new Dictionary<string, string>());

    [Fact]
    public void Set_QuandoChaveNova_TryGetDevolveEntradaCorrespondente()
    {
        var store = new BoundedConditionalGetStore();
        var entry = CreateEntry("1");

        store.Set("uri/a", entry);

        var found = store.TryGet("uri/a", out var retrieved);

        found.Should().BeTrue();
        retrieved.Should().Be(entry);
    }

    [Fact]
    public void TryGet_QuandoChaveAusente_DevolveFalse()
    {
        var store = new BoundedConditionalGetStore();

        var found = store.TryGet("uri/inexistente", out var retrieved);

        found.Should().BeFalse();
        retrieved.Should().BeNull();
    }

    [Fact]
    public void Set_QuandoChaveJaExiste_SubstituiEntradaAnterior()
    {
        var store = new BoundedConditionalGetStore();
        store.Set("uri/a", CreateEntry("1"));
        var novaEntrada = CreateEntry("2");

        store.Set("uri/a", novaEntrada);

        store.TryGet("uri/a", out var retrieved);
        retrieved.Should().Be(novaEntrada);
    }

    [Fact]
    public void Set_AlemDaCapacidade_MantemContagemLimitadaEEvictaAsEntradasMaisAntigas()
    {
        var store = new BoundedConditionalGetStore();
        const int cap = 128;
        const int extra = 10;

        for (var i = 0; i < cap + extra; i++)
        {
            store.Set($"uri/{i}", CreateEntry(i.ToString()));
        }

        var totalPresentes = 0;
        for (var i = 0; i < cap + extra; i++)
        {
            if (store.TryGet($"uri/{i}", out _))
            {
                totalPresentes++;
            }
        }

        totalPresentes.Should().Be(cap);

        for (var i = 0; i < extra; i++)
        {
            store.TryGet($"uri/{i}", out _).Should().BeFalse();
        }

        for (var i = extra; i < cap + extra; i++)
        {
            store.TryGet($"uri/{i}", out _).Should().BeTrue();
        }
    }
}
