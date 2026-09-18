using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BillingControl.Storage.SharePoint;

/// <summary>
/// Small optional OAuth client-credentials token provider for server-side deployments.
/// </summary>
/// <remarks>
/// ClientSecret is read only from ASP.NET Core configuration at runtime. It is never
/// included in logs or exception messages. Deployments using certificates or managed
/// identity can replace this provider with their own <see cref="IGraphAccessTokenProvider"/>.
/// </remarks>
public sealed class GraphClientCredentialsAccessTokenProvider : IGraphAccessTokenProvider
{
    private readonly HttpClient httpClient;
    private readonly IOptions<SharePointDocumentStorageOptions> options;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private TokenCache? cache;

    public GraphClientCredentialsAccessTokenProvider(
        HttpClient httpClient,
        IOptions<SharePointDocumentStorageOptions> options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var current = Volatile.Read(ref cache);
        if (current is not null && current.ExpiresAt > now.AddMinutes(1))
        {
            return current.AccessToken;
        }

        await refreshGate.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            current = cache;
            if (current is not null && current.ExpiresAt > now.AddMinutes(1))
            {
                return current.AccessToken;
            }

            var configured = options.Value;
            if (string.IsNullOrWhiteSpace(configured.TenantId) ||
                string.IsNullOrWhiteSpace(configured.ClientId) ||
                string.IsNullOrWhiteSpace(configured.ClientSecret))
            {
                throw new DocumentStorageValidationException(
                    "Graph client-credential configuration is incomplete.");
            }

            var tenant = Uri.EscapeDataString(configured.TenantId.Trim());
            var tokenUri = new Uri(
                $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
                UriKind.Absolute);

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = configured.ClientId.Trim(),
                    ["client_secret"] = configured.ClientSecret,
                    ["grant_type"] = "client_credentials",
                    ["scope"] = "https://graph.microsoft.com/.default"
                })
            };

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new DocumentStorageException("Microsoft identity token acquisition failed.");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            TokenResponse? tokenResponse;
            try
            {
                tokenResponse = JsonSerializer.Deserialize<TokenResponse>(payload);
            }
            catch (JsonException exception)
            {
                throw new DocumentStorageValidationException(
                    "Microsoft identity returned a malformed token response.",
                    exception);
            }

            if (tokenResponse is null ||
                string.IsNullOrWhiteSpace(tokenResponse.AccessToken) ||
                tokenResponse.ExpiresIn <= 0)
            {
                throw new DocumentStorageValidationException(
                    "Microsoft identity returned an incomplete token response.");
            }

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
            cache = new TokenCache(tokenResponse.AccessToken, expiresAt);
            return tokenResponse.AccessToken;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private sealed record TokenCache(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
