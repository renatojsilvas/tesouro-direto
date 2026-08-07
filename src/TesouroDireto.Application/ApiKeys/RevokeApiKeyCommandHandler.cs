using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public sealed class RevokeApiKeyCommandHandler(
    IApiKeyWriteRepository apiKeyWriteRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<RevokeApiKeyCommand, Result>
{
    public async Task<Result> Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        var keyResult = await apiKeyWriteRepository.GetByIdAsync(request.KeyId, cancellationToken);
        if (keyResult.IsFailure || keyResult.Value.DonoUsuarioId != request.DonoUsuarioId)
        {
            return Result.Failure(ApiKeyErrors.NotFound);
        }

        var apiKey = keyResult.Value;
        apiKey.Revogar(timeProvider.GetUtcNow());

        var updateResult = await apiKeyWriteRepository.UpdateAsync(apiKey, cancellationToken);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
