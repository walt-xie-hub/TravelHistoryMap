using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contracts;
using Shared.Security;
using User.Domain.Common;
using User.Infrastructure.Clients;
using Xunit;

namespace User.UnitTests;

/// <summary>
/// 委托调用的**失败语义**（ADR-0025）——本功能最容易做错的地方。
///
/// "下游暂时坏了"（→ 503，可重试）与"我们自己配置错了"（→ 500，必须响亮）
/// 不是一回事：混成一个错误码，运维就会去重启服务，而不是去补 scope 注册。
/// 另外，"反序列化失败"绝不能被静默当成"0 条记录"。
/// </summary>
public class TravelFootprintGatewayTests
{
    private static readonly FootprintOverview Sample = new(7, 3, 2, null, null);

    private static DelegatedTravelFootprintGateway CreateGateway(
        HttpMessageHandler handler,
        IDelegatedTokenClient tokens)
        => new(
            tokens,
            new HttpClient(handler),
            Options.Create(new TravelServiceOptions
            {
                Audience = "travel-service",
                BaseUrl = "http://travel-server:8080",
            }));

    private static Mock<IDelegatedTokenClient> TokenClientReturning(string token = "delegated-token")
    {
        var tokens = new Mock<IDelegatedTokenClient>();
        tokens
            .Setup(t => t.ExchangeAsync("subject-token", "travel-service", ServiceScopes.TravelRead, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        return tokens;
    }

    [Fact]
    public async Task GetAsync_CallsInternalEndpointWithDelegatedTokenAndWithoutUserId()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"totalRecords":7,"cityCount":3,"favoriteCount":2,"firstArrivedAt":null,"lastArrivedAt":null}""");
        var tokens = TokenClientReturning();

        var result = await CreateGateway(handler, tokens.Object).GetAsync("subject-token");

        Assert.Equal(Sample, result);
        Assert.Equal("http://travel-server:8080/internal/travels/footprint", handler.LastRequestUri);
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
        Assert.Equal("delegated-token", handler.LastAuthorizationParameter);
        // 归属由令牌的 sub 表达：请求里不该出现任何 userId
        Assert.DoesNotContain("userId", handler.LastRequestUri, StringComparison.OrdinalIgnoreCase);
        tokens.Verify(
            t => t.ExchangeAsync("subject-token", "travel-service", ServiceScopes.TravelRead, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAsync_WhenExchangeUnavailable_ThrowsUnavailable()
    {
        var tokens = new Mock<IDelegatedTokenClient>();
        tokens
            .Setup(t => t.ExchangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DelegatedTokenUnavailableException("identity down"));

        await Assert.ThrowsAsync<FootprintUnavailableException>(
            () => CreateGateway(new RecordingHandler(HttpStatusCode.OK, "{}"), tokens.Object).GetAsync("subject-token"));
    }

    [Fact]
    public async Task GetAsync_WhenExchangeDenied_PropagatesFailureInsteadOfLookingUnavailable()
    {
        var tokens = new Mock<IDelegatedTokenClient>();
        tokens
            .Setup(t => t.ExchangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DelegatedTokenDeniedException("invalid_scope"));

        // scope 没授予 = 部署时必须修好的错 → 不能被翻译成 503
        await Assert.ThrowsAsync<DelegatedTokenDeniedException>(
            () => CreateGateway(new RecordingHandler(HttpStatusCode.OK, "{}"), tokens.Object).GetAsync("subject-token"));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetAsync_WhenTravelServiceFails_ThrowsUnavailable(HttpStatusCode status)
    {
        await Assert.ThrowsAsync<FootprintUnavailableException>(
            () => CreateGateway(new RecordingHandler(status, "boom"), TokenClientReturning().Object).GetAsync("subject-token"));
    }

    [Fact]
    public async Task GetAsync_WhenTravelServiceRejectsToken_ThrowsLoudContractError()
    {
        // 401/403 = 委托令牌不被接受（scope 未授予 / audience 未登记）→ 必须响亮，而不是"暂时不可用"
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateGateway(new RecordingHandler(HttpStatusCode.Forbidden, "nope"), TokenClientReturning().Object).GetAsync("subject-token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    public async Task GetAsync_WhenResponseIsEmpty_ThrowsInsteadOfReportingZeroRecords(string body)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateGateway(new RecordingHandler(HttpStatusCode.OK, body), TokenClientReturning().Object).GetAsync("subject-token"));
    }

    [Fact]
    public async Task GetAsync_WhenTravelServiceUnreachable_ThrowsUnavailable()
    {
        await Assert.ThrowsAsync<FootprintUnavailableException>(
            () => CreateGateway(new ThrowingHandler(), TokenClientReturning().Object).GetAsync("subject-token"));
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastRequestUri { get; private set; }

        public string? LastAuthorizationScheme { get; private set; }

        public string? LastAuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
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
