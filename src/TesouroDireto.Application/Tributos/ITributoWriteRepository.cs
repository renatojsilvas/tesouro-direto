using TesouroDireto.Domain.Common;
using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public interface ITributoWriteRepository
{
    Task<Result> AddAsync(Tributo tributo, CancellationToken cancellationToken);
    Task<Result> UpdateAsync(Tributo tributo, CancellationToken cancellationToken);
}
