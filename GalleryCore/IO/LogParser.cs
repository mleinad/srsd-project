using System.Security.Cryptography;
using System.Text;

namespace GalleryCore.IO;   

public class LogParser
{
    private const int HmacSize = 32;  // HMAC-SHA256 → 32 bytes

    // ------------------------------------------------------------------
    // AppendEvent
    //
    // Appends a single event to the log file without modifying existing entries.
    // Creates the file automatically if it doesn't already exist.
    // For hash chaining, it obtains the HMAC of the last entry before appending.
    // ------------------------------------------------------------------
    public void AppendEvent(LogEvent evento, string token, string filePath)
    {
        // Validate the entire file before appending
        // This ensures we never chain onto a corrupted or tampered entry
        if (File.Exists(filePath))
            ReadAllEvents(token, filePath);  // throws IntegrityViolationException if invalid
        
        // Obtains the HMAC of the last entry to chain it (zeros if it's the first)
        byte[] previousHmac = GetLastHmac(filePath);
        byte[] entryBytes = SerializeEntry(evento, token, previousHmac);

        // Opens the file in append mode, which means it will add the new data to the end of the file
        using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write);
        fs.Write(entryBytes, 0, entryBytes.Length);
    }

    // ------------------------------------------------------------------
    // ReadAllEvents
    //
    // Reads all events from the log file, validating each entry.
    // Throws IntegrityViolationException if any entry is invalid.
    // ------------------------------------------------------------------
    public List<LogEvent> ReadAllEvents(string token, string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Log file not found: {filePath}");

        var eventos = new List<LogEvent>();
        byte[] previousHmac = new byte[HmacSize];
        var (_, hmacKey)    = DeriveKeys(token);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        while (fs.Position < fs.Length)
        {
            // Read the HMAC (32 bytes)
            byte[] storedHmac = ReadExact(fs, HmacSize);

            // Read the payload length (4 bytes)
            byte[] lenBytes = ReadExact(fs, 4);
            int payloadLen  = BitConverter.ToInt32(lenBytes, 0);
            if (payloadLen <= 0)
                throw new IntegrityViolationException();

            // Read the encrypted payload
            byte[] cipherBytes = ReadExact(fs, payloadLen);

            // Verify the HMAC
            byte[] chainInput = previousHmac.Concat(lenBytes).Concat(cipherBytes).ToArray();
            byte[] expectedHmac = ComputeHMAC(chainInput, hmacKey);
          
            if (!ConstantTimeEquals(expectedHmac, storedHmac))
            {
                throw new IntegrityViolationException();
            }

            // Decrypt and reconstruct the event
            string plainText = Decrypt(cipherBytes, token);
            
            //Validate serialization for format integrity aswell
            try
            {
                eventos.Add(LogEvent.Deserialize(plainText.Trim()));
            }
            catch (FormatException)
            {
                throw new IntegrityViolationException();
            }

            previousHmac = storedHmac;
        }

        return eventos;
    }

    // ------------------------------------------------------------------
    // GetLastTimestamp
    //
    // Returns the timestamp of the last event (for validation in logappend).
    // Returns 0 if the file is empty or does not exist.
    // ------------------------------------------------------------------
    public int GetLastTimestamp(string token, string filePath)
    {
        if (!File.Exists(filePath)) return 0;
        var eventos = ReadAllEvents(token, filePath);
        return eventos.Count > 0 ? eventos[^1].Timestamp : 0;
    }

    // ------------------------------------------------------------------
    // ValidateToken
    //
    // Tries to read the file with the provided token.
    // Returns false if the token is wrong (HMAC will fail).
    // ------------------------------------------------------------------
    public bool ValidateToken(string token, string filePath)
    {
        // If the log file does not exist, there is no encrypted data available
        // to validate the token against. In this case, any token is considered
        // valid.
        if (!File.Exists(filePath)) return true;

        try
        {
            ReadAllEvents(token, filePath);
            return true;
        }
        catch (IntegrityViolationException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // DeriveKey
    //
    // Derives the AES-256 key from the token using SHA-256
    // ------------------------------------------------------------------
    private static (byte[] aesKey, byte[] hmacKey) DeriveKeys(string token)
    {
        byte[] aesKey  = SHA256.HashData(Encoding.UTF8.GetBytes("AES"  + token));
        byte[] hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes("HMAC" + token));
        return (aesKey, hmacKey);
    }

    // ------------------------------------------------------------------
    // Encrypt
    //
    // Encrypts text with AES-256-CBC + random IV
    // Result format: [ 16 bytes IV ][ N bytes ciphertext ]
    // ------------------------------------------------------------------
    private static byte[] Encrypt(string plainText, string token)
    {
        var (aesKey, _) = DeriveKeys(token);
 
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.GenerateIV();  // Random IV per entry — prevents pattern attacks
        byte[] iv = aes.IV;
 
        using var ms        = new MemoryStream();
        using var encryptor = aes.CreateEncryptor();
        using var cs        = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        byte[] data = Encoding.UTF8.GetBytes(plainText);
        cs.Write(data, 0, data.Length);
        cs.FlushFinalBlock();
        byte[] cipherText = ms.ToArray();
 
        // Stores IV + ciphertext together
        byte[] result = new byte[iv.Length + cipherText.Length];
        Array.Copy(iv, 0, result, 0, iv.Length);
        Array.Copy(cipherText, 0, result, iv.Length, cipherText.Length);
        return result;
    }

    // ------------------------------------------------------------------
    // Decrypt
    //
    // Decrypts bytes (format: [ 16 bytes I V ][ N bytes ciphertext ])
    // ------------------------------------------------------------------
    private static string Decrypt(byte[] data, string token)
    {
        if (data.Length < 17)  // minimum: 16 IV + 1 byte
            throw new IntegrityViolationException();

        var (aesKey, _) = DeriveKeys(token);

        byte[] iv = new byte[16];
        byte[] cipherText = new byte[data.Length - 16];
        Array.Copy(data, 0, iv, 0, 16);
        Array.Copy(data, 16, cipherText, 0, cipherText.Length);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV  = iv;

        try
        {
            using var ms = new MemoryStream(cipherText);
            using var decryptor = aes.CreateDecryptor();
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
            return sr.ReadToEnd();
        }
        catch (CryptographicException)
        {
            // Wrong token → invalid padding → integrity violation
            throw new IntegrityViolationException();
        }
    }

    // ------------------------------------------------------------------
    // ComputeHMAC
    //
    // Calculates HMAC-SHA256 over encrypted bytes
    // ------------------------------------------------------------------
    private static byte[] ComputeHMAC(byte[] data, byte[] hmacKey)
    {
        using var hmac = new HMACSHA256(hmacKey);
        return hmac.ComputeHash(data);
    }

    // ------------------------------------------------------------------
    // ConstantTimeEquals
    //
    // Compares two byte arrays in constant time to avoid timing attacks
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
    // SerializeEntry
    //
    // Serializes an event and prepares the complete entry for writing:
    // [ 32 bytes HMAC ][ 4 bytes length ][ N bytes payload encrypted ]
    // The hash is computed over the previous HMAC, the length of the
    // encrypted payload, and the encrypted payload itself.
    // ------------------------------------------------------------------
    private static byte[] SerializeEntry(LogEvent evento, string token, byte[] previousHmac)
    {
        var (_, hmacKey)   = DeriveKeys(token);
        byte[] cipherBytes = Encrypt(evento.Serialize(), token);
        byte[] lenBytes    = BitConverter.GetBytes(cipherBytes.Length);
 
        // Hash chain: includes the previous HMAC in the calculation
        byte[] chainInput = previousHmac.Concat(lenBytes).Concat(cipherBytes).ToArray();
        byte[] hmac = ComputeHMAC(chainInput, hmacKey);
 
        byte[] entry = new byte[HmacSize + 4 + cipherBytes.Length];
        Array.Copy(hmac, 0, entry, 0, HmacSize);
        Array.Copy(lenBytes, 0, entry, HmacSize, 4);
        Array.Copy(cipherBytes, 0, entry, HmacSize + 4, cipherBytes.Length);
        return entry;
    }

    // ------------------------------------------------------------------
    // GetLastHmac
    //
    // Gets the last HMAC from the log file without decrypting the payload
    // If the file is empty or corrupted, returns an array of 32 zeros
    // ------------------------------------------------------------------
    private static byte[] GetLastHmac(string filePath)
    {
        byte[] result = new byte[HmacSize];  // zeros by default
 
        if (!File.Exists(filePath)) return result;
 
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        if (fs.Length < HmacSize + 4) return result;
 
        // Iterates through all entries to get the last one
        while (fs.Position < fs.Length)
        {
            byte[] hmac    = ReadExact(fs, HmacSize);
            byte[] lenB    = ReadExact(fs, 4);
            int    len     = BitConverter.ToInt32(lenB, 0);
            if (len <= 0) return new byte[HmacSize];
 
            if (fs.Position + len > fs.Length) return new byte[HmacSize];
            fs.Seek(len, SeekOrigin.Current);  // skips the payload
            result = hmac;  // saves the HMAC of this entry
        }
 
        return result;
    }

    // ------------------------------------------------------------------
    // ReadExact
    //
    // Reads exactly 'count' bytes from the stream — throws exception if the file
    // is truncated (which indicates tampering)
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