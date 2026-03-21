using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TesouroDireto.API.Tests.Middleware;

public sealed class CorrelationIdMiddlewareTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string PublicPath = "/health";

    [Fact]
    public async Task Request_WithoutCorrelationId_ShouldGenerateAndReturnInResponse()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(PublicPath, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey(CorrelationIdHeader);

        var correlationId = response.Headers.GetValues(CorrelationIdHeader).Single();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Request_WithCorrelationId_ShouldPreserveAndReturnSameValue()
    {
        var client = factory.CreateClient();
        var expectedId = Guid.NewGuid().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.Add(CorrelationIdHeader, expectedId);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnedId = response.Headers.GetValues(CorrelationIdHeader).Single();
        returnedId.Should().Be(expectedId);
    }

    [Fact]
    public async Task Request_WithEmptyCorrelationId_ShouldGenerateNewOne()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.Add(CorrelationIdHeader, string.Empty);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var correlationId = response.Headers.GetValues(CorrelationIdHeader).Single();
        correlationId.Should().NotBeEmpty();
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("malicious\r\nheader")]
    [InlineData("a]b[c{d}e")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Request_WithInvalidCorrelationId_ShouldGenerateNewOne(string maliciousId)
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, maliciousId);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var correlationId = response.Headers.GetValues(CorrelationIdHeader).Single();
        correlationId.Should().NotBe(maliciousId);
        Guid.TryParse(correlationId, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData("my-trace-id-001")]
    public async Task Request_WithValidAlphanumericCorrelationId_ShouldPreserve(string validId)
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, PublicPath);
        request.Headers.Add(CorrelationIdHeader, validId);

        var response = await client.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnedId = response.Headers.GetValues(CorrelationIdHeader).Single();
        returnedId.Should().Be(validId);
    }
}
