using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HttpTrafficMonitor.Services
{
    // Hides credential values in what the local API hands to MCP tools: the name stays, the value goes
    public class CredentialRedactor
    {
        public const string Marker = "[redacted]";

        public const string Notice =
            "Credential values are hidden by a setting in HTTP Traffic Monitor; only the user can change it there.";

        private static readonly string[] TokenQueryParameters =
        {
            "access_token", "refresh_token", "id_token", "token", "auth", "auth_token", "authorization",
            "api_key", "apikey", "client_secret", "password", "session_token", "sessionid",
            "sig", "signature", "x-amz-signature", "x-amz-credential", "x-amz-security-token"
        };

        // "Name: value" lines of a header block, also a STOMP frame inside a WebSocket message
        private static readonly Regex CredentialHeaderLine = new(
            @"^(?<name>[ \t]*(?:authorization|proxy-authorization|cookie|set-cookie)[ \t]*:[ \t]*)(?<value>[^\r\n]*)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // "?token=value" or "&token=value" in a URL, a request line or a message that quotes a URL
        private static readonly Regex TokenQueryParameter = new(
            @"(?<=[?&])(?<name>(?:" + string.Join("|", TokenQueryParameters.Select(Regex.Escape)) + @")=)(?<value>[^&#\s""'<>\\]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Several Set-Cookie headers joined into one line by ", ", as a replay answer lists them;
        // the comma inside "Expires=Wed, 21 Oct 2026" is not followed by a name=value pair
        private static readonly Regex JoinedSetCookies = new(@",\s*(?=[^;,=\s]+=)", RegexOptions.Compiled);

        private readonly bool _revealCredentials;

        public CredentialRedactor(bool revealCredentials)
        {
            _revealCredentials = revealCredentials;
        }

        public bool RedactedAny { get; private set; }

        // The notice an answer carries once it hid a value, null otherwise
        public string? NoticeIfRedacted => RedactedAny ? Notice : null;

        public string Redact(string text)
        {
            if (_revealCredentials || string.IsNullOrEmpty(text)) return text;

            string redacted = CredentialHeaderLine.Replace(text, m =>
                m.Groups["name"].Value + RedactHeaderValue(m.Groups["name"].Value.Trim().TrimEnd(':').Trim(), m.Groups["value"].Value));
            redacted = TokenQueryParameter.Replace(redacted, m =>
                m.Groups["name"].Value + Marker);

            if (redacted != text) RedactedAny = true;
            return redacted;
        }

        public string? RedactOrNull(string? text) => text == null ? null : Redact(text);

        private static string RedactHeaderValue(string name, string value)
        {
            if (value.Trim().Length == 0) return value;

            if (name.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                return string.Join("; ", value.Split(';').Select(c => c.Trim().Length == 0 ? "" : RedactNameValuePair(c.Trim())));

            if (name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
                return string.Join(", ", JoinedSetCookies.Split(value).Select(RedactSetCookie));

            // Authorization: the scheme word (Bearer, Basic, Digest) is no secret and helps debugging
            string trimmed = value.Trim();
            int space = trimmed.IndexOf(' ');
            return space < 0 ? Marker : trimmed[..space] + " " + Marker;
        }

        private static string RedactSetCookie(string cookie)
        {
            // Only the first pair is the cookie; Path, Domain, Expires, Secure, HttpOnly stay readable
            int end = cookie.IndexOf(';');
            return end < 0 ? RedactNameValuePair(cookie.Trim()) : RedactNameValuePair(cookie[..end].Trim()) + cookie[end..];
        }

        private static string RedactNameValuePair(string pair)
        {
            int eq = pair.IndexOf('=');
            return eq < 0 ? Marker : pair[..(eq + 1)] + Marker;
        }
    }
}
