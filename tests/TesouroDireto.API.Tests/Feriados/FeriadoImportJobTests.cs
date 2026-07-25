using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using TesouroDireto.Application.Feriados;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.Feriados;

namespace TesouroDireto.API.Tests.Feriados;

public sealed class FeriadoImportJobTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly FeriadoImportJob _job;

    public FeriadoImportJobTests()
    {
        _job = new FeriadoImportJob(_sender, Substitute.For<ILogger<FeriadoImportJob>>());
    }

    [Fact]
    public async Task Execute_ShouldSendImportFeriadosCommand()
    {
        _sender
            .Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportFeriadosResult>.Success(new ImportFeriadosResult(0, 0)));

        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        await _job.Execute(context);

        await _sender.Received(1).Send(
            Arg.Any<ImportFeriadosCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenImportFails_ShouldNotThrow()
    {
        _sender
            .Send(Arg.Any<ImportFeriadosCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportFeriadosResult>.Failure(new Error("Feriados.Error", "Something failed")));

        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        var act = () => _job.Execute(context);

        await act.Should().NotThrowAsync();
    }
}
