using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TesouroDireto.API.Tests.Observability;

/// <summary>
/// `AlloyContractTests.TextfileCollector_AindaEmiteAsMetricasQueAsRegrasConsomem` só faz
/// `Assert.Contains(nome_da_metrica, script)` — prova que o NOME da métrica sobrevive no
/// arquivo, nada sobre a FÓRMULA que a calcula. A tarefa 80.2 corrigiu
/// `td_container_memory_unreclaimable_bytes` de `anon + shmem + kernel` para
/// `anon + shmem + (kernel - slab_reclaimable)` (`slab_reclaimable` é dentry/inode cache que o
/// kernel encolhe sob pressão ANTES de cogitar OOM — mesma natureza do `active_file` que a
/// tarefa 76 já tinha excluído do `working_set`). Um `Contains` sobre o nome da métrica
/// continuaria verde com a fórmula antiga: é o padrão de teste vácuo que este projeto já foi
/// punido por repetir (`teste_contrato_nasce_vacuo`).
///
/// Regex sobre o texto do shell foi descartado como desenho: `awk`/aritmética bash não têm
/// forma canônica única em texto (`$(( a + b + (c - d) ))` vs. variações espaçadas/reordenadas
/// que calculam o MESMO número) — um regex frágil o bastante para pegar a mutação de verdade
/// (trocar a fórmula) também quebraria com reformatação inofensiva do script, e um regex frouxo
/// o bastante para tolerar reformatação vira vácuo de novo. A prova honesta é EXECUTAR o script
/// contra um `memory.stat` sintético e assertar o número que ele de fato emite.
///
/// `container-metrics.sh` já suporta isso sem nenhuma refatoração: `CGROUP_ROOT` e
/// `TEXTFILE_DIR` são configuráveis por variável de ambiente (linhas 27-28), e o script só
/// precisa de `docker ps`/`docker inspect` no PATH — um `docker` fake de duas linhas (só
/// implementando os dois subcomandos que o script usa) resolve isso sem depender de Docker de
/// verdade rodando na máquina de CI.
/// </summary>
public class ContainerMetricsUnreclaimableFormulaTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root não encontrado");
    }

    private sealed record ScriptRun(int ExitCode, string Stdout, string Stderr, string PromContent);

    /// <summary>
    /// Monta um "host" sintético (cgroup root + `docker` fake no PATH), roda
    /// `infra/host/container-metrics.sh` de verdade contra ele, e devolve o CONTEÚDO do `.prom`
    /// resultante — lido ainda dentro deste método, antes de o diretório temporário ser
    /// apagado no `finally` (armadilha: `finally` roda antes do valor de retorno chegar ao
    /// chamador, então devolver só o CAMINHO do arquivo daria `DirectoryNotFoundException` no
    /// teste — já reproduzido). `memoryStat` vai literal para `memory.stat` do container — cada
    /// teste decide que campos (e quais faltam) quer simular.
    /// </summary>
    private static ScriptRun RunScript(string containerId, string containerName, string memoryStat)
    {
        var work = Directory.CreateTempSubdirectory("td-container-metrics-test-");
        try
        {
            var cgroupScope = Path.Combine(work.FullName, "cgroup", "system.slice", $"docker-{containerId}.scope");
            Directory.CreateDirectory(cgroupScope);
            File.WriteAllText(Path.Combine(cgroupScope, "memory.current"), "200000000\n");
            File.WriteAllText(Path.Combine(cgroupScope, "memory.stat"), memoryStat);
            File.WriteAllText(Path.Combine(cgroupScope, "cpu.stat"), "usage_usec 1000\nnr_periods 0\nnr_throttled 0\nthrottled_usec 0\n");
            File.WriteAllText(Path.Combine(cgroupScope, "memory.events"), "low 0\nhigh 0\nmax 0\noom 0\noom_kill 0\n");

            var textfileDir = Path.Combine(work.FullName, "textfile");
            Directory.CreateDirectory(textfileDir);

            var binDir = Path.Combine(work.FullName, "bin");
            Directory.CreateDirectory(binDir);
            var fakeDockerPath = Path.Combine(binDir, "docker");
            File.WriteAllText(fakeDockerPath, $"""
                #!/bin/bash
                case "$1" in
                  ps)
                    printf '{containerId}\t{containerName}\n'
                    ;;
                  inspect)
                    printf '{containerId}|0\n'
                    ;;
                esac
                """.Replace("\r\n", "\n"));
            Process.Start("chmod", $"+x \"{fakeDockerPath}\"")!.WaitForExit();

            var scriptPath = Path.Combine(RepoRoot(), "infra", "host", "container-metrics.sh");
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { scriptPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment["PATH"] = binDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            psi.Environment["CGROUP_ROOT"] = Path.Combine(work.FullName, "cgroup");
            psi.Environment["TEXTFILE_DIR"] = textfileDir;

            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            var promFilePath = Path.Combine(textfileDir, "container_resources.prom");
            var promContent = File.Exists(promFilePath) ? File.ReadAllText(promFilePath) : string.Empty;

            return new ScriptRun(proc.ExitCode, stdout, stderr, promContent);
        }
        finally
        {
            try { Directory.Delete(work.FullName, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Extrai o VALOR de `td_container_memory_unreclaimable_bytes{container="&lt;nome&gt;"}` do
    /// `.prom` — falha alto (não devolve null/0) se a série não existir, porque "série ausente"
    /// e "série com valor 0" são fatos diferentes e um teste que confundisse os dois esconderia
    /// justamente a mutação "omitir a série inteira".
    /// </summary>
    private static long ExtractUnreclaimableValue(string promContent, string containerName)
    {
        var pattern = new Regex(
            "^td_container_memory_unreclaimable_bytes\\{container=\"" + Regex.Escape(containerName) + "\"\\}\\s+(\\d+)$",
            RegexOptions.Multiline);
        var match = pattern.Match(promContent);
        Assert.True(match.Success,
            $"td_container_memory_unreclaimable_bytes{{container=\"{containerName}\"}} não apareceu no .prom emitido:\n{promContent}");
        return long.Parse(match.Groups[1].Value);
    }

    /// <summary>
    /// anon=100_000_000, shmem=20_000_000, kernel=30_000_000, slab_reclaimable=7_000_000.
    /// Fórmula correta (80.2): 100_000_000 + 20_000_000 + (30_000_000 - 7_000_000) =
    /// 143_000_000. A fórmula antiga (anon+shmem+kernel, pré-80.2) daria 150_000_000 — os dois
    /// valores são checados explicitamente para que reverter a subtração deixe este teste
    /// vermelho por igualdade, não por uma tolerância frouxa que os dois números passariam.
    ///
    /// Prova por mutação (ver relatório da tarefa): reverter a linha
    /// `unreclaimable=$(( mem_anon + mem_shmem + (mem_kernel - mem_slab_reclaimable) ))` para
    /// `unreclaimable=$(( mem_anon + mem_shmem + mem_kernel ))` faz este teste falhar
    /// (143_000_000 esperado vs. 150_000_000 emitido) — vermelho.
    /// </summary>
    [Fact]
    public void Unreclaimable_DescontaSlabReclaimableDoKernel()
    {
        const string memoryStat = """
            anon 100000000
            file 5000000
            kernel 30000000
            slab_reclaimable 7000000
            slab_unreclaimable 2000000
            shmem 20000000
            inactive_file 1500000

            """;

        var run = RunScript("deadbeef0000", "fake-com-slab", memoryStat);

        Assert.Equal(0, run.ExitCode);
        var valor = ExtractUnreclaimableValue(run.PromContent, "fake-com-slab");

        const long formulaCorrigida = 100_000_000L + 20_000_000L + (30_000_000L - 7_000_000L);
        const long formulaAntiga = 100_000_000L + 20_000_000L + 30_000_000L;

        Assert.Equal(143_000_000L, formulaCorrigida);
        Assert.Equal(150_000_000L, formulaAntiga);
        Assert.Equal(formulaCorrigida, valor);
        Assert.NotEqual(formulaAntiga, valor);
    }

    /// <summary>
    /// cgroup v1 / kernel antigo: `slab_reclaimable` não existe em `memory.stat`. A ARMADILHA
    /// que a tarefa 80.2 precisou tratar: o padrão de `read_kv` neste script é omitir a série
    /// inteira quando um campo falta (`&amp;&amp;` encadeado) — omitir aqui seria PIOR que emitir a
    /// fórmula antiga, porque as 5 regras `td-container-*` e o dead-man
    /// (`td-metricas-container-obsoletas`, `noDataState: Alerting`) ficariam sem dado nenhum.
    /// Por isso `slab_reclaimable` ausente tem que cair para 0 (equivalente à fórmula antiga,
    /// `anon+shmem+kernel`) e a série tem que CONTINUAR sendo emitida.
    /// </summary>
    [Fact]
    public void Unreclaimable_SlabReclaimableAusente_CaiParaFormulaAntigaEContinuaEmitindo()
    {
        const string memoryStat = """
            anon 100000000
            file 5000000
            kernel 30000000
            shmem 20000000
            inactive_file 1500000

            """;

        var run = RunScript("cafebabe0000", "fake-sem-slab", memoryStat);

        Assert.Equal(0, run.ExitCode);
        var valor = ExtractUnreclaimableValue(run.PromContent, "fake-sem-slab");

        Assert.Equal(100_000_000L + 20_000_000L + 30_000_000L, valor);
    }
}
