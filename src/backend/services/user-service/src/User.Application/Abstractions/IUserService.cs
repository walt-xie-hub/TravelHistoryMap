namespace User.Application.Abstractions;

/// <summary>
/// 用户用例接口：注册（凭据写入）与本人档案管理（me 资源）。
///
/// **登录不在这里**：认证与令牌归 identity-service（ADR-0020）。本服务保留
/// <see cref="ChangePasswordAsync"/> 这类凭据**写入**操作，因为 Users 表只有它一个写者。
/// </summary>
public interface IUserService
{
    /// <summary>注册新用户（邮箱占用时抛 <see cref="Domain.Common.EmailAlreadyExistsException"/>）。</summary>
    Task<DTOs.UserDto> RegisterAsync(string name, string email, string password, CancellationToken ct = default);

    /// <summary>按 id 获取档案；不存在返回 null。</summary>
    Task<DTOs.UserDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>更新本人资料（邮箱冲突抛 EmailAlreadyExistsException；用户不存在抛 UserNotFoundException）。</summary>
    Task<DTOs.UserDto> UpdateProfileAsync(int id, DTOs.UpdateProfileDto dto, CancellationToken ct = default);

    /// <summary>
    /// 修改本人密码（当前密码错误抛 <see cref="Domain.Common.InvalidCredentialsException"/>）。
    ///
    /// ip / userAgent 只为审计（事件 <c>password_changed</c>）；改密后 CredentialVersion 自增，
    /// identity-service 会因此拒掉旧 refresh（ADR-0020）。
    /// </summary>
    Task ChangePasswordAsync(int id, string currentPassword, string newPassword, string? ip, string? userAgent, CancellationToken ct = default);
}
