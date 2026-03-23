using System;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HttpTrafficMonitor.Helpers
{
    public static class FormatHelper
    {
        public const int MaxBodyCaptureSize = 1_048_576; // 1 MB

        public static string FormatBody(string? body, string? contentType)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;

            try
            {
                if (contentType != null)
                {
                    if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                        return FormatJson(body);
                    if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
                        contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                        return FormatXml(body);
                }

                // Try to detect JSON/XML by content
                string trimmed = body.TrimStart();
                if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                {
                    try { return FormatJson(body); } catch { }
                }
                if (trimmed.StartsWith('<'))
                {
                    try { return FormatXml(body); } catch { }
                }
            }
            catch
            {
                // Return as-is if formatting fails
            }

            return body;
        }

        public static string FormatJson(string json)
        {
            var obj = JToken.Parse(json);
            return obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public static string FormatXml(string xml)
        {
            var doc = XDocument.Parse(xml);
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineOnAttributes = false,
                OmitXmlDeclaration = false
            };
            using (var writer = XmlWriter.Create(sb, settings))
            {
                doc.WriteTo(writer);
            }
            return sb.ToString();
        }

        public static string FormatHeaders(string headers)
        {
            if (string.IsNullOrWhiteSpace(headers)) return string.Empty;
            return headers.Replace("\r\n", "\n").TrimEnd('\n');
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:F1} {units[unitIndex]}";
        }
    }
}
