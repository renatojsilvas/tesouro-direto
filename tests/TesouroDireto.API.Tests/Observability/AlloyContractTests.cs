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
    // `TodoJobUsadoNasRegras_NasceNaPosicaoQueODefineNoConfigAlloy` na 77.3, junto com os
    // fatos que verificam — teste que nasce vermelho e atravessa PR viola o Global Constraint
    // e a regra never_skip_failing_tests.

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
    /// job="nginx" e job="kernel" (77.2) nascem no `path_targets` de cada
    /// `local.file_match` (ex.: `path_targets = [{__path__ = "...", job = "nginx"}]`) — a
    /// mesma posição sintática do teste acima. O literal "nginx"/"kernel" também aparece
    /// nos rótulos dos blocos `local.file_match "nginx"`, `loki.source.file "nginx"` e
    /// `loki.process "nginx"` (idem para "kernel") — esses são só nomes de componente
    /// Alloy e não determinam o label da série.
    ///
    /// Achado da revisão adversarial da 77.2: o `selector` LogQL do `stage.match` dentro
    /// de `loki.process "kernel"` (`{job="kernel"} !~ "..."`) TAMBÉM contém o literal
    /// `job="kernel"` — mas ele só FILTRA a série por esse label, não a DEFINE. Um regex
    /// livre `job\s*=\s*"kernel"` solto no arquivo casa esse `selector` sozinho e fica
    /// verde mesmo com o `path_targets` quebrado — provado por mutação: trocar
    /// `job = "kernel"` para `"kernel-BROKEN"` só dentro do `path_targets` deixava os 10
    /// testes verdes antes desta correção (vácuo). Por isso o padrão abaixo ancora
    /// especificamente dentro do literal de lista `path_targets = [{...}]`, a única
    /// posição sintática que de fato define o label — vale tanto para nginx quanto para
    /// kernel, ainda que hoje só kernel tenha um `stage.match` que repete o literal.
    /// </summary>
    [Theory]
    [InlineData("nginx")]
    [InlineData("kernel")]
    public void ConfigAlloy_DeclaraJobDoPathTargetDeLog(string job)
    {
        var config = ConfigSemComentarios();
        var pattern = new Regex(
            "path_targets\\s*=\\s*\\[\\{[^}]*\\bjob\\s*=\\s*\"" + job + "\"[^}]*\\}\\]");
        Assert.Matches(pattern, config);
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

    /// <summary>
    /// Passo 9 da 77.3 — trava os jobs que as 18 regras de `infra/grafana/cloud/rules.yaml`
    /// consomem contra as posições que os DEFINEM em config.alloy. O teste do PLANO, copiado
    /// literalmente, nasceria vácuo pela terceira vez: fazia `config.Contains($"\"{job}\"")`,
    /// o mesmo `Contains` solto que as revisões adversariais da 77.1 e da 77.2 já condenaram
    /// (comentários acima). Concretamente, o literal `"tesouro-direto-api"` aparece duas vezes
    /// em COMENTÁRIO no config.alloy — no cabeçalho que anuncia o job do
    /// `prometheus.scrape "app"` e no bloco que explica de onde `loki.source.api` recebe o
    /// stream já rotulado — além da ocorrência de verdade, dentro do `targets` desse mesmo
    /// `prometheus.scrape "app"`; quebrar só essa última deixaria o `Contains` verde mesmo
    /// assim, sustentado pelos comentários. Por isso: (1) os jobs vêm de rules.yaml por regex,
    /// nunca hardcoded aqui; (2) cada job é checado contra a posição sintática que o DEFINE em
    /// config.alloy, reaproveitando `ConfigSemComentarios()` e os mesmos padrões âncora usados
    /// acima — nunca o literal solto; (3) o mapa job→padrão é FECHADO: um job novo em
    /// rules.yaml sem entrada aqui falha com mensagem explícita, em vez de passar batido — o
    /// mesmo buraco de sempre.
    ///
    /// Fora de escopo deliberado: job="node" não é citado por NENHUMA regra de rules.yaml —
    /// `td-disco-cheio` e as 7 regras `td-container-*` selecionam por NOME DE MÉTRICA
    /// (`node_filesystem_*`, `td_container_*`), sem filtro de `job`; `host.json` idem. Quem
    /// cobre job="node" é `ConfigAlloy_ForcaJobNodeViaRelabel`, não este teste — este teste é
    /// derivado de rules.yaml e não pode inventar cobertura que os dados não sustentam.
    /// </summary>
    [Fact]
    public void TodoJobUsadoNasRegras_NasceNaPosicaoQueODefineNoConfigAlloy()
    {
        var rulesYaml = File.ReadAllText(Path.Combine(RepoRoot(), "infra/grafana/cloud/rules.yaml"));

        // `=~?` casa tanto o matcher exato do PromQL/LogQL (`job="x"`) quanto o de regex
        // (`job=~"x"`). Sem o `?`, uma regra futura escrita como `job=~"nginx|kernel"` não
        // casaria o extrator, o job sumiria da lista abaixo e o teste passaria sem checar
        // nada sobre ela — vácuo silencioso. Com o `?`, essa mesma regra extrai o valor
        // literal `nginx|kernel` (a alternação não é interpretada — deliberado: este teste
        // não faz parsing de regex PromQL), que não existe no mapa fechado abaixo, e o teste
        // FALHA alto pedindo uma decisão humana. Falhar é o comportamento certo aqui: hoje
        // nenhuma regra usa `=~` (confirmado por grep em rules.yaml), então isto é uma trava
        // para o dia em que alguém escrever uma, não uma tentativa de decifrar a alternação.
        var jobsNasRegras = Regex.Matches(rulesYaml, "job\\s*=~?\\s*\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        // Sem isto, um regex que não casa nada deixaria o foreach abaixo sem iterações — o
        // teste passaria sem checar coisa nenhuma (o mesmo vácuo de sempre, só que disfarçado
        // de fonte de dados vazia em vez de asserção solta).
        Assert.NotEmpty(jobsNasRegras);

        var padraoAncoraPorJob = new Dictionary<string, Regex>
        {
            // Mesma posição sintática de ConfigAlloy_DeclaraJobDoScrapeDireto.
            ["tesouro-direto-api"] = new Regex("job\\s*=\\s*\"tesouro-direto-api\""),
            // Mesma posição sintática (path_targets) de ConfigAlloy_DeclaraJobDoPathTargetDeLog.
            ["nginx"] = new Regex("path_targets\\s*=\\s*\\[\\{[^}]*\\bjob\\s*=\\s*\"nginx\"[^}]*\\}\\]"),
            ["kernel"] = new Regex("path_targets\\s*=\\s*\\[\\{[^}]*\\bjob\\s*=\\s*\"kernel\"[^}]*\\}\\]"),
        };

        var config = ConfigSemComentarios();
        foreach (var job in jobsNasRegras)
        {
            Assert.True(
                padraoAncoraPorJob.TryGetValue(job, out var padrao),
                $"job=\"{job}\" aparece em rules.yaml mas não tem posição definidora conhecida " +
                "em config.alloy — decida onde esse job nasce e acrescente ao mapa acima.");

            Assert.Matches(padrao!, config);
        }
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
