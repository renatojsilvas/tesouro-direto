using YamlDotNet.RepresentationModel;

namespace TesouroDireto.API.Tests.Observability;

/// <summary>
/// A 77.4 removeu `grafana`, `loki`, `promtail` e `node-exporter` de docker-compose.yml e
/// pôs `prometheus` sob `profiles: ["load"]` (só sobe durante teste de carga — o k6 faz
/// remote-write local, e mandar as séries `k6_*` para o Grafana Cloud estouraria o teto de
/// séries do plano). A revisão adversarial provou por mutação que NENHUM gate do CI protege
/// isso: os três `docker compose config -q` do workflow validam só sintaxe/schema, e
/// continuam verdes mesmo se `profiles: ["load"]` for apagado — o `prometheus` voltaria a
/// subir em todo `docker compose up -d` de produção, comendo 72 MB em silêncio, e só
/// revisão humana pegaria. Estes testes travam essa invariante.
///
/// ARMADILHA: docker-compose.yml cita `grafana`, `loki`, `promtail`, `node-exporter` e
/// `prometheus` muitas vezes em COMENTÁRIOS de prosa (histórico da migração, ex.: "removido
/// na 77.4", "mesmos binds que o serviço `promtail` (removido na 77.4) tinha"). Um teste que
/// faça `Contains("loki")` no texto bruto do arquivo ficaria VERMELHO por causa desses
/// comentários mesmo com o compose correto — o mesmo vácuo/falso-positivo que
/// AlloyContractTests já documentou para config.alloy. Por isso aqui NÃO se casa literal em
/// texto: o arquivo é parseado como YAML de verdade (YamlDotNet, já usado no projeto para
/// ler o rules.yaml do Grafana Cloud alerting) e a checagem navega
/// `services.&lt;nome&gt;.profiles` na árvore — comentários YAML nunca viram nós da árvore,
/// então não têm como sustentar (nem derrubar) nenhum destes testes.
/// </summary>
public class ComposeContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root não encontrado");
    }

    /// <summary>
    /// Parseia docker-compose.yml e devolve o mapa `services:` como árvore YAML — nunca como
    /// texto. Comentários (`#`) são descartados pelo próprio parser, não por um filtro de
    /// linha como em AlloyContractTests: aqui não há necessidade do truque, porque nenhuma
    /// asserção abaixo olha o texto bruto do arquivo.
    /// </summary>
    private static YamlMappingNode ServicesNode()
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(Path.Combine(RepoRoot(), "docker-compose.yml"));
        yaml.Load(reader);

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        return (YamlMappingNode)root.Children[new YamlScalarNode("services")];
    }

    private static bool TryGetService(YamlMappingNode services, string nome, out YamlMappingNode servico)
    {
        var achou = services.Children.TryGetValue(new YamlScalarNode(nome), out var node);
        servico = achou ? (YamlMappingNode)node! : null!;
        return achou;
    }

    /// <summary>
    /// Se `profiles: ["load"]` sumir do serviço `prometheus` (apagado por engano, ou por um
    /// merge que reverte a 77.4), este teste é o único que percebe: os gates de CI validam
    /// só sintaxe. `prometheus` tem que existir E carregar "load" na lista `profiles`.
    /// </summary>
    [Fact]
    public void Prometheus_ExisteESoSobeSobProfileLoad()
    {
        var services = ServicesNode();

        Assert.True(
            TryGetService(services, "prometheus", out var prometheus),
            "serviço `prometheus` não existe mais em docker-compose.yml — o overlay " +
            "docker-compose.load.yml depende dele para o k6 fazer remote-write local.");

        Assert.True(
            prometheus.Children.TryGetValue(new YamlScalarNode("profiles"), out var profilesNode),
            "serviço `prometheus` perdeu a chave `profiles` — sem ela ele volta a subir em " +
            "todo `docker compose up -d` de produção, comendo 72 MB em silêncio (é exatamente " +
            "o buraco que a revisão adversarial da 77.4 encontrou).");

        var profiles = ((YamlSequenceNode)profilesNode!)
            .Children
            .Select(c => ((YamlScalarNode)c).Value)
            .ToList();

        Assert.Contains("load", profiles);
    }

    /// <summary>
    /// Os quatro serviços que a 77.4 removeu (substituídos pelo `alloy`) não podem voltar por
    /// merge ruim ou por alguém rodar `setup.sh` (que ainda os gera, achado da 77.4). Checa a
    /// AUSÊNCIA da chave do serviço na árvore — nunca `Contains` no texto, que casaria os
    /// comentários que citam esses nomes no histórico da migração.
    /// </summary>
    [Theory]
    [InlineData("grafana")]
    [InlineData("loki")]
    [InlineData("promtail")]
    [InlineData("node-exporter")]
    public void ServicoRemovidoNaLimpezaNaoRessuscitou(string servico)
    {
        var services = ServicesNode();

        Assert.False(
            TryGetService(services, servico, out _),
            $"serviço `{servico}` voltou a existir em docker-compose.yml — a 77.4 removeu-o " +
            "porque o `alloy` assumiu seu papel; se isto é intencional, revise esta trava.");
    }

    /// <summary>
    /// `alloy` é o coletor único (métricas de host, textfile collector e os 4 fluxos de log
    /// convergem nele) e tem que subir sempre — se alguém copiar o padrão `profiles: ["load"]`
    /// do `prometheus` para o `alloy` por engano, a observabilidade inteira para em produção
    /// normal (sem `--profile load`), em silêncio.
    /// </summary>
    [Fact]
    public void Alloy_ExisteENaoEstaSobNenhumProfile()
    {
        var services = ServicesNode();

        Assert.True(
            TryGetService(services, "alloy", out var alloy),
            "serviço `alloy` não existe em docker-compose.yml — é o coletor único de " +
            "observabilidade desde a 77.2/77.3, não pode ter sumido.");

        Assert.False(
            alloy.Children.ContainsKey(new YamlScalarNode("profiles")),
            "serviço `alloy` ganhou uma chave `profiles` — ele é o coletor único e tem que " +
            "subir em todo `docker compose up -d`, não só sob um profile específico.");
    }
}
