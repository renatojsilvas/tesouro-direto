using FluentAssertions;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Domain.Tests.PrecosTaxas;

public sealed class PrecoTaxaTests
{
    private static readonly Guid TituloId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_ShouldReturnSuccess()
    {
        var result = CreateValid();

        result.IsSuccess.Should().BeTrue();
        result.Value.TituloId.Should().Be(TituloId);
        result.Value.DataBase.Value.Should().Be(new DateOnly(2024, 6, 15));
        result.Value.TaxaCompra.Value.Should().Be(10.50m);
        result.Value.TaxaVenda.Value.Should().Be(10.75m);
        result.Value.PuCompra.Value.Should().Be(1000.123456m);
        result.Value.PuVenda.Value.Should().Be(999.654321m);
        result.Value.PuBase.Value.Should().Be(998.111111m);
    }

    [Fact]
    public void Create_ShouldGenerateNonEmptyId()
    {
        var preco = CreateValid().Value;

        preco.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_WithEmptyTituloId_ShouldReturnFailure()
    {
        var result = PrecoTaxa.Create(
            Guid.Empty,
            DataBase.Create(new DateOnly(2024, 6, 15)).Value,
            Taxa.Create(10.50m).Value,
            Taxa.Create(10.75m).Value,
            PrecoUnitario.Create(1000m).Value,
            PrecoUnitario.Create(999m).Value,
            PrecoUnitario.Create(998m).Value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PrecoTaxa.InvalidTituloId");
    }

    [Fact]
    public void TwoPrecoTaxas_WithDifferentIds_ShouldNotBeEqual()
    {
        var p1 = CreateValid().Value;
        var p2 = CreateValid().Value;

        p1.Should().NotBe(p2);
    }

    [Fact]
    public void Create_WithNullDataBase_ShouldThrowArgumentNullException()
    {
        var act = () => PrecoTaxa.Create(
            TituloId,
            null!,
            Taxa.Create(10m).Value,
            Taxa.Create(11m).Value,
            PrecoUnitario.Create(1000m).Value,
            PrecoUnitario.Create(999m).Value,
            PrecoUnitario.Create(998m).Value);

        act.Should().Throw<ArgumentNullException>();
    }

    private static Domain.Common.Result<PrecoTaxa> CreateValid()
    {
        return PrecoTaxa.Create(
            TituloId,
            DataBase.Create(new DateOnly(2024, 6, 15)).Value,
            Taxa.Create(10.50m).Value,
            Taxa.Create(10.75m).Value,
            PrecoUnitario.Create(1000.123456m).Value,
            PrecoUnitario.Create(999.654321m).Value,
            PrecoUnitario.Create(998.111111m).Value);
    }
}
