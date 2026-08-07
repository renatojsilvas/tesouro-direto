using Microsoft.AspNetCore.Http;
using TesouroDireto.API.Middleware;

namespace TesouroDireto.API.Http;

internal static class ApiKeyIdentityExtensions
{
    public static bool IsServiceIdentity(this HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ApiKeyMiddleware.IdentityKindItemsKey, out var kind)
        && kind as string == ApiKeyMiddleware.ServiceIdentityKind;
}
