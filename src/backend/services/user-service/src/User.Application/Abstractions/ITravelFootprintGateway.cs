using Shared.Contracts;

namespace User.Application.Abstractions;

/// <summary>
/// 读取"某用户足迹概览"的出站端口（driven port，ADR-0025）：适配器在基础设施层。
///
/// 刻意**没有**单独的 <c>IUserFootprintService</c> 包一层：这个用例的实体就是"调另一个服务"本身，
/// 再套一个只做转发的类只会增加跳转，不会增加含义。
///
/// 注意方法签名里**没有 userId**：归属由委托令牌的 <c>sub</c> 表达（ADR-0024），
/// 调用方在应用层根本拿不到"指定要看谁的数据"这个能力 —— 参数表本身就是一条安全约束。
/// </summary>
public interface ITravelFootprintGateway
{
    /// <summary>
    /// 以 <paramref name="subjectToken"/> 代表的用户身份，读取其足迹概览。
    ///
    /// 失败语义（调用方据此决定 HTTP 状态码）：
    /// <list type="bullet">
    /// <item><see cref="Domain.Common.FootprintUnavailableException"/> — 下游暂时不可用 → 503；</item>
    /// <item>其余异常（如 <see cref="InvalidOperationException"/>）— 配置/契约破裂 → 500，必须显式暴露。</item>
    /// </list>
    /// </summary>
    Task<FootprintOverview> GetAsync(string subjectToken, CancellationToken cancellationToken = default);
}
