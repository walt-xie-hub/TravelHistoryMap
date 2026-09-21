using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Shared.Security;

public interface IServiceTokenClient
{
    Task<string> GetAccessTokenAsync(string audience, string? scope = null, CancellationToken ct = default);
}

public static class ServiceTokenClientExtensions
{
    public static IServiceCollection AddTravelMapServiceTokenClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new ServiceIdentityOptions();
        configuration.GetSection(ServiceIdentityOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.Issuer))
            throw new InvalidOperationException("ServiceIdentity:Issuer is required.");
        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new InvalidOperationException("ServiceIdentity:ClientId is required.");
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new InvalidOperationException("ServiceIdentity:ClientSecret is required.");

        services.AddSingleton(Options.Create(options));
        services.AddHttpClient<IServiceTokenClient, ServiceTokenClient>();
        return services;
    }
}

public sealed class ServiceTokenClient(
    HttpClient http,
    IOptions<ServiceIdentityOptions> configured) : IServiceTokenClient, IDisposable
{
    private readonly ServiceIdentityOptions options = configured.Value;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly Dictionary<string, CachedToken> cached = new(StringComparer.Ordinal);

    public async Task<string> GetAccessTokenAsync(
        string audience,
        string? scope = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(audience))
            throw new ArgumentException("Audience is required.", nameof(audience));

        var cacheKey = CreateCacheKey(audience, scope);
        if (cached.TryGetValue(cacheKey, out var current)
            && current.IsUsable(options.RefreshBeforeExpiry))
            return current.AccessToken;

        await refreshLock.WaitAsync(ct);
        try
        {
            if (cached.TryGetValue(cacheKey, out var refreshed)
                && refreshed.IsUsable(options.RefreshBeforeExpiry))
                return refreshed.AccessToken;

            using var response = await http.PostAsync(
                $"{options.Issuer.TrimEnd('/')}/token",
                new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", options.ClientId),
                    new KeyValuePair<string, string>("client_secret", options.ClientSecret),
                    new KeyValuePair<string, string>("audience", audience),
                    new KeyValuePair<string, string>("scope", scope ?? string.Empty),
                ]),
                ct);

            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Identity service returned an empty token response.");
            if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
                throw new InvalidOperationException("Identity service returned an invalid token response.");

            cached[cacheKey] = new CachedToken(token.AccessToken,
                DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn));
            return token.AccessToken;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Dispose() => refreshLock.Dispose();

    private static string CreateCacheKey(string audience, string? scope)
        => audience + "\u001f" + (scope ?? string.Empty);

    private sealed record CachedToken(
        string AccessToken,
        DateTimeOffset ExpiresAt)
    {
        public bool IsUsable(TimeSpan refreshBeforeExpiry)
            => ExpiresAt - DateTimeOffset.UtcNow > refreshBeforeExpiry;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}