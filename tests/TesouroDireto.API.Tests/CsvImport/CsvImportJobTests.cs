using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using TesouroDireto.Application.Importacao;
using TesouroDireto.Domain.Common;
using TesouroDireto.Infrastructure.CsvImport;

namespace TesouroDireto.API.Tests.CsvImport;

public sealed class CsvImportJobTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly CsvImportJob _job;

    public CsvImportJobTests()
    {
        _job = new CsvImportJob(_sender, Substitute.For<ILogger<CsvImportJob>>());
    }

    [Fact]
    public async Task Execute_ShouldSendImportCsvCommand()
    {
        _sender
            .Send(Arg.Any<ImportCsvCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportResult>.Success(new ImportResult(0, 0, 0, 0)));

        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        await _job.Execute(context);

        await _sender.Received(1).Send(
            Arg.Any<ImportCsvCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenImportFails_ShouldNotThrow()
    {
        _sender
            .Send(Arg.Any<ImportCsvCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ImportResult>.Failure(new Error("CsvImport.Error", "Something failed")));

        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);

        var act = () => _job.Execute(context);

        await act.Should().NotThrowAsync();
    }
}
