using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TesouroDireto.Application.Projecoes;
using TesouroDireto.Infrastructure.Projecoes;

namespace TesouroDireto.API.Tests.Projecoes;

// Rede de segurança da tarefa 23: trava por teste a composição de DI da
// projeção de mercado (Infrastructure/DependencyInjection.cs, ~linhas 103-122).
// Uma regressão que voltasse a registrar
// AddHttpClient<IProjecaoMercadoService, FocusBcbService>(...) faria os testes
// (e o app) baterem direto na API real do BCB, pulando o decorator de cache.
public sealed class DependencyInjectionCompositionTests
    : IClassFixture<DependencyInjectionCompositionTests.CompositionWebFactory>
{
    private readonly CompositionWebFactory _factory;

    public DependencyInjectionCompositionTests(CompositionWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void IProjecaoMercadoService_ResolvesTo_CachedDecorator()
    {
        using var scope = _factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IProjecaoMercadoService>();

        service.Should().BeOfType<CachedProjecaoMercadoService>();
    }

    [Fact]
    public void FocusBcbService_TypedClient_HasThirtySecondTimeout()
    {
        var httpClientFactory = _factory.Services.GetRequiredService<IHttpClientFactory>();

        var client = httpClientFactory.CreateClient(nameof(FocusBcbService));

        client.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    public sealed class CompositionWebFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Key"] = "test-api-key-12345",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=fake;Username=fake;Password=fake"
                });
            });
        }
    }
}
