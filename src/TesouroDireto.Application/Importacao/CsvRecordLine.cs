using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Importacao;

/// <summary>
/// Um registro (ou falha de parsing) emitido pelo parser de CSV, junto com o número
/// da linha FÍSICA do arquivo de origem (contando o header e linhas em branco puladas).
/// Usado para que erros reportados em log apontem para a linha real do arquivo.
/// </summary>
public sealed record CsvRecordLine(int LineNumber, Result<CsvRecord> Record);
