namespace Travel.Domain.Common;

/// <summary>
/// 写入时引用的用户不存在（user-service 的 users.Id 中无此 id）。
/// 由基础设施层在捕获数据库外键违例（PostgreSQL 23503）后抛出，
/// 表现层据此返回 HTTP 400 —— 不引入跨服务同步调用（见 docs/adr/0002）。
/// </summary>
public sealed class UnknownUserException(int userId) : Exception($"User {userId} does not exist.")
{
    public int UserId { get; } = userId;
}
