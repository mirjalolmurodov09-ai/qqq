using System.Security.Cryptography;
using System.Text;
using AiUstozPro.Application.Security;

namespace AiUstozPro.Infrastructure.Services;

/// <summary>
/// Zaxira faylini parol bilan shifrlash: AES-256-GCM, kalit PBKDF2-HMAC-SHA256 (600 000 iteratsiya) bilan hosil qilinadi.
/// Format: "AUPENC1\0" | iteratsiya (4 bayt) | tuz (16) | nonce (12) | teg (16) | shifrlangan ma'lumot.
/// GCM tegi faylning o'zgartirilmaganini ham tekshiradi.
/// </summary>
public static class BackupCrypto
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AUPENC1\0");
    private const int DefaultIterations = 600_000;
    private const int HeaderSize = 8 + 4 + 16 + 12 + 16;
    public const string Extension = ".aupbak";

    public static bool IsEncrypted(string path)
    {
        if (!File.Exists(path)) return false;
        using var fs = File.OpenRead(path);
        var head = new byte[Magic.Length];
        return fs.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(Magic);
    }

    public static void EncryptFile(string sourcePath, string destPath, string password, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8) throw new BusinessRuleException("Zaxira paroli kamida 8 belgidan iborat bo'lsin.");
        var plain = File.ReadAllBytes(sourcePath);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Magic);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
        using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        fs.Write(Magic);
        fs.Write(BitConverter.GetBytes(iterations));
        fs.Write(salt);
        fs.Write(nonce);
        fs.Write(tag);
        fs.Write(cipher);
    }

    public static void DecryptFile(string sourcePath, string destPath, string password)
    {
        var all = File.ReadAllBytes(sourcePath);
        if (all.Length < HeaderSize || !all.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new BusinessRuleException("Bu fayl AI Ustoz Pro shifrlangan zaxira nusxasi emas.");
        int pos = Magic.Length;
        var iterations = BitConverter.ToInt32(all, pos); pos += 4;
        if (iterations < 10_000 || iterations > 10_000_000) throw new BusinessRuleException("Zaxira fayli sarlavhasi buzilgan.");
        var salt = all.AsSpan(pos, 16).ToArray(); pos += 16;
        var nonce = all.AsSpan(pos, 12).ToArray(); pos += 12;
        var tag = all.AsSpan(pos, 16).ToArray(); pos += 16;
        var cipher = all.AsSpan(pos).ToArray();
        var plain = new byte[cipher.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new BusinessRuleException("Parol noto'g'ri yoki fayl shikastlangan.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        File.WriteAllBytes(destPath, plain);
        CryptographicOperations.ZeroMemory(plain);
    }
}
