using FluentAssertions;
using TesouroDireto.Infrastructure.RateLimiting;
using Xunit;

namespace TesouroDireto.Infrastructure.Tests.RateLimiting;

public sealed class InMemoryAuthFailureRateLimiterTests
{
    [Fact]
    public void RegisterFailure_UpToPermitLimit_ShouldReturnTrue_ThenFalse()
    {
        var limiter = new InMemoryAuthFailureRateLimiter(new AuthFailureRateLimitingOptions(3, TimeSpan.FromMinutes(1)));

        limiter.RegisterFailure("1.2.3.4").Should().BeTrue();
        limiter.RegisterFailure("1.2.3.4").Should().BeTrue();
        limiter.RegisterFailure("1.2.3.4").Should().BeTrue();
        limiter.RegisterFailure("1.2.3.4").Should().BeFalse();
    }

    [Fact]
    public void RegisterFailure_WithDifferentIps_ShouldHaveIndependentCounters()
    {
        var limiter = new InMemoryAuthFailureRateLimiter(new AuthFailureRateLimitingOptions(1, TimeSpan.FromMinutes(1)));

        limiter.RegisterFailure("1.2.3.7").Should().BeTrue();
        limiter.RegisterFailure("1.2.3.7").Should().BeFalse();

        limiter.RegisterFailure("1.2.3.8").Should().BeTrue();
    }

    [Fact]
    public void Window_ShouldReflectConfiguredOption()
    {
        var limiter = new InMemoryAuthFailureRateLimiter(new AuthFailureRateLimitingOptions(1, TimeSpan.FromSeconds(45)));

        limiter.Window.Should().Be(TimeSpan.FromSeconds(45));
    }
}
