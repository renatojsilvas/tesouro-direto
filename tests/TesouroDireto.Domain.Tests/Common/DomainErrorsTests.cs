using FluentAssertions;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Tests.Common;

public sealed class DomainErrorsTests
{
    [Fact]
    public void NotFound_ShouldReturnErrorWithEntityName()
    {
        var error = DomainErrors.General.NotFound("Titulo");

        error.Code.Should().Be("General.NotFound");
        error.Description.Should().Contain("Titulo");
    }

    [Fact]
    public void Validation_ShouldReturnErrorWithMessage()
    {
        var error = DomainErrors.General.Validation("Field is required");

        error.Code.Should().Be("General.Validation");
        error.Description.Should().Be("Field is required");
    }

    [Fact]
    public void NullOrEmpty_ShouldReturnErrorWithFieldName()
    {
        var error = DomainErrors.General.NullOrEmpty("Name");

        error.Code.Should().Be("General.NullOrEmpty");
        error.Description.Should().Contain("Name");
    }
}
