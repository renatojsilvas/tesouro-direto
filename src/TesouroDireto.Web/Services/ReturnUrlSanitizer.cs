namespace TesouroDireto.Web.Services;

internal static class ReturnUrlSanitizer
{
    internal static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (!returnUrl.StartsWith('/'))
        {
            return "/";
        }

        if (returnUrl.StartsWith("//"))
        {
            return "/";
        }

        if (returnUrl.StartsWith("/\\"))
        {
            return "/";
        }

        if (returnUrl.Contains('\\'))
        {
            return "/";
        }

        return returnUrl;
    }
}
