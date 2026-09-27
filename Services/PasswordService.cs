using System.Security.Cryptography;
namespace AzumahCloud.Api.Services;
public sealed class PasswordService
{
    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);
        return $"120000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    public bool Verify(string password, string stored)
    {
        try
        {
            var p = stored.Split('.', 3);
            if (p.Length != 3) return false;
            byte[] salt = Convert.FromBase64String(p[1]);
            byte[] expected = Convert.FromBase64String(p[2]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, int.Parse(p[0]), HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}
