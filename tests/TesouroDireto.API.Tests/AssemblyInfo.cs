using Xunit;

// Vários grupos de testes deste assembly sobem seus próprios containers Postgres via
// Testcontainers (as classes de Persistence e o ApiTestFactory usado pela collection
// "api"). Com paralelização de collections habilitada (padrão do xUnit), até 6
// containers Postgres podem subir ao mesmo tempo, causando contenção no Docker Desktop
// e falhas intermitentes de conexão (ex.: "Received unknown response H for SSLRequest").
// Desabilitar a paralelização serializa a execução do assembly inteiro — mais lento,
// porém estável, o que é preferível para testes de integração dependentes de Docker.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
