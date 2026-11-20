using System.Security.Cryptography;
using System.Text;

namespace SalesManagement_SysDev;

/// <summary>Versioned PBKDF2 password storage for employee credentials.</summary>
public static class EmployeePasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltLength = 16;
    private const int KeyLength = 32;
    private const string Prefix = "pbkdf2-sha256";
    private const string PlaceholderAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations)
            || iterations is < 100_000 or > 2_000_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltLength || expected.Length != KeyLength) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    public static bool VerifyLegacy(string password, string? legacy)
    {
        if (legacy == null) return false;
        var supplied = Encoding.UTF8.GetBytes(password);
        var stored = Encoding.UTF8.GetBytes(legacy);
        return supplied.Length == stored.Length && CryptographicOperations.FixedTimeEquals(supplied, stored);
    }

    public static string CreateLegacyPlaceholder()
    {
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++) chars[i] = PlaceholderAlphabet[RandomNumberGenerator.GetInt32(PlaceholderAlphabet.Length)];
        return new string(chars);
    }
}
