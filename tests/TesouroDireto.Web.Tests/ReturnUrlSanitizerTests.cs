using FluentAssertions;
using TesouroDireto.Web.Services;

namespace TesouroDireto.Web.Tests;

public class ReturnUrlSanitizerTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("//evil", "/")]
    [InlineData("/\\\\evil", "/")]
    [InlineData("https:evil", "/")]
    [InlineData("evil.com", "/")]
    [InlineData("titulos", "/")]
    [InlineData("/titulos", "/titulos")]
    [InlineData("/titulos?page=2", "/titulos?page=2")]
    public void Sanitize_DevolveEsperado(string? returnUrl, string esperado)
    {
        var resultado = ReturnUrlSanitizer.Sanitize(returnUrl);

        resultado.Should().Be(esperado);
    }
}
