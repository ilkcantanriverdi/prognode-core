namespace Prognode.Contracts;

/// <summary>
/// The rule for every password or passphrase a person sets in PROGNODE (backup passphrases here; PROGNODE
/// accounts use the same rule in PROGNODE Cloud): at least 8 characters with an upper-case letter, a
/// lower-case letter, a digit and a special character. Existing passwords are not re-checked when used.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 1024;
    public const string Requirement = "at least 8 characters, with an upper-case letter, a lower-case letter, a digit and a special character";

    public static bool IsValid(string? password) =>
        password is { Length: >= MinimumLength and <= MaximumLength } &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit) &&
        password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
}
