using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.ApiKeys;

public sealed class ApiKey : Entity<Guid>
{
#pragma warning disable CS8618
    private ApiKey() : base(Guid.Empty) { }
#pragma warning restore CS8618

    private ApiKey(
        Guid id,
        string nome,
        ApiKeyHash hash,
        string prefixo,
        Guid donoUsuarioId,
        DateTimeOffset criadaEm)
        : base(id)
    {
        Nome = nome;
        Hash = hash;
        Prefixo = prefixo;
        DonoUsuarioId = donoUsuarioId;
        Ativa = true;
        CriadaEm = criadaEm;
    }

    public string Nome { get; }
    public ApiKeyHash Hash { get; }
    public string Prefixo { get; }
    public Guid DonoUsuarioId { get; }
    public bool Ativa { get; private set; }
    public DateTimeOffset CriadaEm { get; }
    public DateTimeOffset? RevogadaEm { get; private set; }
    public DateTimeOffset? UltimoUsoEm { get; private set; }

    public static Result<ApiKey> Create(
        string nome,
        ApiKeyHash hash,
        string prefixo,
        Guid donoUsuarioId,
        DateTimeOffset criadaEm)
    {
        ArgumentNullException.ThrowIfNull(hash);

        if (string.IsNullOrWhiteSpace(nome))
        {
            return ApiKeyErrors.InvalidNome;
        }

        if (string.IsNullOrWhiteSpace(prefixo))
        {
            return ApiKeyErrors.InvalidPrefixo;
        }

        return new ApiKey(Guid.NewGuid(), nome, hash, prefixo, donoUsuarioId, criadaEm);
    }

    public void Revogar(DateTimeOffset quando)
    {
        Ativa = false;
        RevogadaEm = quando;
    }

    public void RegistrarUso(DateTimeOffset quando)
    {
        UltimoUsoEm = quando;
    }
}
