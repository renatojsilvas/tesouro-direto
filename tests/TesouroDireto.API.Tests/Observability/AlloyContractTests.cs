using System.Text.RegularExpressions;

namespace TesouroDireto.API.Tests.Observability;

/// <summary>
/// As 18 regras de alerta filtram por nomes de `job` e de métrica que vivem fora do C#
/// (config.alloy, container-metrics.sh). Nada na suíte protegia esses nomes — a tarefa 77
/// descobriu que renomear um `job` deixa alertas mudos em silêncio. Estes testes falham
/// se alguém mexer num nome sem mexer nas regras.
/// </summary>
public class AlloyContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root não encontrado");
    }

    /// <summary>
    /// O arquivo tem o literal `"node"` em 4 lugares (comentário, rótulo do bloco
    /// `discovery.relabel`, rótulo do `prometheus.scrape` e o `replacement` que de fato
    /// define o label) — um `Contains` solto casaria com qualquer um deles e ficaria verde
    /// mesmo com o `replacement` errado. Comentários são removidos ANTES do match para que
    /// nenhum deles sozinho sustente um teste.
    /// </summary>
    private static string ConfigSemComentarios()
    {
        var config = File.ReadAllText(Path.Combine(RepoRoot(), "infra/alloy/config.alloy"));
        var linhasUteis = config
            .Split('\n')
            .Where(linha => !linha.TrimStart().StartsWith("//", StringComparison.Ordinal));
        return string.Join('\n', linhasUteis);
    }

    // Cobre SÓ os jobs que existem ao fim desta fase. `nginx` e `kernel` entram na 77.2 e
    // `TodoJobUsadoNasRegras` na 77.3, junto com os fatos que verificam — teste que nasce
    // vermelho e atravessa PR viola o Global Constraint e a regra never_skip_failing_tests.

    /// <summary>
    /// job="tesouro-direto-api" nasce no target do `prometheus.scrape "app"`
    /// (`targets = [{__address__ = "app:8080", job = "tesouro-direto-api"}]`). Casa a forma
    /// `job = "&lt;nome&gt;"` — a posição sintática que de fato define o job da série — em vez
    /// do literal solto, que também bateria em comentário ou rótulo de bloco.
    /// </summary>
    [Fact]
    public void ConfigAlloy_DeclaraJobDoScrapeDireto()
    {
        var config = ConfigSemComentarios();
        Assert.Matches(new Regex("job\\s*=\\s*\"tesouro-direto-api\""), config);
    }

    /// <summary>
    /// job="node" não nasce de nenhum literal `job = "..."` — o `prometheus.exporter.unix`
    /// rotula os alvos com um job próprio e é o `replacement = "node"` dentro do
    /// `discovery.relabel` que força o nome do contrato. O rótulo do bloco
    /// (`discovery.relabel "node"`, `prometheus.scrape "node"`) é só o nome do componente
    /// Alloy, não determina o label da série — este teste deliberadamente NÃO reage a ele.
    /// </summary>
    [Fact]
    public void ConfigAlloy_ForcaJobNodeViaRelabel()
    {
        var config = ConfigSemComentarios();
        Assert.Matches(new Regex("replacement\\s*=\\s*\"node\""), config);
    }

    [Theory]
    [InlineData("td_container_memory_unreclaimable_bytes")]
    [InlineData("td_container_memory_limit_bytes")]
    [InlineData("td_container_oom_kill_total")]
    [InlineData("td_container_restarts_total")]
    [InlineData("td_container_memory_reclaim_events_total")]
    [InlineData("td_container_cpu_cfs_throttled_periods_total")]
    public void TextfileCollector_AindaEmiteAsMetricasQueAsRegrasConsomem(string metrica)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "infra/host/container-metrics.sh"));
        Assert.Contains(metrica, script);
    }
}
