using MediatR;
using TesouroDireto.Application.Common.Interfaces;
using TesouroDireto.Domain.ApiKeys;
using TesouroDireto.Domain.Common;

namespace TesouroDireto.Application.ApiKeys;

public sealed class GenerateApiKeyCommandHandler(
    IApiKeyGenerator apiKeyGenerator,
    IApiKeyWriteRepository apiKeyWriteRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<GenerateApiKeyCommand, Result<GeneratedApiKeyDto>>
{
    public async Task<Result<GeneratedApiKeyDto>> Handle(GenerateApiKeyCommand request, CancellationToken cancellationToken)
    {
        var material = apiKeyGenerator.Generate();
        var hash = ApiKeyHash.FromRawKey(material.RawKey);
        var criadaEm = timeProvider.GetUtcNow();

        var apiKeyResult = ApiKey.Create(request.Nome, hash, material.Prefixo, request.DonoUsuarioId, criadaEm);
        if (apiKeyResult.IsFailure)
        {
            return Result<GeneratedApiKeyDto>.Failure(apiKeyResult.Error);
        }

        var apiKey = apiKeyResult.Value;

        var addResult = await apiKeyWriteRepository.AddAsync(apiKey, cancellationToken);
        if (addResult.IsFailure)
        {
            return Result<GeneratedApiKeyDto>.Failure(addResult.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<GeneratedApiKeyDto>.Success(
            new GeneratedApiKeyDto(apiKey.Id, apiKey.Nome, apiKey.Prefixo, material.RawKey, apiKey.CriadaEm));
    }
}
