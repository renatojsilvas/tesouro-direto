using FluentAssertions;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Domain.Tests.Usuarios;

public sealed class UsuarioTests
{
    private static readonly Email ValidEmail = Email.Create("usuario@exemplo.com").Value;
    private static readonly DateTimeOffset CriadoEm = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidData_ShouldReturnSuccessAndNotAprovado()
    {
        var result = Usuario.Create(ValidEmail, "Fulano de Tal", PapelUsuario.User, CriadoEm);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be(ValidEmail);
        result.Value.Nome.Should().Be("Fulano de Tal");
        result.Value.Papel.Should().Be(PapelUsuario.User);
        result.Value.CriadoEm.Should().Be(CriadoEm);
        result.Value.Aprovado.Should().BeFalse();
        result.Value.AprovadoEm.Should().BeNull();
        result.Value.AprovadoPor.Should().BeNull();
        result.Value.GoogleSub.Should().BeNull();
        result.Value.Id.Should().NotBe(Guid.Empty);
        result.Value.Ativo.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyNome_ShouldReturnFailure(string nome)
    {
        var result = Usuario.Create(ValidEmail, nome, PapelUsuario.User, CriadoEm);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.InvalidNome");
    }

    [Fact]
    public void Create_WithNullEmail_ShouldThrow()
    {
        var act = () => Usuario.Create(null!, "Fulano", PapelUsuario.User, CriadoEm);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithGoogleSub_ShouldSetGoogleSub()
    {
        var result = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.Admin, CriadoEm, "google-sub-123");

        result.IsSuccess.Should().BeTrue();
        result.Value.GoogleSub.Should().Be("google-sub-123");
        result.Value.Papel.Should().Be(PapelUsuario.Admin);
    }

    [Fact]
    public void Aprovar_ShouldSetAprovadoAndAuditFields()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.User, CriadoEm).Value;
        var adminId = Guid.NewGuid();
        var quando = CriadoEm.AddDays(1);

        usuario.Aprovar(adminId, quando);

        usuario.Aprovado.Should().BeTrue();
        usuario.AprovadoEm.Should().Be(quando);
        usuario.AprovadoPor.Should().Be(adminId);
    }

    [Fact]
    public void Aprovar_WhenAlreadyAprovado_ShouldBeNoOpAndKeepOriginalAuditFields()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.User, CriadoEm).Value;
        var primeiroAdminId = Guid.NewGuid();
        var primeiraAprovacao = CriadoEm.AddDays(1);
        usuario.Aprovar(primeiroAdminId, primeiraAprovacao);

        usuario.Aprovar(Guid.NewGuid(), CriadoEm.AddDays(5));

        usuario.Aprovado.Should().BeTrue();
        usuario.AprovadoEm.Should().Be(primeiraAprovacao);
        usuario.AprovadoPor.Should().Be(primeiroAdminId);
    }

    [Fact]
    public void Desativar_ShouldSetAtivoFalse()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.User, CriadoEm).Value;

        usuario.Desativar();

        usuario.Ativo.Should().BeFalse();
    }

    [Fact]
    public void DefinirGoogleSub_WhenNotSet_ShouldSucceedAndSetValue()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.User, CriadoEm).Value;

        var result = usuario.DefinirGoogleSub("novo-sub");

        result.IsSuccess.Should().BeTrue();
        usuario.GoogleSub.Should().Be("novo-sub");
    }

    [Fact]
    public void DefinirGoogleSub_WhenAlreadySet_ShouldReturnFailure()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.User, CriadoEm, "sub-original").Value;

        var result = usuario.DefinirGoogleSub("outro-sub");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Usuario.GoogleSubJaVinculado");
        usuario.GoogleSub.Should().Be("sub-original");
    }

    [Fact]
    public void GarantirAdminAprovado_WhenUserAndNotAprovado_ShouldReturnTrueAndPromote()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.User, CriadoEm).Value;
        var quando = CriadoEm.AddDays(1);

        var alterou = usuario.GarantirAdminAprovado(quando);

        alterou.Should().BeTrue();
        usuario.Papel.Should().Be(PapelUsuario.Admin);
        usuario.Aprovado.Should().BeTrue();
        usuario.AprovadoEm.Should().Be(quando);
    }

    [Fact]
    public void GarantirAdminAprovado_WhenUserAndNotAprovado_ShouldNotSetAprovadoPor()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.User, CriadoEm).Value;
        var quando = CriadoEm.AddDays(1);

        usuario.GarantirAdminAprovado(quando);

        usuario.AprovadoPor.Should().BeNull();
    }

    [Fact]
    public void GarantirAdminAprovado_WhenAlreadyAdminAndAprovado_ShouldReturnFalseAndKeepAprovadoEm()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.Admin, CriadoEm).Value;
        var quando1 = CriadoEm.AddDays(1);
        var quando2 = CriadoEm.AddDays(2);
        usuario.GarantirAdminAprovado(quando1);

        var alterou = usuario.GarantirAdminAprovado(quando2);

        alterou.Should().BeFalse();
        usuario.AprovadoEm.Should().Be(quando1);
    }

    [Fact]
    public void GarantirAdminAprovado_WhenAdminButNotAprovado_ShouldReturnTrueAndApprove()
    {
        var usuario = Usuario.Create(ValidEmail, "Fulano", PapelUsuario.Admin, CriadoEm).Value;
        var quando = CriadoEm.AddDays(1);

        var alterou = usuario.GarantirAdminAprovado(quando);

        alterou.Should().BeTrue();
        usuario.Papel.Should().Be(PapelUsuario.Admin);
        usuario.Aprovado.Should().BeTrue();
        usuario.AprovadoEm.Should().Be(quando);
    }
}
