namespace TesouroDireto.Web.Contracts;

public sealed record SyncUsuarioRequest(string GoogleSub, string Email, string Nome, bool EmailVerified);
