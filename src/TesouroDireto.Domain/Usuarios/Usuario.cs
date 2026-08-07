using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Usuarios;

public sealed class Usuario : Entity<Guid>
{
#pragma warning disable CS8618
    private Usuario() : base(Guid.Empty) { }
#pragma warning restore CS8618

    private Usuario(
        Guid id,
        Email email,
        string nome,
        PapelUsuario papel,
        DateTimeOffset criadoEm,
        string? googleSub)
        : base(id)
    {
        GoogleSub = googleSub;
        Email = email;
        Nome = nome;
        Papel = papel;
        Aprovado = false;
        Ativo = true;
        CriadoEm = criadoEm;
    }

    public string? GoogleSub { get; private set; }
    public Email Email { get; }
    public string Nome { get; }
    public PapelUsuario Papel { get; private set; }
    public bool Aprovado { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; }
    public DateTimeOffset? AprovadoEm { get; private set; }
    public Guid? AprovadoPor { get; private set; }

    public static Result<Usuario> Create(
        Email email,
        string nome,
        PapelUsuario papel,
        DateTimeOffset criadoEm,
        string? googleSub = null)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (string.IsNullOrWhiteSpace(nome))
        {
            return UsuarioErrors.InvalidNome;
        }

        return new Usuario(Guid.NewGuid(), email, nome, papel, criadoEm, googleSub);
    }

    public void Aprovar(Guid adminId, DateTimeOffset quando)
    {
        if (Aprovado)
        {
            return;
        }

        Aprovado = true;
        AprovadoEm = quando;
        AprovadoPor = adminId;
    }

    public void Desativar()
    {
        Ativo = false;
    }

    public bool GarantirAdminAprovado(DateTimeOffset quando)
    {
        var alterou = false;

        if (Papel != PapelUsuario.Admin)
        {
            Papel = PapelUsuario.Admin;
            alterou = true;
        }

        if (!Aprovado)
        {
            Aprovado = true;
            AprovadoEm = quando;
            alterou = true;
        }

        return alterou;
    }

    public Result DefinirGoogleSub(string sub)
    {
        ArgumentNullException.ThrowIfNull(sub);

        if (!string.IsNullOrEmpty(GoogleSub))
        {
            return Result.Failure(UsuarioErrors.GoogleSubJaVinculado);
        }

        GoogleSub = sub;
        return Result.Success();
    }
}
