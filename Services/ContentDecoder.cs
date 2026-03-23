using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HttpTrafficMonitor.Services
{
    public static class ContentDecoder
    {
        public static DecodedContent Decode(string content, string? contentEncoding = null)
        {
            var result = new DecodedContent { Original = content };

            // Try content-encoding based decompression
            if (!string.IsNullOrEmpty(contentEncoding))
            {
                byte[]? bytes = TryGetBytes(content);
                if (bytes != null)
                {
                    string? decompressed = contentEncoding.ToLowerInvariant() switch
                    {
                        "gzip" => TryDecompressGzip(bytes),
                        "deflate" => TryDecompressDeflate(bytes),
                        "br" => TryDecompressBrotli(bytes),
                        _ => null
                    };
                    if (decompressed != null)
                    {
                        result.Decoded = decompressed;
                        result.DecodingApplied = $"Decompressed ({contentEncoding})";
                        return result;
                    }
                }
            }

            // Try Base64 detection and decoding
            if (IsLikelyBase64(content))
            {
                string? decoded = TryDecodeBase64(content.Trim());
                if (decoded != null)
                {
                    result.Decoded = decoded;
                    result.DecodingApplied = "Base64 decoded";
                    return result;
                }
            }

            // Try URL decoding
            if (content.Contains('%'))
            {
                try
                {
                    string urlDecoded = Uri.UnescapeDataString(content);
                    if (urlDecoded != content)
                    {
                        result.Decoded = urlDecoded;
                        result.DecodingApplied = "URL decoded";
                        return result;
                    }
                }
                catch { }
            }

            result.Decoded = content;
            result.DecodingApplied = "No encoding detected";
            return result;
        }

        public static string? TryDecodeBase64(string input)
        {
            try
            {
                string cleaned = input.Trim().Replace("\r", "").Replace("\n", "");
                byte[] bytes = Convert.FromBase64String(cleaned);
                string decoded = Encoding.UTF8.GetString(bytes);
                // Verify it's valid text
                if (decoded.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
                    return null;
                return decoded;
            }
            catch { return null; }
        }

        public static string EncodeBase64(string input)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(input));

        public static string UrlEncode(string input) => Uri.EscapeDataString(input);
        public static string UrlDecode(string input) => Uri.UnescapeDataString(input);

        private static bool IsLikelyBase64(string s)
        {
            string trimmed = s.Trim();
            if (trimmed.Length < 8 || trimmed.Length > 1_000_000) return false;
            return Regex.IsMatch(trimmed, @"^[A-Za-z0-9+/\r\n]+=*$") && trimmed.Length % 4 == 0;
        }

        private static byte[]? TryGetBytes(string content)
        {
            try { return Convert.FromBase64String(content); }
            catch
            {
                try { return Encoding.Latin1.GetBytes(content); }
                catch { return null; }
            }
        }

        private static string? TryDecompressGzip(byte[] data)
        {
            try
            {
                using var input = new MemoryStream(data);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch { return null; }
        }

        private static string? TryDecompressDeflate(byte[] data)
        {
            try
            {
                using var input = new MemoryStream(data);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var reader = new StreamReader(deflate, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch { return null; }
        }

        private static string? TryDecompressBrotli(byte[] data)
        {
            try
            {
                using var input = new MemoryStream(data);
                using var brotli = new BrotliStream(input, CompressionMode.Decompress);
                using var reader = new StreamReader(brotli, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch { return null; }
        }
    }

    public class DecodedContent
    {
        public string Original { get; set; } = string.Empty;
        public string Decoded { get; set; } = string.Empty;
        public string DecodingApplied { get; set; } = string.Empty;
        public bool WasDecoded => DecodingApplied != "No encoding detected";
    }
}
