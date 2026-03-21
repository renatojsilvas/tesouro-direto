using TesouroDireto.Domain.Tributos;

namespace TesouroDireto.Application.Tributos;

public interface ITributoWriteRepository
{
    Task AddAsync(Tributo tributo, CancellationToken cancellationToken);
    void Update(Tributo tributo);
}
