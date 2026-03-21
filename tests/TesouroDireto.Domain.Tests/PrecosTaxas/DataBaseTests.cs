using FluentAssertions;
using TesouroDireto.Domain.PrecosTaxas;

namespace TesouroDireto.Domain.Tests.PrecosTaxas;

public sealed class DataBaseTests
{
    [Fact]
    public void Create_WithValidDate_ShouldReturnSuccess()
    {
        var date = new DateOnly(2024, 6, 15);

        var result = DataBase.Create(date);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(date);
    }

    [Fact]
    public void Create_WithMinDate_ShouldReturnFailure()
    {
        var result = DataBase.Create(DateOnly.MinValue);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DataBase.Invalid");
    }

    [Fact]
    public void SameDate_ShouldBeEqual()
    {
        var date = new DateOnly(2024, 6, 15);
        var db1 = DataBase.Create(date).Value;
        var db2 = DataBase.Create(date).Value;

        db1.Should().Be(db2);
    }
}
