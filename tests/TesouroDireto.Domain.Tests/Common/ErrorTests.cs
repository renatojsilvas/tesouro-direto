using FluentAssertions;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Tests.Common;

public sealed class ErrorTests
{
    [Fact]
    public void Error_ShouldHaveCodeAndDescription()
    {
        var error = new Error("Test.Error", "Something went wrong");

        error.Code.Should().Be("Test.Error");
        error.Description.Should().Be("Something went wrong");
    }

    [Fact]
    public void Errors_WithSameCodeAndDescription_ShouldBeEqual()
    {
        var error1 = new Error("Test.Error", "desc");
        var error2 = new Error("Test.Error", "desc");

        error1.Should().Be(error2);
    }

    [Fact]
    public void Errors_WithDifferentCode_ShouldNotBeEqual()
    {
        var error1 = new Error("Test.Error1", "desc");
        var error2 = new Error("Test.Error2", "desc");

        error1.Should().NotBe(error2);
    }

    [Fact]
    public void None_ShouldHaveEmptyCodeAndDescription()
    {
        var none = Error.None;

        none.Code.Should().BeEmpty();
        none.Description.Should().BeEmpty();
    }
}
