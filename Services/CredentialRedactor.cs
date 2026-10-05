using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

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
        // and "password=value" at the start of a form-encoded body or of a JSON string that echoes one.
        // ASP.NET Core's System.Text.Json writes the & of such a string as \u0026 and a + (a space of the form value) as \u002B;
        // a field after \u0026 counts like one after &, and an escaped character the value may hold does not end it,
        // also when such an echo is quoted once more (\\u0026, \\u002B)
        private static readonly Regex UrlEncodedCredentialField = new(
            @"(?<=^|[?&#""]|\\u0026)(?<name>(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")=)"
                + @"(?<value>(?:[^&#\s""'<>\\]|\\+u(?!0026|0023|0022|0027|003C|003E)[0-9A-F]{4})+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same URL-encoded inside another query value, as a redirect target carries it:
        // ?url=http%3A%2F%2Fexample.com%2Fcb%3Faccess_token%3Dvalue, also next= or return_to= encoded twice (%253F, %253D).
        // The value ends at the outer & or at the next encoded & or #; an encoded user%5Bpassword%5D counts as password-like.
        // The name may also open the outer value itself, as ?state=access_token%3Dvalue carries it,
        // also after the \u0026 System.Text.Json writes for the & of a URL it lists
        // and with an escaped character it writes inside the value (\u002B for a +) not ending it
        private static readonly Regex NestedUrlEncodedCredentialField = new(
            @"(?<name>(?:%(?:25)*(?:3F|26|23)|(?<=(?:[?&]|\\u0026)[^=&#\s""'<>\\]*=))(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + NestedPasswordLikeField + @")%(?:25)*3D)"
                + @"(?<value>(?:(?!%(?:25)*(?:26|23))(?:[^&#\s""'<>\\]|\\+u(?!0026|0023|0022|0027|003C|003E)[0-9A-F]{4}))+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The credential fields of a JSON body; "authorization" is left to JsonHeaderValues, which keeps its scheme word
        private static readonly string[] JsonCredentialFields =
            CredentialFields.Where(f => f != "authorization").Concat(ApiKeyHeaders).ToArray();

        // "password": "value" in a JSON body or WebSocket message, also an API-key header a server echoes
        // back as JSON; an object or array under such a name stays
        private static readonly Regex JsonCredentialField = new(
            @"(?<name>""(?:" + string.Join("|", JsonCredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"password\": \"value\", as an echo service or a logged payload quotes it;
        // a quote inside the value arrives as \\\" and must not end it; a pretty-printed body keeps its line breaks
        // escaped, so \"password\":\n  \"value\" puts the value on a line of its own, and \"password\"\n  : the colon
        private static readonly Regex EscapedJsonCredentialField = new(
            @"(?<name>\\""(?:" + string.Join("|", JsonCredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // One value of a header an echo service lists as an array; a null or a number among the values
        // must not leave the strings beside it readable
        private const string JsonArrayValue = @"(?:""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*|null)";

        // A Cookie or Set-Cookie header a server echoes back as JSON ("Cookie": "session=value", also a list of
        // Set-Cookie strings, and "HTTP_COOKIE" as a CGI or PHP server lists it); each cookie keeps its name like in the header itself
        private static readonly Regex JsonCookieField = new(
            @"(?<name>""(?:http_)?(?<header>cookie|set-cookie)""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|\[\s*" + JsonArrayValue + @"(?:\s*,\s*" + JsonArrayValue + @")*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A string inside a JSON string, \"value\"; a quote inside it arrives as \\\" and must not end it
        private const string EscapedJsonStringValue = @"\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""";

        private const string EscapedJsonArrayValue = @"(?:" + EscapedJsonStringValue + @"|-?\d[\d.eE+-]*|null)";

        // The same inside a JSON string, \"Cookie\": \"session=value\", also \"cookie\": [\"session=value\"], also pretty-printed
        private static readonly Regex EscapedJsonCookieField = new(
            @"(?<name>\\""(?:http_)?(?<header>cookie|set-cookie)\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>" + EscapedJsonStringValue
                + @"|\[" + EscapedJsonSpace + EscapedJsonArrayValue + @"(?:" + EscapedJsonSpace + "," + EscapedJsonSpace + EscapedJsonArrayValue + @")*" + EscapedJsonSpace + @"\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // An Authorization or API-key header a server echoes back as JSON, "Authorization": "Bearer value", also with an
        // array of values as an echo service such as webhook.site lists it, "authorization": ["Bearer value"], "x-api-key": ["value"];
        // each value goes like in the header itself, so the scheme word of an Authorization or Proxy-Authorization value stays;
        // also "HTTP_AUTHORIZATION" and "HTTP_PROXY_AUTHORIZATION" as a CGI, WSGI or PHP server lists the request environment
        private static readonly Regex JsonHeaderValues = new(
            @"(?<name>""(?:http_)?(?<header>authorization|proxy[-_]authorization|" + string.Join("|", ApiKeyHeaders.Select(Regex.Escape)) + @")""\s*:\s*)"
                + @"(?<value>""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*|\[\s*" + JsonArrayValue + @"(?:\s*,\s*" + JsonArrayValue + @")*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"Authorization\": \"Bearer value\", \"authorization\": [\"Bearer value\"], \"HTTP_AUTHORIZATION\": \"Bearer value\", also pretty-printed
        private static readonly Regex EscapedJsonHeaderValues = new(
            @"(?<name>\\""(?:http_)?(?<header>authorization|proxy[-_]authorization|" + string.Join("|", ApiKeyHeaders.Select(Regex.Escape)) + @")\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")"
                + @"(?<value>" + EscapedJsonStringValue + @"|-?\d[\d.eE+-]*|\[" + EscapedJsonSpace + EscapedJsonArrayValue + @"(?:" + EscapedJsonSpace + "," + EscapedJsonSpace + EscapedJsonArrayValue + @")*" + EscapedJsonSpace + @"\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The cookies an echo service lists already parsed, one property per cookie ("cookies": {"session": "value"}),
        // also Express's "signedCookies" and PHP's "_COOKIE" (a debug page may name it "$_COOKIE"); the cookie names stay;
        // a cookie that holds an object of its own (Express's j: cookies) loses its values too
        private const string JsonCookieObjectName = @"(?:cookies?|signedCookies|\$?_COOKIE)";

        private const string JsonObjectMember = @"[^{}""]|""(?:[^""\\]|\\.)*""";

        private static readonly Regex JsonCookieObject = new(
            @"(?<name>""" + JsonCookieObjectName + @"""\s*:\s*)(?<value>\{(?:" + JsonObjectMember + @"|\{(?:" + JsonObjectMember + @")*\})*\})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"cookies\": {\"session\": \"value\"}, also with a line break before the "{"
        private static readonly Regex EscapedJsonCookieObject = new(
            @"(?<name>\\""" + JsonCookieObjectName + @"\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\{(?:[^{}\\]|\\.|\{(?:[^{}\\]|\\.)*\})*\})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The cookies a HAR-style echo service (mockbin and similar) lists as name/value objects,
        // "cookies": [{"name": "session", "value": "value", "path": "/"}]; only each "value" goes. An entry may hold
        // objects of its own at any depth, as the partitionKey of a cookie Chrome DevTools or Puppeteer lists;
        // the braces are counted so one deep entry does not leave every other entry of the list readable
        private const string JsonCookieListEntry = @"\{(?>" + JsonObjectMember + @"|(?<open>\{)|(?<-open>\}))*(?(open)(?!))\}";

        // A null in such a list counts as an entry, so it does not leave every other entry readable
        private const string JsonListEntry = @"(?:" + JsonCookieListEntry + @"|null)";

        private static readonly Regex JsonCookieList = new(
            @"(?<name>""cookies?""\s*:\s*)(?<value>\[\s*" + JsonListEntry + @"(?:\s*,\s*" + JsonListEntry + @")*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"cookies\": [{\"name\": \"session\", \"value\": \"value\"}];
        // each string is taken whole, so a brace inside a value is not counted. A pretty-printed body arrives there
        // with its line breaks escaped between the entries, [\n    {\"name\": ...},\n    {...}\n], also before the "["
        private const string EscapedJsonCookieListEntry = @"\{(?>" + EscapedJsonStringValue + @"|[^{}\\]|\\.|(?<open>\{)|(?<-open>\}))*(?(open)(?!))\}";

        private const string EscapedJsonListEntry = @"(?:" + EscapedJsonCookieListEntry + @"|null)";

        private static readonly Regex EscapedJsonCookieList = new(
            @"(?<name>\\""cookies?\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\[" + EscapedJsonSpace + EscapedJsonListEntry
                + @"(?:" + EscapedJsonSpace + "," + EscapedJsonSpace + EscapedJsonListEntry + @")*" + EscapedJsonSpace + @"\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The "value" property of one entry of such a list
        private static readonly Regex JsonCookieListValue = new(
            @"(?<name>""value""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, also with a line break before the value
        private static readonly Regex EscapedJsonCookieListValue = new(
            @"(?<name>\\""value\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""|-?\d[\d.eE+-]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The headers a HAR-style echo service lists the same way, "headers": [{"name": "cookie", "value": "session=value"},
        // {"name": "authorization", "value": "Bearer value"}]; a credential header's "value" goes like in the header itself.
        // A browser's webRequest lists them as "requestHeaders" / "responseHeaders", a Postman collection as
        // "header": [{"key": "Authorization", "value": "Bearer value"}], a Postman v1 collection as "headerData": [...]
        private const string JsonHeaderListKey = @"(?:(?:request|response)?headers?|headerData)";

        private static readonly Regex JsonHeaderList = new(
            @"(?<name>""" + JsonHeaderListKey + @"""\s*:\s*)(?<value>\[\s*" + JsonListEntry + @"(?:\s*,\s*" + JsonListEntry + @")*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"headers\": [{\"name\": \"cookie\", \"value\": \"session=value\"}], also pretty-printed
        private static readonly Regex EscapedJsonHeaderList = new(
            @"(?<name>\\""" + JsonHeaderListKey + @"\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\[" + EscapedJsonSpace + EscapedJsonListEntry
                + @"(?:" + EscapedJsonSpace + "," + EscapedJsonSpace + EscapedJsonListEntry + @")*" + EscapedJsonSpace + @"\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // One entry of such a list and its "name" (Postman: "key") property, when that names a credential header
        private static readonly Regex JsonHeaderListEntry = new(JsonCookieListEntry, RegexOptions.Compiled);

        private static readonly Regex EscapedJsonHeaderListEntry = new(EscapedJsonCookieListEntry, RegexOptions.Compiled);

        private static readonly Regex JsonHeaderListName = new(
            @"""(?:name|key)""\s*:\s*""(?<header>authorization|proxy-authorization|cookie|set-cookie|"
                + string.Join("|", ApiKeyHeaders.Select(Regex.Escape)) + @")""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EscapedJsonHeaderListName = new(
            @"\\""(?:name|key)\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @"\\""(?<header>authorization|proxy-authorization|cookie|set-cookie|"
                + string.Join("|", ApiKeyHeaders.Select(Regex.Escape)) + @")\\""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The headers whose value is a credential, for the shapes below
        private static readonly string CredentialHeaderNames =
            "authorization|proxy-authorization|cookie|set-cookie|" + string.Join("|", ApiKeyHeaders.Select(Regex.Escape));

        private static readonly Regex CredentialHeaderName = new(@"^(?:" + CredentialHeaderNames + @")$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Node's rawHeaders: one flat list alternating name and value, "rawHeaders": ["Authorization", "Bearer value", "Cookie", "sid=value"],
        // also rawTrailers; a value goes like in the header itself when the name before it is a credential header.
        // Only under these names, since a plain list of header names ("headers": ["Authorization", "Content-Type"]) is no such list
        private const string JsonRawHeaderListKey = @"raw_?(?:headers|trailers)";

        private static readonly Regex JsonRawHeaderList = new(
            @"(?<name>""" + JsonRawHeaderListKey + @"""\s*:\s*)(?<value>\[\s*" + JsonArrayValue + @"(?:\s*,\s*" + JsonArrayValue + @")*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"rawHeaders\": [\"Authorization\", \"Bearer value\"], also pretty-printed
        private static readonly Regex EscapedJsonRawHeaderList = new(
            @"(?<name>\\""" + JsonRawHeaderListKey + @"\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\[" + EscapedJsonSpace + EscapedJsonArrayValue
                + @"(?:" + EscapedJsonSpace + "," + EscapedJsonSpace + EscapedJsonArrayValue + @")*" + EscapedJsonSpace + @"\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // One item of such a list; a null or a number counts as an item, so it does not shift the names and values after it
        private static readonly Regex JsonArrayItem = new(@"""(?<text>(?:[^""\\]|\\.)*)""|-?\d[\d.eE+-]*|null", RegexOptions.Compiled);

        private static readonly Regex EscapedJsonArrayItem = new(@"\\""(?<text>(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*)\\""|-?\d[\d.eE+-]*|null", RegexOptions.Compiled);

        // ASGI and Starlette list the headers as name/value pairs, "headers": [["authorization", "Bearer value"], ["cookie", "sid=value"]];
        // the value of a pair named after a credential header goes like in the header itself. Such a pair counts under any
        // property name (scope.headers, raw_headers) but only as an entry of a list of pairs, so a plain list of
        // header names ("allowHeaders": ["Authorization", "Content-Type"]) stays
        private static readonly Regex JsonHeaderPair = new(
            @"(?<=\[\s*|\]\s*,\s*)(?<name>\[\s*""(?<header>" + CredentialHeaderNames + @")""\s*,\s*)""(?<value>(?:[^""\\]|\\.)*)""(?<end>\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, [\"authorization\", \"Bearer value\"]
        private static readonly Regex EscapedJsonHeaderPair = new(
            @"(?<=\[" + EscapedJsonSpace + @"|\]" + EscapedJsonSpace + "," + EscapedJsonSpace + @")(?<name>\[" + EscapedJsonSpace + @"\\""(?<header>" + CredentialHeaderNames + @")\\""" + EscapedJsonSpace + "," + EscapedJsonSpace + @")"
                + @"\\""(?<value>(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*)\\""(?<end>" + EscapedJsonSpace + @"\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A header block held in one JSON string, "headers": "Authorization: Bearer value\nAccept: */*", as a Postman v1 collection
        // or a logged request keeps it, also one "Name: value" string of a list; a line starts at the opening quote or after
        // an escaped line break, also indented by escaped tabs ("\n\tAuthorization: ..."), and ends at the next escaped line break or at the closing quote
        private static readonly Regex JsonHeaderTextLine = new(
            @"(?<=(?<!\\)""(?:\\t)*|(?<!\\)\\[rn](?:\\t)*)(?<name>[ \t]*(?:" + CredentialHeaderNames + @")[ \t]*:[ \t]*)(?<value>(?:[^""\\]|\\[^rn])*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"headers\": \"Authorization: Bearer value\\nAccept: */*\"
        private static readonly Regex EscapedJsonHeaderTextLine = new(
            @"(?<=\\""(?:\\\\t)*|\\\\[rn](?:\\\\t)*)(?<name>[ \t]*(?:" + CredentialHeaderNames + @")[ \t]*:[ \t]*)(?<value>(?:\\\\\\.|\\\\[^""\\rn]|\\[^""\\]|[^""\\])*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The auth block of a Postman collection lists its credentials as key/value entries, "bearer": [{"key": "token", "value": "value"}],
        // also basic, apikey, oauth2 and the other auth types; an environment or a collection lists its variables the same way,
        // "values": [{"key": "access_token", "value": "value"}], "variable": [...], and a request its query parameters and form body,
        // "query": [...], "urlencoded": [...], "formdata": [...], in a v1 collection "queryParams": [...] and "data": [...],
        // and its path variables as "pathVariableData": [...].
        // Only the "value" of an entry whose key names a credential goes; the auth type and the keys stay
        private const string PostmanListKey = @"(?:bearer|basic|digest|apikey|oauth1|oauth2|hawk|awsv4|ntlm|akamai|edgegrid|jwt|asap|values|variable|query|urlencoded|formdata|queryParams|data|pathVariableData)";

        private static readonly Regex JsonPostmanList = new(
            @"(?<name>""(?<list>" + PostmanListKey + @")""\s*:\s*)(?<value>\[\s*" + JsonListEntry + @"(?:\s*,\s*" + JsonListEntry + @")*\s*\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The same inside a JSON string, \"bearer\": [{\"key\": \"token\", \"value\": \"value\"}]; a pretty-printed collection
        // arrives there with its line breaks escaped, [\n    {\"key\": ...},\n    {...}\n], also before the "[" and before the colon
        private const string EscapedJsonSpace = @"(?:\s|\\[nrt])*";

        private static readonly Regex EscapedJsonPostmanList = new(
            @"(?<name>\\""(?<list>" + PostmanListKey + @")\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\[" + EscapedJsonSpace + EscapedJsonListEntry
                + @"(?:" + EscapedJsonSpace + "," + EscapedJsonSpace + EscapedJsonListEntry + @")*" + EscapedJsonSpace + @"\])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A Postman v2.0 collection writes the apikey block as one object, "apikey": {"key": "X-Api-Key", "value": "value"};
        // its "value" is the key itself
        private static readonly Regex JsonPostmanApiKeyObject = new(
            @"(?<name>""apikey""\s*:\s*)(?<value>\{(?:" + JsonObjectMember + @")*\})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EscapedJsonPostmanApiKeyObject = new(
            @"(?<name>\\""apikey\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\{(?:[^{}\\]|\\.)*\})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // A key named like a credential: a credential field, anything password-like, or a name ending in token, secret, a
        // kind of key, jwt, bearer, cookie, credentials, passphrase or session id (bearerToken, consumerSecret, secretKey,
        // API_KEY, id_jwt, sessionCookie, session_id; tokenName and tokenType stay). In an apikey block
        // the entry "key" holds the header name and stays, the entry "value" holds the key itself
        private const string PostmanCredentialKey = @"[^""\\]*?(?:token|secret|api[_-]?key|access[_-]?key|secret[_-]?key|private[_-]?key|auth[_-]?key|jwt|bearer|cookie|credentials?|passphrase|session[_-]?id)";

        private static readonly Regex JsonPostmanListKey = new(
            @"""key""\s*:\s*""(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + "|" + PostmanCredentialKey + @"|(?<apikey>value))""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EscapedJsonPostmanListKey = new(
            @"\\""key\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @"\\""(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + "|" + PostmanCredentialKey + @"|(?<apikey>value))\\""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // One "name": "value" property of such an object
        private static readonly Regex JsonCookieProperty = new(
            @"(?<name>""(?:[^""\\]|\\.)*""\s*:\s*)(?<value>""(?:[^""\\]|\\.)*""|-?\d[\d.eE+-]*)",
            RegexOptions.Compiled);

        // The same inside a JSON string, also with a line break before the value
        private static readonly Regex EscapedJsonCookieProperty = new(
            @"(?<name>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""" + EscapedJsonSpace + ":" + EscapedJsonSpace + @")(?<value>\\""(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*\\""|-?\d[\d.eE+-]*)",
            RegexOptions.Compiled);

        // A string or a number of such a header; a number comes back as a string, like a credential field's
        private static readonly Regex JsonString = new(@"""(?<text>(?:[^""\\]|\\.)*)""|(?<text>-?\d[\d.eE+-]*)", RegexOptions.Compiled);

        private static readonly Regex EscapedJsonString = new(@"\\""(?<text>(?:\\\\\\.|\\\\[^""\\]|\\[^""\\]|[^""\\])*)\\""|(?<text>-?\d[\d.eE+-]*)", RegexOptions.Compiled);

        // A password or token field of a multipart/form-data body: the part's value, up to the next boundary line;
        // a part with a filename is a file and stays. The name may come without quotes, as .NET's MultipartFormDataContent writes it
        private static readonly Regex MultipartCredentialPart = new(
            @"(?<name>^Content-Disposition:[ \t]*form-data[ \t]*;[ \t]*name=(?<quote>""?)(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")\k<quote>[ \t]*;?[ \t]*\r?\n(?:[^\r\n]+\r?\n)*\r?\n)"
                + @"(?<value>(?:(?!\r?\n--)[\s\S])+)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // The same inside a JSON string, as a request inspector echoes the raw body, "data": "--boundary\r\nContent-Disposition:
        // form-data; name=\"password\"\r\n\r\nvalue\r\n--boundary--"; the line breaks arrive escaped, and the value
        // ends at the next boundary line or at the end of the string. ASP.NET Core's System.Text.Json writes the quotes as \u0022
        private static readonly Regex EscapedMultipartCredentialPart = new(
            @"(?<name>(?<=(?<!\\)\\n)Content-Disposition:[ \t]*form-data[ \t]*;[ \t]*name=(?<quote>(?:\\""|\\u0022)?)(?:" + string.Join("|", CredentialFields.Select(Regex.Escape)) + "|" + PasswordLikeField + @")\k<quote>[ \t]*;?[ \t]*(?:\\r)?\\n(?:(?:[^""\\]|\\[^rn])+(?:\\r)?\\n)*(?:\\r)?\\n)"
                + @"(?<value>(?:(?!(?:\\r)?\\n--)(?:[^""\\]|\\.))+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        // A JSON string that holds a JSON string of its own, as an echo service quotes a request body that already carried
        // JSON as a string: {"received": "{\"echoOf\": \"{\\\"Authorization\\\": \\\"Bearer value\\\"}\"}"}. The rules above
        // see the body and one quoting level; such a string is unquoted once and redacted on its own, which reaches the
        // next level the same way, and only quoted back when it lost a value. A quote that opens it follows an even run of backslashes;
        // a stray quote before it ('"' in a script) would pair it the wrong way, so the closing quote of a string may open
        // the next one
        private static readonly Regex JsonStringQuotedTwice = new(
            @"(?<=(?:^|[^\\])(?:\\\\)*)""(?<text>(?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);

        // The same for a single-quoted script string, var n = '{\\\"token\\\":\\\"value\\\"}': no double-quoted string stands
        // around it, so the quotes inside follow an odd run of backslashes and the rule above never pairs them. Only a string
        // holding a quote two levels deep, or one ASP.NET Core's encoder wrote as \u0022 (JSON.parse('{\u0022apiKey\u0022: ...}')),
        // is unquoted; an apostrophe in prose pairs text that holds none
        private static readonly Regex ScriptStringQuotedTwice = new(
            @"(?<=(?:^|[^\\])(?:\\\\)*)'(?<text>(?:[^'\\]|\\.)*)'",
            RegexOptions.Compiled);

        private const string QuoteTwoLevelsDeep = @"\\\""";

        // A multipart body two levels deep may hold no quote at all, as .NET writes the part names without quotes
        private const string LineBreakTwoLevelsDeep = @"\\n";

        // ASP.NET Core's System.Text.Json writes a quote inside a string as \u0022, which the rules above do not read as one;
        // a string holding it is unquoted the same way, so a body it echoes ("data": "{\u0022password\u0022: ...}") is read as plain JSON
        private const string QuoteEscapedAsUnicode = @"\u0022";

        // Such a string waits behind a placeholder while the other rules run, since they would read its second level
        // as the first: a header text there would lose every line after the Authorization line. It carries a key of its own
        // call, so a body string of the same shape stays as it is. One that shares its opening quote with the string before
        // goes without it
        private static readonly Regex QuotedTwicePlaceholder = new("\"?\u0001(?<key>[0-9a-f]{32}):(?<index>\\d{1,9})\u0001\"", RegexOptions.Compiled);

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

            var quotedTwice = new List<string>();
            string placeholderKey = Guid.NewGuid().ToString("N");
            string redacted = ReplaceQuotedTwice(text, JsonStringQuotedTwice, '"', quotedTwice, placeholderKey);
            redacted = ReplaceQuotedTwice(redacted, ScriptStringQuotedTwice, '\'', quotedTwice, placeholderKey);
            redacted = CredentialHeaderLine.Replace(redacted, m =>
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
                m.Groups["name"].Value + EscapedJsonString.Replace(m.Groups["value"].Value, s =>
                    "\\\"" + RedactHeaderValue(m.Groups["header"].Value, s.Groups["text"].Value) + "\\\""));
            redacted = JsonHeaderValues.Replace(redacted, m =>
                m.Groups["name"].Value + JsonString.Replace(m.Groups["value"].Value, s =>
                    "\"" + RedactHeaderValue(m.Groups["header"].Value, s.Groups["text"].Value) + "\""));
            redacted = EscapedJsonHeaderValues.Replace(redacted, m =>
                m.Groups["name"].Value + EscapedJsonString.Replace(m.Groups["value"].Value, s =>
                    "\\\"" + RedactHeaderValue(m.Groups["header"].Value, s.Groups["text"].Value) + "\\\""));
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
            redacted = JsonHeaderList.Replace(redacted, m =>
                m.Groups["name"].Value + JsonHeaderListEntry.Replace(m.Groups["value"].Value, e =>
                {
                    Match header = JsonHeaderListName.Match(e.Value);
                    if (!header.Success) return e.Value;
                    return JsonCookieListValue.Replace(e.Value, p =>
                    {
                        string value = p.Groups["value"].Value;
                        return p.Groups["name"].Value + "\"" + RedactHeaderValue(header.Groups["header"].Value, value.StartsWith("\"") ? value[1..^1] : value) + "\"";
                    });
                }));
            redacted = EscapedJsonHeaderList.Replace(redacted, m =>
                m.Groups["name"].Value + EscapedJsonHeaderListEntry.Replace(m.Groups["value"].Value, e =>
                {
                    Match header = EscapedJsonHeaderListName.Match(e.Value);
                    if (!header.Success) return e.Value;
                    return EscapedJsonCookieListValue.Replace(e.Value, p =>
                    {
                        string value = p.Groups["value"].Value;
                        return p.Groups["name"].Value + "\\\"" + RedactHeaderValue(header.Groups["header"].Value, value.StartsWith("\\\"") ? value[2..^2] : value) + "\\\"";
                    });
                }));
            redacted = JsonRawHeaderList.Replace(redacted, m =>
                m.Groups["name"].Value + RedactRawHeaderItems(JsonArrayItem, m.Groups["value"].Value, "\""));
            redacted = EscapedJsonRawHeaderList.Replace(redacted, m =>
                m.Groups["name"].Value + RedactRawHeaderItems(EscapedJsonArrayItem, m.Groups["value"].Value, "\\\""));
            redacted = JsonHeaderPair.Replace(redacted, m =>
                m.Groups["name"].Value + "\"" + RedactHeaderValue(m.Groups["header"].Value, m.Groups["value"].Value) + "\"" + m.Groups["end"].Value);
            redacted = EscapedJsonHeaderPair.Replace(redacted, m =>
                m.Groups["name"].Value + "\\\"" + RedactHeaderValue(m.Groups["header"].Value, m.Groups["value"].Value) + "\\\"" + m.Groups["end"].Value);
            redacted = JsonHeaderTextLine.Replace(redacted, m =>
                m.Groups["name"].Value + RedactHeaderValue(m.Groups["name"].Value.Trim().TrimEnd(':').Trim(), m.Groups["value"].Value));
            redacted = EscapedJsonHeaderTextLine.Replace(redacted, m =>
                m.Groups["name"].Value + RedactHeaderValue(m.Groups["name"].Value.Trim().TrimEnd(':').Trim(), m.Groups["value"].Value));
            redacted = JsonPostmanList.Replace(redacted, m =>
                m.Groups["name"].Value + JsonHeaderListEntry.Replace(m.Groups["value"].Value, e =>
                {
                    Match key = JsonPostmanListKey.Match(e.Value);
                    if (!key.Success || (key.Groups["apikey"].Success && !m.Groups["list"].Value.Equals("apikey", StringComparison.OrdinalIgnoreCase))) return e.Value;
                    return JsonCookieListValue.Replace(e.Value, p =>
                        p.Groups["name"].Value + (p.Groups["value"].Value == "\"\"" ? "\"\"" : "\"" + Marker + "\""));
                }));
            redacted = EscapedJsonPostmanList.Replace(redacted, m =>
                m.Groups["name"].Value + EscapedJsonHeaderListEntry.Replace(m.Groups["value"].Value, e =>
                {
                    Match key = EscapedJsonPostmanListKey.Match(e.Value);
                    if (!key.Success || (key.Groups["apikey"].Success && !m.Groups["list"].Value.Equals("apikey", StringComparison.OrdinalIgnoreCase))) return e.Value;
                    return EscapedJsonCookieListValue.Replace(e.Value, p =>
                        p.Groups["name"].Value + (p.Groups["value"].Value == "\\\"\\\"" ? "\\\"\\\"" : "\\\"" + Marker + "\\\""));
                }));
            redacted = JsonPostmanApiKeyObject.Replace(redacted, m =>
                m.Groups["name"].Value + JsonCookieListValue.Replace(m.Groups["value"].Value, p =>
                    p.Groups["name"].Value + (p.Groups["value"].Value == "\"\"" ? "\"\"" : "\"" + Marker + "\"")));
            redacted = EscapedJsonPostmanApiKeyObject.Replace(redacted, m =>
                m.Groups["name"].Value + EscapedJsonCookieListValue.Replace(m.Groups["value"].Value, p =>
                    p.Groups["name"].Value + (p.Groups["value"].Value == "\\\"\\\"" ? "\\\"\\\"" : "\\\"" + Marker + "\\\"")));
            redacted = MultipartCredentialPart.Replace(redacted, m =>
                m.Groups["name"].Value + Marker);
            redacted = EscapedMultipartCredentialPart.Replace(redacted, m =>
                m.Groups["name"].Value + Marker);
            redacted = QuotedTwicePlaceholder.Replace(redacted, m =>
            {
                int index = int.Parse(m.Groups["index"].Value);
                if (m.Groups["key"].Value != placeholderKey || index >= quotedTwice.Count) return m.Value;
                return m.Value[0] == '"' ? quotedTwice[index] : quotedTwice[index][1..];
            });

            if (redacted != text) RedactedAny = true;
            return redacted;
        }

        public string? RedactOrNull(string? text) => text == null ? null : Redact(text);

        // Puts each string quoted twice that lost a value behind a placeholder. Strings are paired from the start, but the
        // scan goes on from the closing quote of each one, so a stray quote before a string does not hide it, not even when
        // the text it wrongly pairs lost a value as well; the placeholder of a string that opens with the closing quote of
        // the one before goes without that quote
        private string ReplaceQuotedTwice(string text, Regex quotedString, char quote, List<string> quotedTwice, string placeholderKey)
        {
            var result = new StringBuilder();
            int copied = 0;
            Match m = quotedString.Match(text);
            while (m.Success)
            {
                int end = m.Index + m.Length;
                string replaced = RedactQuotedTwice(m, quote, quotedTwice, placeholderKey);
                if (replaced != m.Value)
                {
                    if (m.Index < copied) result.Append(replaced, 1, replaced.Length - 1);
                    else result.Append(text, copied, m.Index - copied).Append(replaced);
                    copied = end;
                }
                m = quotedString.Match(text, end - 1);
            }
            return copied == 0 ? text : result.Append(text, copied, text.Length - copied).ToString();
        }

        // Unquotes the string once, redacts what it holds and quotes it back, then hands it on as a placeholder;
        // a string that holds no second quoting level, that is no valid JSON string or that loses nothing stays as it is.
        // A single-quoted script string is read and quoted back with its own quote
        private string RedactQuotedTwice(Match m, char quote, List<string> quotedTwice, string placeholderKey)
        {
            string text = m.Groups["text"].Value;
            if (quote == '\'' ? !text.Contains(QuoteTwoLevelsDeep) && !text.Contains(QuoteEscapedAsUnicode, StringComparison.OrdinalIgnoreCase)
                : !text.Contains(QuoteTwoLevelsDeep) && !text.Contains(LineBreakTwoLevelsDeep) && !text.Contains(QuoteEscapedAsUnicode, StringComparison.OrdinalIgnoreCase)) return m.Value;
            // a raw line break or other control character never stands in a JSON string, only in text a stray quote paired
            // the wrong way, which quoting back would rewrite
            if (text.Any(c => c < ' ')) return m.Value;

            string? unquoted;
            try
            {
                using var reader = new JsonTextReader(new StringReader(quote + text + quote)) { DateParseHandling = DateParseHandling.None };
                unquoted = reader.Read() ? reader.Value as string : null;
            }
            catch (JsonReaderException)
            {
                return m.Value;
            }
            if (unquoted == null) return m.Value;

            string redacted = Redact(unquoted);
            if (redacted == unquoted) return m.Value;
            // Newtonsoft leaves a double quote bare inside a single-quoted string; the script escaped it, since it holds one two levels deep
            // or as \u0022
            string requoted = JsonConvert.ToString(redacted, quote);
            quotedTwice.Add(quote == '\'' ? requoted.Replace("\"", "\\\"") : requoted);
            return "\"\u0001" + placeholderKey + ":" + (quotedTwice.Count - 1) + "\u0001\"";
        }

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

        // Items alternate name and value; a value goes when the name before it is a credential header
        private static string RedactRawHeaderItems(Regex item, string list, string quote)
        {
            int index = 0;
            string? header = null;
            return item.Replace(list, s =>
            {
                if (index++ % 2 == 0)
                {
                    header = s.Groups["text"].Success && CredentialHeaderName.IsMatch(s.Groups["text"].Value) ? s.Groups["text"].Value : null;
                    return s.Value;
                }
                return header == null || !s.Groups["text"].Success ? s.Value : quote + RedactHeaderValue(header, s.Groups["text"].Value) + quote;
            });
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
