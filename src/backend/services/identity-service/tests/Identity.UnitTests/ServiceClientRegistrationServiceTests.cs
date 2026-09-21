using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Services;
using Identity.Domain.Entities;
using Xunit;

namespace Identity.UnitTests;

public class ServiceClientRegistrationServiceTests
{
    [Fact]
    public async Task EnsureRegisteredAsync_CreatesClientWithHashedSecretAndScopes()
    {
        var repository = new RecordingServiceClientRepository();
        var options = new IdentityOptions
        {
            ServiceClients =
            [
                new()
                {
                    ClientId = "travel-service",
                    ClientSecret = "secret-value",
                    DisplayName = "Travel service",
                    Scopes = ["travel:read", "travel:read"],
                },
            ],
        };

        await new ServiceClientRegistrationService(
            repository,
            new RecordingSecretHasher(),
            options).EnsureRegisteredAsync();

        var client = Assert.Single(repository.Added);
        Assert.Equal("hash:secret-value", client.ClientSecretHash);
        Assert.Equal(["travel:read"], client.Scopes.Select(s => s.Scope).ToArray());
    }

    [Fact]
    public async Task EnsureRegisteredAsync_ExistingClientKeepsHashWhenSecretIsUnchanged()
    {
        var repository = new RecordingServiceClientRepository
        {
            Existing = new ServiceClient
            {
                ClientId = "travel-service",
                ClientSecretHash = "hash:secret-value",
                DisplayName = "old",
                IsActive = false,
            },
        };
        var options = new IdentityOptions
        {
            ServiceClients =
            [new() { ClientId = "travel-service", ClientSecret = "secret-value", DisplayName = "new" }],
        };

        await new ServiceClientRegistrationService(
            repository,
            new RecordingSecretHasher(),
            options).EnsureRegisteredAsync();

        Assert.Equal("hash:secret-value", repository.Existing.ClientSecretHash);
        Assert.Equal("new", repository.Existing.DisplayName);
        Assert.True(repository.Existing.IsActive);
    }

    [Fact]
    public async Task EnsureRegisteredAsync_RejectsMissingSecret()
    {
        var options = new IdentityOptions
        {
            ServiceClients = [new() { ClientId = "travel-service" }],
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ServiceClientRegistrationService(
                new RecordingServiceClientRepository(),
                new RecordingSecretHasher(),
                options).EnsureRegisteredAsync());

        Assert.Contains("has no secret", exception.Message);
    }

    private sealed class RecordingSecretHasher : IServiceSecretHasher
    {
        public string Hash(string secret) => "hash:" + secret;

        public bool Verify(string secret, string hash) => hash == Hash(secret);
    }

    private sealed class RecordingServiceClientRepository : IServiceClientRepository
    {
        public ServiceClient? Existing { get; set; }

        public List<ServiceClient> Added { get; } = [];

        public Task<ServiceClient?> FindByIdAsync(string clientId, CancellationToken cancellationToken = default)
            => Task.FromResult(Existing);

        public Task AddAsync(ServiceClient client, CancellationToken cancellationToken = default)
        {
            Added.Add(client);
            return Task.CompletedTask;
        }

        public void Update(ServiceClient client) { }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}