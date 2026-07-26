using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed record GetPrecoAtualByCodigoQuery(string Codigo) : IRequest<Result<PrecoTaxaDto>>;
