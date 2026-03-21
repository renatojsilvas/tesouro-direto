using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Tributos;

public sealed class Tributo : Entity<Guid>
{
    private readonly List<Faixa> _faixas = [];

#pragma warning disable CS8618 // EF Core requires parameterless constructor
    private Tributo() : base(Guid.Empty) { }
#pragma warning restore CS8618

    private Tributo(
        Guid id,
        string nome,
        BaseCalculo baseCalculo,
        TipoCalculo tipoCalculo,
        List<Faixa> faixas,
        int ordem,
        bool cumulativo)
        : base(id)
    {
        Nome = nome;
        BaseCalculo = baseCalculo;
        TipoCalculo = tipoCalculo;
        _faixas = faixas;
        Ativo = true;
        Ordem = ordem;
        Cumulativo = cumulativo;
    }

    public string Nome { get; }
    public BaseCalculo BaseCalculo { get; }
    public TipoCalculo TipoCalculo { get; }
    public IReadOnlyCollection<Faixa> Faixas => _faixas.AsReadOnly();
    public bool Ativo { get; private set; }
    public int Ordem { get; }
    public bool Cumulativo { get; }

    public static Result<Tributo> Create(
        string nome,
        BaseCalculo baseCalculo,
        TipoCalculo tipoCalculo,
        IReadOnlyCollection<Faixa> faixas,
        int ordem,
        bool cumulativo)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            return TributoErrors.InvalidNome;
        }

        if (faixas.Count == 0)
        {
            return TributoErrors.NoFaixas;
        }

        if (ordem < 0)
        {
            return TributoErrors.InvalidOrdem;
        }

        return new Tributo(Guid.NewGuid(), nome, baseCalculo, tipoCalculo, faixas.ToList(), ordem, cumulativo);
    }

    public void Ativar() => Ativo = true;

    public void Desativar() => Ativo = false;

    public Result AtualizarFaixas(IReadOnlyCollection<Faixa> novasFaixas)
    {
        if (novasFaixas.Count == 0)
        {
            return Result.Failure(TributoErrors.NoFaixas);
        }

        _faixas.Clear();
        _faixas.AddRange(novasFaixas);

        return Result.Success();
    }
}
