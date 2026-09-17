using Identity.Domain.Entities;

namespace Identity.Application.Abstractions;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);

    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>撤销整族（重用检测、登出、改密/停用后清理）。返回被撤销的条数。</summary>
    Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IServiceClientRepository
{
    Task<ServiceClient?> FindByIdAsync(string clientId, CancellationToken cancellationToken = default);

    Task AddAsync(ServiceClient client, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ILoginAttemptRepository
{
    Task<LoginAttempt?> FindAsync(string key, CancellationToken cancellationToken = default);

    Task AddAsync(LoginAttempt attempt, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
