namespace TesouroDireto.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);
