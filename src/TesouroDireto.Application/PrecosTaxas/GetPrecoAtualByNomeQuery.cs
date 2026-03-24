using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.PrecosTaxas;

public sealed record GetPrecoAtualByNomeQuery(string Nome) : IRequest<Result<PrecoTaxaDto>>;
