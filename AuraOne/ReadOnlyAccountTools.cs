// Aura One / My Baby: independent read-only Google & GitHub API adapter.
// No Lovable dependency and no authentication secrets in this source.
// Requires an implementation of IAccountAccessTokenProvider backed by a
// user-consented OAuth flow. Never ship a Google/GitHub client secret in an APK.
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AuraOne.Integrations
{
    public enum AccountProvider { Google, GitHub }

    // Implement with a user-consented, provider-specific Android OAuth flow.
    // The provider must return a user-bound access token, not a developer key.
    public interface IAccountAccessTokenProvider
    {
        Task<string?> GetTokenAsync(AccountProvider provider, CancellationToken cancellationToken);
    }

    public sealed class AccountApiException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public AccountApiException(HttpStatusCode code, string description)
            : base(description) { StatusCode = code; }
    }

    public sealed class ReadOnlyAccountTools
    {
        private readonly HttpClient client;
        private readonly IAccountAccessTokenProvider tokens;
        private const int MaxBodyBytes = 1024 * 1024;
        private static readonly Regex Slug = new Regex("^[A-Za-z0-9_.-]{1,100}$", RegexOptions.Compiled);
        private static readonly Regex GmailId = new Regex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.Compiled);

        public ReadOnlyAccountTools(HttpClient client, IAccountAccessTokenProvider tokens)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        private static string Query(string value) => Uri.EscapeDataString(value);
        private static int Limit(int n, int max) => Math.Min(Math.Max(n, 1), max);

        private async Task<JsonDocument> GetJsonAsync(AccountProvider provider, string url, CancellationToken ct)
        {
            // Disallow arbitrary endpoints: this tool must not become an SSRF proxy.
            var uri = new Uri(url, UriKind.Absolute);
            if (uri.Scheme != "https" ||
                (provider == AccountProvider.Google && uri.Host != "www.googleapis.com") ||
                (provider == AccountProvider.GitHub && uri.Host != "api.github.com"))
                throw new InvalidOperationException("Unapproved API destination.");

            string? token = await tokens.GetTokenAsync(provider, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Account not authorized for this application. Please connect it first.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (provider == AccountProvider.GitHub)
            {
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                request.Headers.UserAgent.ParseAdd("AuraOne-Android/1.0");
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Do not include raw provider response or tokens in an error message.
                string message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Account authorization expired or was revoked.",
                    HttpStatusCode.Forbidden => "The account lacks permission, quota, or API access.",
                    HttpStatusCode.TooManyRequests => "Provider rate limit reached.",
                    _ => $"Provider request failed (HTTP {(int)response.StatusCode})."
                };
                throw new AccountApiException(response.StatusCode, message);
            }
            if (response.Content.Headers.ContentLength is long length && length > MaxBodyBytes)
                throw new InvalidOperationException("Provider response exceeded permitted size.");
            // Limit the maximum JSON body received even when Content-Length is omitted.
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var copy = new System.IO.MemoryStream();
            var buffer = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                if (copy.Length + count > MaxBodyBytes)
                    throw new InvalidOperationException("Provider response exceeded permitted size.");
                copy.Write(buffer, 0, count);
            }
            copy.Position = 0;
            try { return await JsonDocument.ParseAsync(copy, cancellationToken: ct).ConfigureAwait(false); }
            catch (JsonException) { throw new InvalidOperationException("Provider returned invalid account data."); }
        }

        // Google Gmail API. Requires https://www.googleapis.com/auth/gmail.readonly.
        public Task<JsonDocument> GmailSearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => GetJsonAsync(AccountProvider.Google,
                "https://www.googleapis.com/gmail/v1/users/me/messages?q=" + Query(query ?? "") +
                "&maxResults=" + Limit(maxResults, 30), ct);

        public Task<JsonDocument> GmailMessageAsync(string messageId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(messageId) || !GmailId.IsMatch(messageId))
                throw new ArgumentException("Invalid Gmail message identifier.", nameof(messageId));
            return GetJsonAsync(AccountProvider.Google,
                "https://www.googleapis.com/gmail/v1/users/me/messages/" + Query(messageId) + "?format=metadata", ct);
        }

        // Google Calendar API. Requires calendar.readonly or calendar.events.readonly.
        public Task<JsonDocument> CalendarUpcomingAsync(DateTimeOffset from, int maxResults = 10,
            CancellationToken ct = default)
            => GetJsonAsync(AccountProvider.Google,
                "https://www.googleapis.com/calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime" +
                "&timeMin=" + Query(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")) +
                "&maxResults=" + Limit(maxResults, 50), ct);

        // Google Drive API. Metadata only, requires drive.metadata.readonly.
        public Task<JsonDocument> DriveListAsync(string nameContains = "", int pageSize = 20,
            CancellationToken ct = default)
        {
            string q = "trashed = false";
            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                string clean = nameContains.Trim().Replace("\\", "\\\\").Replace("'", "\\'");
                if (clean.Length > 100) clean = clean.Substring(0, 100);
                q += " and name contains '" + clean + "'";
            }
            return GetJsonAsync(AccountProvider.Google,
                "https://www.googleapis.com/drive/v3/files?q=" + Query(q) +
                "&fields=" + Query("nextPageToken,files(id,name,mimeType,modifiedTime,webViewLink)") +
                "&pageSize=" + Limit(pageSize, 50), ct);
        }

        // GitHub API. Requires a user-authorized GitHub token with relevant permissions.
        public Task<JsonDocument> GitHubRepositoriesAsync(int count = 20, CancellationToken ct = default)
            => GetJsonAsync(AccountProvider.GitHub,
                "https://api.github.com/user/repos?sort=updated&per_page=" + Limit(count, 50), ct);

        public Task<JsonDocument> GitHubIssuesAsync(string owner, string repository, int count = 20,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository) ||
                !Slug.IsMatch(owner) || !Slug.IsMatch(repository) || owner is "." or ".." || repository is "." or "..")
                throw new ArgumentException("Invalid repository name.");
            return GetJsonAsync(AccountProvider.GitHub,
                "https://api.github.com/repos/" + Query(owner) + "/" + Query(repository) +
                "/issues?state=open&per_page=" + Limit(count, 50), ct);
        }
    }
}
