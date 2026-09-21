using Identity.Application.Abstractions;
using Identity.Domain.Entities;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Repositories;

public sealed class RefreshTokenRepository(IdentityDbContext db) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
        => await db.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken cancellationToken = default)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
            token.RevokedAt = revokedAt;

        return tokens.Count;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}

public sealed class ServiceClientRepository(IdentityDbContext db) : IServiceClientRepository
{
    public Task<ServiceClient?> FindByIdAsync(string clientId, CancellationToken cancellationToken = default)
        => db.ServiceClients
            .Include(c => c.Scopes)
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

    public async Task AddAsync(ServiceClient client, CancellationToken cancellationToken = default)
        => await db.ServiceClients.AddAsync(client, cancellationToken);

    public void Update(ServiceClient client) => db.ServiceClients.Update(client);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}

public sealed class LoginAttemptRepository(IdentityDbContext db) : ILoginAttemptRepository
{
    public Task<LoginAttempt?> FindAsync(string key, CancellationToken cancellationToken = default)
        => db.LoginAttempts.FirstOrDefaultAsync(a => a.Key == key, cancellationToken);

    public async Task AddAsync(LoginAttempt attempt, CancellationToken cancellationToken = default)
        => await db.LoginAttempts.AddAsync(attempt, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
