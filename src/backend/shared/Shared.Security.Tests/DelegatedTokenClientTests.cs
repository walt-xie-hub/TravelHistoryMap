using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Shared.Security;
using Xunit;

namespace Shared.Security.Tests;

/// <summary>
/// 委托令牌**发起侧**（ADR-0024/0025）。这组断言盯的是两件最容易出错的事：
/// ① 表单必须是 RFC 8693 的形状（少了 subject_token_type 会被 identity 判 invalid_request）；
/// ② **不做缓存** —— 缓存键一旦漏了 subject，就会把甲用户的令牌发给乙用户。
/// </summary>
public class DelegatedTokenClientTests
{
    private const string SubjectToken = "user-access-token";

    private static DelegatedTokenClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler), Options.Create(new ServiceIdentityOptions
        {
            Issuer = "http://identity/identity",
            ClientId = "user-service",
            ClientSecret = "secret",
        }));

    [Fact]
    public async Task ExchangeAsync_SendsRfc8693TokenExchangeForm()
    {
        var handler = new RecordingHandler();

        var token = await CreateClient(handler)
            .ExchangeAsync(SubjectToken, "travel-service", "travel:read");

        Assert.Equal("delegated-token", token);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", handler.Form["grant_type"]);
        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", handler.Form["subject_token_type"]);
        Assert.Equal(SubjectToken, handler.Form["subject_token"]);
        Assert.Equal("user-service", handler.Form["client_id"]);
        Assert.Equal("secret", handler.Form["client_secret"]);
        Assert.Equal("travel-service", handler.Form["audience"]);
        Assert.Equal("travel:read", handler.Form["scope"]);
    }

    [Theory]
    [InlineData("", "travel-service", "travel:read")]      // 缺主体令牌
    [InlineData(SubjectToken, "", "travel:read")]           // 缺 audience
    [InlineData(SubjectToken, "travel-service", "   ")]     // 缺 scope
    public async Task ExchangeAsync_MissingRequiredArgument_Throws(string subject, string audience, string scope)
    {
        var client = CreateClient(new RecordingHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => client.ExchangeAsync(subject, audience, scope));
    }

    [Fact]
    public async Task ExchangeAsync_RejectedByIdentity_ThrowsDeniedWithErrorCode()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_scope","error_description":"scope 未被授予。"}""");

        var exception = await Assert.ThrowsAsync<DelegatedTokenDeniedException>(
            () => CreateClient(handler).ExchangeAsync(SubjectToken, "travel-service", "travel:read"));

        // 4xx 是"我们请求得不对"（配置错误），必须能被上层识别并响亮地暴露
        Assert.Equal("invalid_scope", exception.Error);
    }

    [Fact]
    public async Task ExchangeAsync_IdentityFails_ThrowsUnavailable()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "upstream down");

        await Assert.ThrowsAsync<DelegatedTokenUnavailableException>(
            () => CreateClient(handler).ExchangeAsync(SubjectToken, "travel-service", "travel:read"));
    }

    [Fact]
    public async Task ExchangeAsync_TransportFails_ThrowsUnavailable()
    {
        await Assert.ThrowsAsync<DelegatedTokenUnavailableException>(
            () => CreateClient(new ThrowingHandler()).ExchangeAsync(SubjectToken, "travel-service", "travel:read"));
    }

    [Fact]
    public async Task ExchangeAsync_EmptyTokenResponse_ThrowsUnavailable()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"token_type":"Bearer"}""");

        await Assert.ThrowsAsync<DelegatedTokenUnavailableException>(
            () => CreateClient(handler).ExchangeAsync(SubjectToken, "travel-service", "travel:read"));
    }

    /// <summary>
    /// 本轮改动里最重要的一条：同一个 audience/scope 换个主体**必须重新换取**。
    /// 若照搬 IServiceTokenClient 的缓存（键只有 audience+scope），第二次会直接返回
    /// 第一次的令牌 —— 那就是把甲用户的身份发给了乙用户。
    /// </summary>
    [Fact]
    public async Task ExchangeAsync_DifferentSubjects_NeverReuseAToken()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        await client.ExchangeAsync("token-of-user-a", "travel-service", "travel:read");
        await client.ExchangeAsync("token-of-user-b", "travel-service", "travel:read");

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(["token-of-user-a", "token-of-user-b"], handler.SubjectTokens);
    }

    private sealed class RecordingHandler(
        HttpStatusCode status = HttpStatusCode.OK,
        string? body = null) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Dictionary<string, string> Form { get; } = [];

        public List<string> SubjectTokens { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var text = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
                Form[key] = value;
                if (key == "subject_token")
                    SubjectTokens.Add(value);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    body ?? """{"access_token":"delegated-token","issued_token_type":"urn:ietf:params:oauth:token-type:access_token","token_type":"Bearer","expires_in":300}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }
}
