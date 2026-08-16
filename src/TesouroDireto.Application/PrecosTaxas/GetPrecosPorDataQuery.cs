using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed record GetPrecosPorDataQuery(string? DataBase) : IRequest<Result<IReadOnlyList<PrecoTaxaDiaDto>>>;
