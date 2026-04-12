using System.Security.Cryptography;
using System.Text;

namespace GalleryCore.IO;

public class LogParser
{
    private const int HmacSize = 32;  // HMAC-SHA256 → 32 bytes

    // ------------------------------------------------------------------
    // AppendEvent  — O(n) true single pass
    //
    // Accepts the lastHmac already computed by ReadAllEventsWithHmac,
    // so no second file scan is needed. Just serializes and writes.
    // ------------------------------------------------------------------
    public void AppendEvent(LogEvent evento, string token, string filePath, byte[] previousHmac)
    {
        byte[] entryBytes = SerializeEntry(evento, token, previousHmac);
        using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write);
        fs.Write(entryBytes, 0, entryBytes.Length);
    }

    // ------------------------------------------------------------------
    // ReadAllEventsWithHmac  — O(n) single pass, public
    //
    // Returns (events, lastHmac).
    // Validates every HMAC in the chain. Throws IntegrityViolationException
    // on any tampering. Throws FileNotFoundException if file missing.
    //
    // For a new (non-existent) log, returns (empty list, zero bytes).
    // ------------------------------------------------------------------
    public (List<LogEvent> Events, byte[] LastHmac) ReadAllEventsWithHmac(
        string token, string filePath)
    {
        if (!File.Exists(filePath))
            return (new List<LogEvent>(), new byte[HmacSize]);

        var events       = new List<LogEvent>();
        byte[] prevHmac  = new byte[HmacSize];
        var (_, hmacKey) = DeriveKeys(token);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        while (fs.Position < fs.Length)
        {
            // 1. Read stored HMAC (32 bytes)
            byte[] storedHmac = ReadExact(fs, HmacSize);

            // 2. Read payload length (4 bytes)
            byte[] lenBytes  = ReadExact(fs, 4);
            int payloadLen   = BitConverter.ToInt32(lenBytes, 0);
            if (payloadLen <= 0)
                throw new IntegrityViolationException();

            // 3. Read encrypted payload
            byte[] cipherBytes = ReadExact(fs, payloadLen);

            // 4. Verify HMAC chain
            byte[] chainInput   = prevHmac.Concat(lenBytes).Concat(cipherBytes).ToArray();
            byte[] expectedHmac = ComputeHMAC(chainInput, hmacKey);
          
            if (!ConstantTimeEquals(expectedHmac, storedHmac))
            {
                throw new IntegrityViolationException();
            }

            // 5. Decrypt and validate event fields
            string plainText = Decrypt(cipherBytes, token);
            
            //Validate serialization for format integrity aswell
            try
            {
                events.Add(LogEvent.Deserialize(plainText.Trim()));
            }
            catch (FormatException)
            {
                throw new IntegrityViolationException();
            }

            prevHmac = storedHmac;
        }

        return (events, prevHmac);
    }

    // ------------------------------------------------------------------
    // ReadAllEvents  — convenience wrapper for logread
    // ------------------------------------------------------------------
    public List<LogEvent> ReadAllEvents(string token, string filePath)
    {
        var (events, _) = ReadAllEventsWithHmac(token, filePath);
        return events;
    }

    // ------------------------------------------------------------------
    // ValidateToken  — O(n) single pass
    // ------------------------------------------------------------------
    public bool ValidateToken(string token, string filePath)
    {
        if (!File.Exists(filePath)) return true;
        try
        {
            ReadAllEventsWithHmac(token, filePath);
            return true;
        }
        catch (IntegrityViolationException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // DeriveKeys — AES-256 key + HMAC key from token via SHA-256
    // ------------------------------------------------------------------
    private static (byte[] aesKey, byte[] hmacKey) DeriveKeys(string token)
    {
        byte[] aesKey  = SHA256.HashData(Encoding.UTF8.GetBytes("AES"  + token));
        byte[] hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes("HMAC" + token));
        return (aesKey, hmacKey);
    }

    // ------------------------------------------------------------------
    // Encrypt — AES-256-CBC + random IV per entry
    // Result: [ 16 bytes IV ][ N bytes ciphertext ]
    // ------------------------------------------------------------------
    private static byte[] Encrypt(string plainText, string token)
    {
        var (aesKey, _) = DeriveKeys(token);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.GenerateIV();
        byte[] iv = aes.IV;

        using var ms        = new MemoryStream();
        using var encryptor = aes.CreateEncryptor();
        using var cs        = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        cs.Write(data, 0, data.Length);
        cs.FlushFinalBlock();
        byte[] cipherText = ms.ToArray();

        byte[] result = new byte[iv.Length + cipherText.Length];
        Array.Copy(iv, 0, result, 0, iv.Length);
        Array.Copy(cipherText, 0, result, iv.Length, cipherText.Length);
        return result;
    }

    // ------------------------------------------------------------------
    // Decrypt — [ 16 bytes IV ][ N bytes ciphertext ] → plaintext
    // ------------------------------------------------------------------
    private static string Decrypt(byte[] data, string token)
    {
        if (data.Length < 17)
            throw new IntegrityViolationException();

        var (aesKey, _) = DeriveKeys(token);

        byte[] iv         = new byte[16];
        byte[] cipherText = new byte[data.Length - 16];
        Array.Copy(data, 0, iv, 0, 16);
        Array.Copy(data, 16, cipherText, 0, cipherText.Length);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV  = iv;

        try
        {
            using var ms        = new MemoryStream(cipherText);
            using var decryptor = aes.CreateDecryptor();
            using var cs        = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr        = new StreamReader(cs);
            return sr.ReadToEnd();
        }
        catch (CryptographicException)
        {
            throw new IntegrityViolationException();
        }
    }

    // ------------------------------------------------------------------
    // ComputeHMAC — HMAC-SHA256
    // ------------------------------------------------------------------
    private static byte[] ComputeHMAC(byte[] data, byte[] hmacKey)
    {
        using var hmac = new HMACSHA256(hmacKey);
        return hmac.ComputeHash(data);
    }

    // ------------------------------------------------------------------
    // ConstantTimeEquals — timing-safe comparison
    // ------------------------------------------------------------------
    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    // ------------------------------------------------------------------
    // SerializeEntry — binary record:
    // [ 32 bytes HMAC ][ 4 bytes length ][ N bytes encrypted payload ]
    // ------------------------------------------------------------------
    private static byte[] SerializeEntry(LogEvent evento, string token, byte[] previousHmac)
    {
        var (_, hmacKey)   = DeriveKeys(token);
        byte[] cipherBytes = Encrypt(evento.Serialize(), token);
        byte[] lenBytes    = BitConverter.GetBytes(cipherBytes.Length);

        byte[] chainInput = previousHmac.Concat(lenBytes).Concat(cipherBytes).ToArray();
        byte[] hmac       = ComputeHMAC(chainInput, hmacKey);

        byte[] entry = new byte[HmacSize + 4 + cipherBytes.Length];
        Array.Copy(hmac,        0, entry, 0,            HmacSize);
        Array.Copy(lenBytes,    0, entry, HmacSize,     4);
        Array.Copy(cipherBytes, 0, entry, HmacSize + 4, cipherBytes.Length);
        return entry;
    }

    // ------------------------------------------------------------------
    // ReadExact — reads exactly 'count' bytes; throws on truncation
    // ------------------------------------------------------------------
    private static byte[] ReadExact(FileStream fs, int count)
    {
        byte[] buf   = new byte[count];
        int    total = 0;
        while (total < count)
        {
            int read = fs.Read(buf, total, count - total);
            if (read == 0) throw new IntegrityViolationException();
            total += read;
        }
        return buf;
    }
}