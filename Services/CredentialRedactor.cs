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

        // The same for a URL-encoded name, where a % may only start an encoded [ or ]
        private const string NestedPasswordLikeField = @"(?:[^=&?#%\s""\\]|%(?:25)*5[BD])*?pass(?:word|wd)(?:[^=&?#%\s""\\]|%(?:25)*5[BD])*";

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
        // "#access_token=value" in the fragment of a redirect target,
        // and "password=value" at the start of a form-encoded body or of a JSON string that echoes one
        private static readonly Regex UrlEncodedCredentialField = new(
            @"(?<=^|[?&#""])(?<name>(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")=)(?<value>[^&#\s""'<>\\]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same URL-encoded inside another query value, as a redirect target carries it:
        // ?url=http%3A%2F%2Fexample.com%2Fcb%3Faccess_token%3Dvalue, also next= or return_to= encoded twice (%253F, %253D).
        // The value ends at the outer & or at the next encoded & or #; an encoded user%5Bpassword%5D counts as password-like.
        // The name may also open the outer value itself, as ?state=access_token%3Dvalue carries it
        private static readonly Regex NestedUrlEncodedCredentialField = new(
            @"(?<name>(?:%(?:25)*(?:3F|26|23)|(?<=[?&][^=&#\s""'<>\\]*=))(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + NestedPasswordLikeField + @")%(?:25)*3D)"
                + @"(?<value>(?:(?!%(?:25)*(?:26|23))[^&#\s""'<>\\])+)",
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

        // A Cookie or Set-Cookie header a server echoes back as JSON ("Cookie": "session=value", also a list of
        // Set-Cookie strings, and "HTTP_COOKIE" as a CGI or PHP server lists it); each cookie keeps its name like in the header itself
        private static readonly Regex JsonCookieField = new(
            @"(?<name>""(?:http_)?(?<header>cookie|set-cookie)""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|\[\s*""(?:[^""\\]|\\.)*""(?:\s*,\s*""(?:[^""\\]|\\.)*"")*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"Cookie\": \"session=value\"
        private static readonly Regex EscapedJsonCookieField = new(
            @"(?<name>\\""(?:http_)?(?<header>cookie|set-cookie)\\""\s*:\s*)(?<value>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\"")",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The cookies an echo service lists already parsed, one property per cookie ("cookies": {"session": "value"}),
        // also Express's "signedCookies" and PHP's "_COOKIE"; the cookie names stay; a cookie that holds an object
        // of its own (Express's j: cookies) loses its values too
        private const string JsonCookieObjectName = @"(?:cookies?|signedCookies|_COOKIE)";

        private const string JsonObjectMember = @"[^{}""]|""(?:[^""\\]|\\.)*""";

        private static readonly Regex JsonCookieObject = new(
            @"(?<name>""" + JsonCookieObjectName + @"""\s*:\s*)(?<value>\{(?:" + JsonObjectMember + @"|\{(?:" + JsonObjectMember + @")*\})*\})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"cookies\": {\"session\": \"value\"}
        private static readonly Regex EscapedJsonCookieObject = new(
            @"(?<name>\\""" + JsonCookieObjectName + @"\\""\s*:\s*)(?<value>\{(?:[^{}\\]|\\.|\{(?:[^{}\\]|\\.)*\})*\})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The cookies a HAR-style echo service (mockbin and similar) lists as name/value objects,
        // "cookies": [{"name": "session", "value": "value", "path": "/"}]; only each "value" goes
        private static readonly Regex JsonCookieList = new(
            @"(?<name>""cookies?""\s*:\s*)(?<value>\[\s*\{(?:" + JsonObjectMember + @")*\}(?:\s*,\s*\{(?:" + JsonObjectMember + @")*\})*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"cookies\": [{\"name\": \"session\", \"value\": \"value\"}]
        private static readonly Regex EscapedJsonCookieList = new(
            @"(?<name>\\""cookies?\\""\s*:\s*)(?<value>\[\s*\{(?:[^{}\\]|\\.)*\}(?:\s*,\s*\{(?:[^{}\\]|\\.)*\})*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The "value" property of one entry of such a list
        private static readonly Regex JsonCookieListValue = new(
            @"(?<name>""value""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EscapedJsonCookieListValue = new(
            @"(?<name>\\""value\\""\s*:\s*)(?<value>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // One "name": "value" property of such an object
        private static readonly Regex JsonCookieProperty = new(
            @"(?<name>""(?:[^""\\]|\\.)*""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*)",
            RegexOptions.Compiled);

        private static readonly Regex EscapedJsonCookieProperty = new(
            @"(?<name>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""\s*:\s*)(?<value>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""|-?\d[\d.eE+-]*)",
            RegexOptions.Compiled);

        private static readonly Regex JsonString = new(@"""(?<text>(?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

        // A password or token field of a multipart/form-data body: the part's value, up to the next boundary line;
        // a part with a filename is a file and stays. The name may come without quotes, as .NET's MultipartFormDataContent writes it
        private static readonly Regex MultipartCredentialPart = new(
            @"(?<name>^Content-Disposition:[ \t]*form-data[ \t]*;[ \t]*name=(?<quote>""?)(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")\k<quote>[ \t]*;?[ \t]*\r?\n(?:[^\r\n]+\r?\n)*\r?\n)"
                + @"(?<value>(?:(?!\r?\n--)[\s\S])+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // A credential a service carries as a URL path segment: a Telegram bot token (/bot123456:AAE.../getUpdates,
        // also /file/bot.../), the secret of a Slack or Discord webhook URL; host and endpoint stay readable.
        // Matched by the path alone, since a request line (GET /bot.../getMe HTTP/1.1) has no host in front of it.
        // The slash may arrive escaped as \/ when a JSON body quotes the URL, or as %2F (%252F) when the URL
        // sits URL-encoded inside another query value
        private const string PathSlash = @"(?:\\?/|%(?:25)*2F)";

        private static readonly Regex UrlPathCredential = new(
            @"(?<name>" + PathSlash + @"bot)(?<value>\d+(?::|%(?:25)*3A)[A-Za-z0-9_-]{20,})"
                + @"|(?<name>" + PathSlash + "services" + PathSlash + "(?-i:T[A-Z0-9]+)" + PathSlash + "(?-i:B[A-Z0-9]+)" + PathSlash + @")(?<value>[A-Za-z0-9]+)"
                + @"|(?<name>" + PathSlash + "api" + PathSlash + @"(?:v\d+" + PathSlash + ")?webhooks" + PathSlash + @"\d{17,20}" + PathSlash + @")(?<value>[A-Za-z0-9_-]+)",
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
            redacted = NestedUrlEncodedCredentialField.Replace(redacted, m =>
                m.Groups["name"].Value + Marker);
            redacted = UrlPathCredential.Replace(redacted, m =>
                m.Groups["name"].Value + Marker);
            redacted = JsonCredentialField.Replace(redacted, m =>
                m.Groups["name"].Value + (m.Groups["value"].Value == "\"\"" ? "\"\"" : "\"" + Marker + "\""));
            redacted = EscapedJsonCredentialField.Replace(redacted, m =>
                m.Groups["name"].Value + (m.Groups["value"].Value == "\\\"\\\"" ? "\\\"\\\"" : "\\\"" + Marker + "\\\""));
            redacted = JsonCookieField.Replace(redacted, m =>
                m.Groups["name"].Value + JsonString.Replace(m.Groups["value"].Value, s =>
                    "\"" + RedactHeaderValue(m.Groups["header"].Value, s.Groups["text"].Value) + "\""));
            redacted = EscapedJsonCookieField.Replace(redacted, m =>
                m.Groups["name"].Value + "\\\"" + RedactHeaderValue(m.Groups["header"].Value, m.Groups["value"].Value[2..^2]) + "\\\"");
            redacted = JsonCookieObject.Replace(redacted, m =>
                m.Groups["name"].Value + JsonCookieProperty.Replace(m.Groups["value"].Value, p =>
                    p.Groups["name"].Value + (p.Groups["value"].Value == "\"\"" ? "\"\"" : "\"" + Marker + "\"")));
            redacted = EscapedJsonCookieObject.Replace(redacted, m =>
                m.Groups["name"].Value + EscapedJsonCookieProperty.Replace(m.Groups["value"].Value, p =>
                    p.Groups["name"].Value + (p.Groups["value"].Value == "\\\"\\\"" ? "\\\"\\\"" : "\\\"" + Marker + "\\\"")));
            redacted = JsonCookieList.Replace(redacted, m =>
                m.Groups["name"].Value + JsonCookieListValue.Replace(m.Groups["value"].Value, p =>
                    p.Groups["name"].Value + (p.Groups["value"].Value == "\"\"" ? "\"\"" : "\"" + Marker + "\"")));
            redacted = EscapedJsonCookieList.Replace(redacted, m =>
                m.Groups["name"].Value + EscapedJsonCookieListValue.Replace(m.Groups["value"].Value, p =>
                    p.Groups["name"].Value + (p.Groups["value"].Value == "\\\"\\\"" ? "\\\"\\\"" : "\\\"" + Marker + "\\\"")));
            redacted = MultipartCredentialPart.Replace(redacted, m =>
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
