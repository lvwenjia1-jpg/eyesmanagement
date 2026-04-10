using System.Security.Cryptography;

namespace MainApi.Services;

public sealed class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public (string Salt, string Hash) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(saltBase64) ||
            string.IsNullOrWhiteSpace(hashBase64))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expectedHash = Convert.FromBase64String(hashBase64);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }
}
