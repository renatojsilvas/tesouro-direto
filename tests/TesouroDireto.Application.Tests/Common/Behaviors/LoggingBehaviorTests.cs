using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using TesouroDireto.Application.Common.Behaviors;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tests.Common.Behaviors;

public sealed class LoggingBehaviorTests
{
    private sealed record TestRequest : IRequest<Result>;

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private readonly RecordingLogger<LoggingBehavior<TestRequest, Result>> _logger = new();
    private readonly LoggingBehavior<TestRequest, Result> _behavior;

    public LoggingBehaviorTests()
    {
        _behavior = new LoggingBehavior<TestRequest, Result>(_logger);
    }

    [Fact]
    public async Task Handle_WithSuccessResponse_ShouldLogInformationAndNotWarning()
    {
        var request = new TestRequest();
        Task<Result> Next(CancellationToken _) => Task.FromResult(Result.Success());

        await _behavior.Handle(request, Next, CancellationToken.None);

        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Information);
        _logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Handle_WithFailureResponse_ShouldLogWarningWithErrorCode()
    {
        var request = new TestRequest();
        var error = new Error("Test.Error", "Something failed");
        Task<Result> Next(CancellationToken _) => Task.FromResult(Result.Failure(error));

        await _behavior.Handle(request, Next, CancellationToken.None);

        _logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains(error.Code));
    }
}
