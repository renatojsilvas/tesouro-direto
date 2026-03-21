using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.PrecosTaxas;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.PrecosTaxas;

public sealed class GetPrecoAtualQueryHandlerTests
{
    private readonly IPrecoTaxaReadRepository _repository = Substitute.For<IPrecoTaxaReadRepository>();
    private readonly GetPrecoAtualQueryHandler _handler;

    public GetPrecoAtualQueryHandlerTests()
    {
        _handler = new GetPrecoAtualQueryHandler(_repository);
    }

    [Fact]
    public async Task Handle_WithValidTitulo_ShouldReturnLatestPreco()
    {
        var tituloId = Guid.NewGuid();
        var dto = new PrecoTaxaDto(Guid.NewGuid(), "2024-12-20", 11.0m, 11.25m, 850m, 849m, 848m);

        _repository.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        _repository.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Success(dto));

        var result = await _handler.Handle(new GetPrecoAtualQuery(tituloId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DataBase.Should().Be("2024-12-20");
    }

    [Fact]
    public async Task Handle_WithNoPrecos_ShouldReturnNotFound()
    {
        var tituloId = Guid.NewGuid();

        _repository.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));

        _repository.GetLatestByTituloIdAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<PrecoTaxaDto>.Failure(new Error("PrecoTaxa.NotFound", "PrecoTaxa was not found.")));

        var result = await _handler.Handle(new GetPrecoAtualQuery(tituloId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PrecoTaxa.NotFound");
    }

    [Fact]
    public async Task Handle_WithNonExistentTitulo_ShouldReturnNotFound()
    {
        var tituloId = Guid.NewGuid();

        _repository.TituloExistsAsync(tituloId, Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(false));

        var result = await _handler.Handle(new GetPrecoAtualQuery(tituloId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }

    [Fact]
    public async Task Handle_WithEmptyGuid_ShouldReturnNotFound()
    {
        var result = await _handler.Handle(new GetPrecoAtualQuery(Guid.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Titulo.NotFound");
    }
}
