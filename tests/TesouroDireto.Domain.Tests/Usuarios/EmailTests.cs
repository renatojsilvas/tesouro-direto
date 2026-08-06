using FluentAssertions;
using TesouroDireto.Domain.Usuarios;

namespace TesouroDireto.Domain.Tests.Usuarios;

public sealed class EmailTests
{
    [Theory]
    [InlineData("USUARIO@Exemplo.com", "usuario@exemplo.com")]
    [InlineData("  outro@exemplo.com  ", "outro@exemplo.com")]
    public void Create_WithValidEmail_ShouldTrimAndLowercase(string input, string expected)
    {
        var result = Email.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sememarroba.com")]
    [InlineData("dois@arrobas@exemplo.com")]
    [InlineData("@exemplo.com")]
    [InlineData("usuario@")]
    public void Create_WithInvalidEmail_ShouldReturnFailure(string input)
    {
        var result = Email.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Email.Invalid");
    }

    [Fact]
    public void Create_WithNull_ShouldReturnFailure()
    {
        var result = Email.Create(null!);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Email.Invalid");
    }

    [Fact]
    public void TwoEmails_WithSameValue_ShouldBeEqual()
    {
        var email1 = Email.Create("mesmo@exemplo.com").Value;
        var email2 = Email.Create("MESMO@Exemplo.com").Value;

        email1.Should().Be(email2);
    }
}
