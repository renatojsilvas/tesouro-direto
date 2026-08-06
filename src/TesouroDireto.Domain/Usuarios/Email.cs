using TesouroDireto.Domain.Common;

namespace TesouroDireto.Domain.Usuarios;

public sealed record Email
{
    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Error("Email.Invalid", "Email must not be empty.");
        }

        var trimmed = value.Trim().ToLowerInvariant();

        if (trimmed.Count(c => c == '@') != 1)
        {
            return new Error("Email.Invalid", "Email must contain exactly one '@'.");
        }

        var parts = trimmed.Split('@');

        if (string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
        {
            return new Error("Email.Invalid", "Email local and domain parts must not be empty.");
        }

        return new Email(trimmed);
    }
}
