namespace Identity.Application.Models;

/// <summary>
/// user-service 所拥有 Users 表中、认证所需的最小列（ADR-0020）。
/// 这是**只读投影**：identity-service 不拥有 Users、不写 Users，
/// 也不把它映射成 EF 实体（避免误建表或与 user-service 的模型漂移）。
/// </summary>
public sealed record UserCredential(
    int Id,
    string Email,
    string? PasswordHash,
    bool IsActive,
    int CredentialVersion);
