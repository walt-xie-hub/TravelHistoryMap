namespace User.Application.Abstractions;

/// <summary>
/// 用户用例接口：注册/登录（认证域）与本人档案管理（me 资源）。
/// 表现层只依赖此抽象；登录/注册所需 JWT 由 Api 层组合根签发。
/// </summary>
public interface IUserService
{
    /// <summary>注册新用户（邮箱占用时抛 <see cref="Domain.Common.EmailAlreadyExistsException"/>）。</summary>
    Task<DTOs.UserDto> RegisterAsync(string name, string email, string password, CancellationToken ct = default);

    /// <summary>邮箱+密码登录（凭据错误抛 <see cref="Domain.Common.InvalidCredentialsException"/>）。</summary>
    Task<DTOs.UserDto> LoginAsync(string email, string password, CancellationToken ct = default);

    /// <summary>按 id 获取档案；不存在返回 null。</summary>
    Task<DTOs.UserDto?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>更新本人资料（邮箱冲突抛 EmailAlreadyExistsException；用户不存在抛 UserNotFoundException）。</summary>
    Task<DTOs.UserDto> UpdateProfileAsync(int id, DTOs.UpdateProfileDto dto, CancellationToken ct = default);

    /// <summary>修改本人密码（当前密码错误抛 InvalidCredentialsException）。</summary>
    Task ChangePasswordAsync(int id, string currentPassword, string newPassword, CancellationToken ct = default);
}
