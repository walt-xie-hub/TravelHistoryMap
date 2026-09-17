namespace Identity.Domain.Common;

/// <summary>
/// 凭据校验失败。停用账号、口令错误、无凭据都抛这一个异常：
/// 三者对外必须不可区分，否则接口会变成账号存在性/状态的探测器。
/// </summary>
public sealed class InvalidCredentialsException()
    : Exception("邮箱或密码错误。");
