using MediatR;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.Tributos;

public sealed record SeedTributosCommand : IRequest<Result>;
