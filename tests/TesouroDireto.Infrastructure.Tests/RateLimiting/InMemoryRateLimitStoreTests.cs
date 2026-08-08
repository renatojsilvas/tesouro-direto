using System.Threading.RateLimiting;
using FluentAssertions;
using TesouroDireto.Infrastructure.RateLimiting;
using Xunit;

namespace TesouroDireto.Infrastructure.Tests.RateLimiting;

public sealed class InMemoryRateLimitStoreTests
{
    private static PartitionedRateLimiter<string?> CreateLimiter(int permitLimit)
    {
        var store = new InMemoryRateLimitStore(new RateLimitingOptions(permitLimit, TimeSpan.FromMinutes(1)));
        return PartitionedRateLimiter.Create<string?, string>(key => store.GetPartition(key));
    }

    [Fact]
    public void AttemptAcquire_WithNullClienteId_ShouldNeverBlock()
    {
        var limiter = CreateLimiter(3);

        for (var i = 0; i < 10; i++)
        {
            using var lease = limiter.AttemptAcquire(null);
            lease.IsAcquired.Should().BeTrue();
        }
    }

    [Fact]
    public void AttemptAcquire_WithClienteId_ShouldAllowUpToPermitLimitThenBlock()
    {
        var limiter = CreateLimiter(3);

        using var lease1 = limiter.AttemptAcquire("clienteA");
        using var lease2 = limiter.AttemptAcquire("clienteA");
        using var lease3 = limiter.AttemptAcquire("clienteA");
        using var lease4 = limiter.AttemptAcquire("clienteA");

        lease1.IsAcquired.Should().BeTrue();
        lease2.IsAcquired.Should().BeTrue();
        lease3.IsAcquired.Should().BeTrue();
        lease4.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void AttemptAcquire_WithDifferentClienteIds_ShouldHaveIndependentCounters()
    {
        var limiter = CreateLimiter(3);

        using var a1 = limiter.AttemptAcquire("clienteA");
        using var a2 = limiter.AttemptAcquire("clienteA");
        using var a3 = limiter.AttemptAcquire("clienteA");
        using var a4 = limiter.AttemptAcquire("clienteA");

        a4.IsAcquired.Should().BeFalse();

        using var b1 = limiter.AttemptAcquire("clienteB");

        b1.IsAcquired.Should().BeTrue();
    }
}
