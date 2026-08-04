namespace TesouroDireto.Application.Feriados;

public sealed record DiasUteisFeriados(int DiasUteis, IReadOnlyCollection<DateOnly> Feriados);
