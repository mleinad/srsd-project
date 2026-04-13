using System.Security.Cryptography;
using System.Text;

namespace GalleryCore.IO;

public class LogParser
{
    private const int HmacSize = 32;
    private const int IvSize   = 16;
    private const int PaddedPlaintextSize = 256;
    private const int KdfIterations = 100_000;
    private const string DummyPrefix = "DUMMY";

    private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("GLOG");
    private const byte FormatVersion = 1;
    private const int HeaderSize = 5;

    private static string? _cachedToken;
    private static byte[]? _cachedAesKey;
    private static byte[]? _cachedHmacKey;

    public void AppendEvent(LogEvent evento, string token, string filePath, byte[] previousHmac)
    {
        using var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);

        if (fs.Length == 0)
        {
            fs.Write(MagicHeader, 0, MagicHeader.Length);
            fs.WriteByte(FormatVersion);
        }
        fs.Seek(0, SeekOrigin.End);

        byte[] realEntry = SerializeEntry(evento.Serialize(), token, previousHmac);
        fs.Write(realEntry, 0, realEntry.Length);
        byte[] lastHmac = ExtractHmac(realEntry);

        int dummyCount = RandomNumberGenerator.GetInt32(0, 3);
        for (int d = 0; d < dummyCount; d++)
        {
            string dummyPayload = DummyPrefix + "," + RandomNumberGenerator.GetInt32(0, int.MaxValue);
            byte[] dummyEntry = SerializeEntry(dummyPayload, token, lastHmac);
            fs.Write(dummyEntry, 0, dummyEntry.Length);
            lastHmac = ExtractHmac(dummyEntry);
        }
    }

    public (List<LogEvent> Events, byte[] LastHmac) ReadAllEventsWithHmac(
        string token, string filePath)
    {
        if (!File.Exists(filePath))
            return (new List<LogEvent>(), new byte[HmacSize]);

        var events       = new List<LogEvent>();
        byte[] prevHmac  = new byte[HmacSize];
        var (_, hmacKey) = DeriveKeys(token);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

        if (fs.Length < HeaderSize)
            throw new IntegrityViolationException();

        byte[] header = ReadExact(fs, MagicHeader.Length);
        if (!ConstantTimeEquals(header, MagicHeader))
            throw new IntegrityViolationException();

        int version = fs.ReadByte();
        if (version != FormatVersion)
            throw new IntegrityViolationException();

        while (fs.Position < fs.Length)
        {
            byte[] storedHmac = ReadExact(fs, HmacSize);

            byte[] lenBytes  = ReadExact(fs, 4);
            int payloadLen   = BitConverter.ToInt32(lenBytes, 0);
            
            if (payloadLen <= 0 || payloadLen > 65_536)
            {
                throw new IntegrityViolationException();
            }

            byte[] cipherBytes = ReadExact(fs, payloadLen);

            byte[] chainInput   = prevHmac.Concat(lenBytes).Concat(cipherBytes).ToArray();
            byte[] expectedHmac = ComputeHMAC(chainInput, hmacKey);
          
            if (!ConstantTimeEquals(expectedHmac, storedHmac))
            {
                throw new IntegrityViolationException();
            }

            string plainText = Decrypt(cipherBytes, token);

            if (!plainText.StartsWith(DummyPrefix))
            {
                try
                {
                    events.Add(LogEvent.Deserialize(plainText));
                }
                catch (FormatException)
                {
                    throw new IntegrityViolationException();
                }
            }

            prevHmac = storedHmac;
        }

        return (events, prevHmac);
    }

    public List<LogEvent> ReadAllEvents(string token, string filePath)
    {
        var (events, _) = ReadAllEventsWithHmac(token, filePath);
        return events;
    }

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

    private static (byte[] aesKey, byte[] hmacKey) DeriveKeys(string token)
    {
        if (_cachedToken == token && _cachedAesKey != null && _cachedHmacKey != null)
            return (_cachedAesKey, _cachedHmacKey);

        byte[] tokenBytes = Encoding.UTF8.GetBytes(token);

        byte[] aesKey  = StretchKey(tokenBytes, Encoding.UTF8.GetBytes("GalleryLog_AES_Salt_v1"));
        byte[] hmacKey = StretchKey(tokenBytes, Encoding.UTF8.GetBytes("GalleryLog_HMAC_Salt_v1"));

        _cachedToken   = token;
        _cachedAesKey  = aesKey;
        _cachedHmacKey = hmacKey;

        return (aesKey, hmacKey);
    }

    private static byte[] StretchKey(byte[] token, byte[] salt)
    {
        byte[] state;
        using (var sha = SHA256.Create())
        {
            sha.TransformBlock(salt, 0, salt.Length, null, 0);
            sha.TransformFinalBlock(token, 0, token.Length);
            state = sha.Hash!;
        }

        for (int i = 1; i <= KdfIterations; i++)
        {
            byte[] iterBytes = BitConverter.GetBytes(i);
            using var sha = SHA256.Create();
            sha.TransformBlock(state, 0, state.Length, null, 0);
            sha.TransformBlock(token, 0, token.Length, null, 0);
            sha.TransformFinalBlock(iterBytes, 0, iterBytes.Length);
            state = sha.Hash!;
        }

        return state;
    }

    private static byte[] Encrypt(string plainText, string token)
    {
        var (aesKey, _) = DeriveKeys(token);

        byte[] rawData = Encoding.UTF8.GetBytes(plainText);
        int paddedLen = PaddedPlaintextSize;
        if (rawData.Length >= paddedLen)
            paddedLen = ((rawData.Length / PaddedPlaintextSize) + 1) * PaddedPlaintextSize;

        byte[] dataWithLength = new byte[4 + paddedLen];
        BitConverter.GetBytes(rawData.Length).CopyTo(dataWithLength, 0);
        Array.Copy(rawData, 0, dataWithLength, 4, rawData.Length);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.GenerateIV();
        byte[] iv = aes.IV;

        using var ms        = new MemoryStream();
        using var encryptor = aes.CreateEncryptor();
        using var cs        = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        cs.Write(dataWithLength, 0, dataWithLength.Length);
        cs.FlushFinalBlock();
        byte[] cipherText = ms.ToArray();

        byte[] result = new byte[iv.Length + cipherText.Length];
        Array.Copy(iv, 0, result, 0, iv.Length);
        Array.Copy(cipherText, 0, result, iv.Length, cipherText.Length);
        return result;
    }

    private static string Decrypt(byte[] data, string token)
    {
        if (data.Length < IvSize + 1)
            throw new IntegrityViolationException();

        var (aesKey, _) = DeriveKeys(token);

        byte[] iv         = new byte[IvSize];
        byte[] cipherText = new byte[data.Length - IvSize];
        Array.Copy(data, 0, iv, 0, IvSize);
        Array.Copy(data, IvSize, cipherText, 0, cipherText.Length);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV  = iv;

        byte[] plainBytes;
        try
        {
            using var ms        = new MemoryStream(cipherText);
            using var decryptor = aes.CreateDecryptor();
            using var cs        = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var output    = new MemoryStream();
            cs.CopyTo(output);
            plainBytes = output.ToArray();
        }
        catch (CryptographicException)
        {
            throw new IntegrityViolationException();
        }

        if (plainBytes.Length < 4)
            throw new IntegrityViolationException();

        int actualLen = BitConverter.ToInt32(plainBytes, 0);
        if (actualLen <= 0 || actualLen > plainBytes.Length - 4)
            throw new IntegrityViolationException();

        return Encoding.UTF8.GetString(plainBytes, 4, actualLen);
    }

    private static byte[] ComputeHMAC(byte[] data, byte[] hmacKey)
    {
        using var hmac = new HMACSHA256(hmacKey);
        return hmac.ComputeHash(data);
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static byte[] SerializeEntry(string plainText, string token, byte[] previousHmac)
    {
        var (_, hmacKey)   = DeriveKeys(token);
        byte[] cipherBytes = Encrypt(plainText, token);
        byte[] lenBytes    = BitConverter.GetBytes(cipherBytes.Length);

        byte[] chainInput = previousHmac.Concat(lenBytes).Concat(cipherBytes).ToArray();
        byte[] hmac       = ComputeHMAC(chainInput, hmacKey);

        byte[] entry = new byte[HmacSize + 4 + cipherBytes.Length];
        Array.Copy(hmac,        0, entry, 0,            HmacSize);
        Array.Copy(lenBytes,    0, entry, HmacSize,     4);
        Array.Copy(cipherBytes, 0, entry, HmacSize + 4, cipherBytes.Length);
        return entry;
    }

    private static byte[] ExtractHmac(byte[] entry)
    {
        byte[] hmac = new byte[HmacSize];
        Array.Copy(entry, 0, hmac, 0, HmacSize);
        return hmac;
    }

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
