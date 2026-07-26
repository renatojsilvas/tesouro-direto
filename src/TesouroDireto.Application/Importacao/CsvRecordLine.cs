using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Importacao;

public sealed record CsvRecordLine(int LineNumber, Result<CsvRecord> Record);
