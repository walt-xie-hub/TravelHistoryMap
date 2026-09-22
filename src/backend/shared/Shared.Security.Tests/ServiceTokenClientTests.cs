using System.Net;
using System.Net.Http.Json;
using Shared.Security;
using Xunit;

namespace Shared.Security.Tests;

public class ServiceTokenClientTests
{
    [Fact]
    public void Validate_WithPlaceholderSecret_RejectsStartup()
    {
        var options = new ServiceIdentityOptions
        {
            Issuer = "http://identity/identity",
            ClientId = "user-service",
            ClientSecret = ServiceIdentityOptions.EnvironmentPlaceholder,
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("占位符", exception.Message);
    }

    [Fact]
    public void Validate_WithRealSecret_Passes()
        => new ServiceIdentityOptions
        {
            Issuer = "http://identity/identity",
            ClientId = "user-service",
            ClientSecret = "a-real-injected-secret",
        }.Validate();

    [Fact]
    public async Task GetAccessTokenAsync_CachesTokenForSameAudienceAndScope()
    {
        var handler = new TokenHandler();
        using var http = new HttpClient(handler);
        using var client = new ServiceTokenClient(
            http,
            Microsoft.Extensions.Options.Options.Create(new ServiceIdentityOptions
            {
                Issuer = "http://identity/identity",
                ClientId = "travel-service",
                ClientSecret = "secret",
            }));

        var first = await client.GetAccessTokenAsync("user-service", "profile:read");
        var second = await client.GetAccessTokenAsync("user-service", "profile:read");

        Assert.Equal("token-1", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("client_credentials", handler.Form["grant_type"]);
        Assert.Equal("travel-service", handler.Form["client_id"]);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RequestsNewTokenForDifferentAudience()
    {
        var handler = new TokenHandler();
        using var client = new ServiceTokenClient(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new ServiceIdentityOptions
            {
                Issuer = "http://identity/identity",
                ClientId = "travel-service",
                ClientSecret = "secret",
            }));

        await client.GetAccessTokenAsync("user-service");
        var result = await client.GetAccessTokenAsync("identity-service");

        Assert.Equal("token-2", result);
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class TokenHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Dictionary<string, string> Form { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                Form[Uri.UnescapeDataString(parts[0])] =
                    Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    access_token = $"token-{RequestCount}",
                    expires_in = 300,
                }),
            };
        }
    }
}