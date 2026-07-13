using System.IO;
using System.Text;

namespace AlphaBoysLab.SerializedShield.Editor
{
    public sealed class SerializedShieldTextFileContent
    {
        public string Text;
        public Encoding Encoding;
        public bool HasBom;
    }

    /// <summary>
    /// Encoding-preserving text file IO (audit U-H6). Files are read with BOM detection
    /// (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE) and written back with the exact same encoding
    /// and BOM presence. Files without a BOM are decoded as strict UTF-8; if the bytes
    /// are not valid UTF-8 a DecoderFallbackException is thrown so callers fail loudly
    /// instead of silently corrupting the file.
    /// </summary>
    public static class SerializedShieldTextFileUtility
    {
        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
        private static readonly byte[] Utf32LeBom = { 0xFF, 0xFE, 0x00, 0x00 };
        private static readonly byte[] Utf32BeBom = { 0x00, 0x00, 0xFE, 0xFF };
        private static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };
        private static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };

        public static SerializedShieldTextFileContent Read(string absolutePath)
        {
            byte[] bytes = File.ReadAllBytes(absolutePath);
            Encoding encoding;
            bool hasBom;
            int bomLength;
            DetectEncoding(bytes, out encoding, out hasBom, out bomLength);

            return new SerializedShieldTextFileContent
            {
                Text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength),
                Encoding = encoding,
                HasBom = hasBom
            };
        }

        public static void Write(string absolutePath, SerializedShieldTextFileContent original, string newText)
        {
            byte[] body = original.Encoding.GetBytes(newText);

            if (!original.HasBom)
            {
                File.WriteAllBytes(absolutePath, body);
                return;
            }

            byte[] bom = GetBomBytes(original.Encoding);
            byte[] output = new byte[bom.Length + body.Length];
            System.Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
            System.Buffer.BlockCopy(body, 0, output, bom.Length, body.Length);
            File.WriteAllBytes(absolutePath, output);
        }

        private static void DetectEncoding(byte[] bytes, out Encoding encoding, out bool hasBom, out int bomLength)
        {
            if (StartsWith(bytes, Utf32LeBom))
            {
                encoding = new UTF32Encoding(false, false, true);
                hasBom = true;
                bomLength = 4;
                return;
            }

            if (StartsWith(bytes, Utf32BeBom))
            {
                encoding = new UTF32Encoding(true, false, true);
                hasBom = true;
                bomLength = 4;
                return;
            }

            if (StartsWith(bytes, Utf8Bom))
            {
                encoding = new UTF8Encoding(false, true);
                hasBom = true;
                bomLength = 3;
                return;
            }

            if (StartsWith(bytes, Utf16LeBom))
            {
                encoding = new UnicodeEncoding(false, false, true);
                hasBom = true;
                bomLength = 2;
                return;
            }

            if (StartsWith(bytes, Utf16BeBom))
            {
                encoding = new UnicodeEncoding(true, false, true);
                hasBom = true;
                bomLength = 2;
                return;
            }

            encoding = new UTF8Encoding(false, true);
            hasBom = false;
            bomLength = 0;
        }

        private static byte[] GetBomBytes(Encoding encoding)
        {
            if (encoding is UTF32Encoding)
            {
                return IsBigEndianUtf32(encoding) ? Utf32BeBom : Utf32LeBom;
            }

            if (encoding is UnicodeEncoding)
            {
                return IsBigEndianUtf16(encoding) ? Utf16BeBom : Utf16LeBom;
            }

            return Utf8Bom;
        }

        private static bool IsBigEndianUtf16(Encoding encoding)
        {
            // Big-endian UTF-16 encodes 'A' as 0x00 0x41.
            byte[] probe = encoding.GetBytes("A");
            return probe.Length == 2 && probe[0] == 0x00;
        }

        private static bool IsBigEndianUtf32(Encoding encoding)
        {
            byte[] probe = encoding.GetBytes("A");
            return probe.Length == 4 && probe[0] == 0x00;
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (bytes.Length < prefix.Length)
            {
                return false;
            }

            for (int index = 0; index < prefix.Length; index++)
            {
                if (bytes[index] != prefix[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
