using FluentAssertions;
using TesouroDireto.API.Extensions;

namespace TesouroDireto.API.Tests.Extensions;

public sealed class ApiKeyGuardTests
{
    [Fact]
    public void Validate_Production_WithDefaultKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "CHANGE-ME-IN-PRODUCTION");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithEmptyKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithNullKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithWhitespaceKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "   ");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithDefaultKeyTrailingWhitespace_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "CHANGE-ME-IN-PRODUCTION ");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithDefaultKeyLeadingWhitespace_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", " CHANGE-ME-IN-PRODUCTION");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithDefaultKeyLowercase_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "change-me-in-production");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithStrongKey_ShouldNotThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "uma-chave-real-forte");

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Production_WithDevLocalKey_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "dev-local-key");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Production_WithDevLocalKeyWhitespaceAndDifferentCase_ShouldThrow()
    {
        var act = () => ApiKeyGuard.Validate("Production", "  DEV-LOCAL-KEY  ");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_Development_WithDevLocalKey_ShouldNotThrow()
    {
        var act = () => ApiKeyGuard.Validate("Development", "dev-local-key");

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Development_WithDefaultKey_ShouldNotThrow()
    {
        var act = () => ApiKeyGuard.Validate("Development", "CHANGE-ME-IN-PRODUCTION");

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_Testing_WithDefaultKey_ShouldNotThrow()
    {
        var act = () => ApiKeyGuard.Validate("Testing", "CHANGE-ME-IN-PRODUCTION");

        act.Should().NotThrow();
    }
}
