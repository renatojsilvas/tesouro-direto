using FluentAssertions;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Tests.Common;

public sealed class ResultGenericTests
{
    [Fact]
    public void Success_ShouldCreateSuccessResultWithValue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_ShouldCreateFailureResult()
    {
        var error = new Error("Test.Error", "Something failed");

        var result = Result<int>.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Failure_AccessingValue_ShouldThrowInvalidOperation()
    {
        var error = new Error("Test.Error", "fail");
        var result = Result<int>.Failure(error);

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Success_AccessingError_ShouldThrowInvalidOperation()
    {
        var result = Result<int>.Success(42);

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldCreateSuccess()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ImplicitConversion_FromError_ShouldCreateFailure()
    {
        var error = new Error("Test.Error", "fail");

        Result<int> result = error;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Failure_WithNoneError_ShouldThrowArgumentException()
    {
        var act = () => Result<int>.Failure(Error.None);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failure_WithNull_ShouldThrowArgumentNullException()
    {
        var act = () => Result<int>.Failure(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Success_WithNullValue_ShouldThrowArgumentNullException()
    {
        var act = () => Result<string>.Success(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
