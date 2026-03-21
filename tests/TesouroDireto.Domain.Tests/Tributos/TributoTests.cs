using FluentAssertions;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Domain.Tests.Tributos;

public sealed class TributoTests
{
    [Fact]
    public void Create_WithValidData_ShouldReturnSuccess()
    {
        var faixas = new[]
        {
            Faixa.Create(0, 180, null, 22.5m).Value,
            Faixa.Create(181, 360, null, 20m).Value,
            Faixa.Create(361, 720, null, 17.5m).Value,
            Faixa.Create(721, null, null, 15m).Value
        };

        var result = Tributo.Create("Imposto de Renda", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, ordem: 1, cumulativo: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Nome.Should().Be("Imposto de Renda");
        result.Value.BaseCalculo.Should().Be(BaseCalculo.Rendimento);
        result.Value.TipoCalculo.Should().Be(TipoCalculo.FaixaPorDias);
        result.Value.Faixas.Should().HaveCount(4);
        result.Value.Ativo.Should().BeTrue();
        result.Value.Ordem.Should().Be(1);
        result.Value.Cumulativo.Should().BeFalse();
    }

    [Fact]
    public void Create_WithEmptyNome_ShouldReturnFailure()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };

        var result = Tributo.Create("", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.InvalidNome");
    }

    [Fact]
    public void Create_WithEmptyFaixas_ShouldReturnFailure()
    {
        var result = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, Array.Empty<Faixa>(), 1, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.NoFaixas");
    }

    [Fact]
    public void Create_WithAliquotaFixa_ShouldReturnSuccess()
    {
        var faixas = new[] { Faixa.Create(null, null, 1, 0.25m).Value };

        var result = Tributo.Create("Taxa B3", BaseCalculo.PuBruto, TipoCalculo.AliquotaFixa, faixas, 2, false);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_ShouldGenerateNonEmptyId()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        tributo.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Desativar_ShouldSetAtivoToFalse()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        tributo.Desativar();

        tributo.Ativo.Should().BeFalse();
    }

    [Fact]
    public void Ativar_ShouldSetAtivoToTrue()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        tributo.Desativar();
        tributo.Ativar();

        tributo.Ativo.Should().BeTrue();
    }

    [Fact]
    public void AtualizarFaixas_ShouldReplaceExistingFaixas()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        var novasFaixas = new[]
        {
            Faixa.Create(0, 360, null, 20m).Value,
            Faixa.Create(361, null, null, 15m).Value
        };

        var result = tributo.AtualizarFaixas(novasFaixas);

        result.IsSuccess.Should().BeTrue();
        tributo.Faixas.Should().HaveCount(2);
    }

    [Fact]
    public void AtualizarFaixas_WithEmpty_ShouldReturnFailure()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };
        var tributo = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, 1, false).Value;

        var result = tributo.AtualizarFaixas(Array.Empty<Faixa>());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_WithNegativeOrdem_ShouldReturnFailure()
    {
        var faixas = new[] { Faixa.Create(0, 180, null, 22.5m).Value };

        var result = Tributo.Create("IR", BaseCalculo.Rendimento, TipoCalculo.FaixaPorDias, faixas, -1, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tributo.InvalidOrdem");
    }
}
