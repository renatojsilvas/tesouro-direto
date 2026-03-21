using FluentAssertions;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessResult()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Failure_ShouldCreateFailureResult()
    {
        var error = new Error("Test.Error", "Something failed");

        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Success_AccessingError_ShouldThrowInvalidOperation()
    {
        var result = Result.Success();

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_WithNoneError_ShouldThrowArgumentException()
    {
        var act = () => Result.Failure(Error.None);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failure_WithNull_ShouldThrowArgumentNullException()
    {
        var act = () => Result.Failure(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
