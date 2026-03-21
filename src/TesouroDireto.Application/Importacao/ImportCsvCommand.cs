using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Importacao;

public sealed record ImportCsvCommand : IRequest<Result<ImportResult>>;
