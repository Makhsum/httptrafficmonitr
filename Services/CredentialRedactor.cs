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

        // Query parameters, form-encoded fields and JSON properties whose value is a credential
        private static readonly string[] CredentialFields =
        {
            "access_token", "refresh_token", "id_token", "token", "auth", "auth_token", "authorization",
            "api_key", "apikey", "client_secret", "password", "session_token", "sessionid",
            "sig", "signature", "x-amz-signature", "x-amz-credential", "x-amz-security-token",
            "passwd", "pwd", "client_assertion",
            "accessToken", "refreshToken", "idToken", "authToken", "apiKey", "clientSecret", "sessionToken"
        };

        // Any other field name holding "password" or "passwd": new_password, password_confirmation, user[password], confirmPassword
        private const string PasswordLikeField = @"[^=&?#\s""\\]*?pass(?:word|wd)[^=&?#\s""\\]*";

        // Headers that carry a bare API key or token, with no scheme word in front of it
        private static readonly string[] ApiKeyHeaders =
        {
            "x-api-key", "x-apikey", "api-key", "apikey", "x-api-token", "x-auth-token", "x-access-token",
            "x-session-token", "x-goog-api-key", "x-amz-security-token", "ocp-apim-subscription-key", "private-token"
        };

        // "Name: value" lines of a header block, also a STOMP frame inside a WebSocket message
        private static readonly Regex CredentialHeaderLine = new(
            @"^(?<name>[ \t]*(?:authorization|proxy-authorization|cookie|set-cookie|"
                + string.Join("|", ApiKeyHeaders.Select(Regex.Escape)) + @")[ \t]*:[ \t]*)(?<value>[^\r\n]*)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // "?token=value" or "&token=value" in a URL, a request line or a message that quotes a URL,
        // and "password=value" at the start of a form-encoded body or of a JSON string that echoes one
        private static readonly Regex UrlEncodedCredentialField = new(
            @"(?<=^|[?&""])(?<name>(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")=)(?<value>[^&#\s""'<>\\]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "password": "value" in a JSON body or WebSocket message, also an API-key header a server echoes
        // back as JSON; an object or array under such a name stays
        private static readonly Regex JsonCredentialField = new(
            @"(?<name>""(?:" + string.Join("|", CredentialFields.Concat(ApiKeyHeaders).Select(Regex.Escape)) + "|" + PasswordLikeField + @")""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"password\": \"value\", as an echo service or a logged payload quotes it;
        // a quote inside the value arrives as \\\" and must not end it
        private static readonly Regex EscapedJsonCredentialField = new(
            @"(?<name>\\""(?:" + string.Join("|", CredentialFields.Concat(ApiKeyHeaders).Select(Regex.Escape)) + "|" + PasswordLikeField + @")\\""\s*:\s*)(?<value>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A credential a service carries as a URL path segment: a Telegram bot token (/bot123456:AAE.../getUpdates,
        // also /file/bot.../), the secret of a Slack or Discord webhook URL; host and endpoint stay readable.
        // Matched by the path alone, since a request line (GET /bot.../getMe HTTP/1.1) has no host in front of it.
        // The slash may arrive escaped as \/ when a JSON body quotes the URL
        private static readonly Regex UrlPathCredential = new(
            @"(?<name>\\?/bot)(?<value>\d+(?::|%3A)[A-Za-z0-9_-]{20,})"
                + @"|(?<name>\\?/services\\?/(?-i:T[A-Z0-9]+\\?/B[A-Z0-9]+)\\?/)(?<value>[A-Za-z0-9]+)"
                + @"|(?<name>\\?/api\\?/(?:v\d+\\?/)?webhooks\\?/\d{17,20}\\?/)(?<value>[A-Za-z0-9_-]+)",
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
            redacted = UrlEncodedCredentialField.Replace(redacted, m =>
                m.Groups["name"].Value + Marker);
            redacted = UrlPathCredential.Replace(redacted, m =>
                m.Groups["name"].Value + Marker);
            redacted = JsonCredentialField.Replace(redacted, m =>
                m.Groups["name"].Value + (m.Groups["value"].Value == "\"\"" ? "\"\"" : "\"" + Marker + "\""));
            redacted = EscapedJsonCredentialField.Replace(redacted, m =>
                m.Groups["name"].Value + (m.Groups["value"].Value == "\\\"\\\"" ? "\\\"\\\"" : "\\\"" + Marker + "\\\""));

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

            // An API-key header holds nothing but the key
            if (!name.EndsWith("authorization", StringComparison.OrdinalIgnoreCase)) return Marker;

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
