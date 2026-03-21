using FluentAssertions;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Domain.Tests.PrecosTaxas;

public sealed class TaxaTests
{
    [Fact]
    public void Create_WithValidValue_ShouldReturnTaxa()
    {
        var taxa = Taxa.Create(10.25m);

        taxa.Value.Should().Be(10.25m);
    }

    [Fact]
    public void Create_WithZero_ShouldReturnTaxa()
    {
        var taxa = Taxa.Create(0m);

        taxa.Value.Should().Be(0m);
    }

    [Fact]
    public void Create_WithNegativeValue_ShouldReturnTaxa()
    {
        var taxa = Taxa.Create(-1m);

        taxa.Value.Should().Be(-1m);
    }

    [Fact]
    public void SameValue_ShouldBeEqual()
    {
        var t1 = Taxa.Create(5.50m);
        var t2 = Taxa.Create(5.50m);

        t1.Should().Be(t2);
    }
}
