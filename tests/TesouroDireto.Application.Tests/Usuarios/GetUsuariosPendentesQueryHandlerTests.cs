using FluentAssertions;
using NSubstitute;
using TesouroDireto.Application.Usuarios;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.Usuarios;

public sealed class GetUsuariosPendentesQueryHandlerTests
{
    private readonly IUsuarioReadRepository _readRepo = Substitute.For<IUsuarioReadRepository>();
    private readonly GetUsuariosPendentesQueryHandler _handler;

    public GetUsuariosPendentesQueryHandlerTests()
    {
        _handler = new GetUsuariosPendentesQueryHandler(_readRepo);
    }

    [Fact]
    public async Task Handle_ShouldDelegateToReadRepository()
    {
        IReadOnlyCollection<UsuarioPendenteDto> pendentes = new[]
        {
            new UsuarioPendenteDto(Guid.NewGuid(), "sub-1", "pendente@exemplo.com", "Pendente", DateTimeOffset.UtcNow)
        };

        _readRepo.ListPendentesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyCollection<UsuarioPendenteDto>>.Success(pendentes));

        var result = await _handler.Handle(new GetUsuariosPendentesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(pendentes);
    }
}
