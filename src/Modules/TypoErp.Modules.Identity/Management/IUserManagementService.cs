using TypoErp.Modules.Identity.Authentication;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;

namespace TypoErp.Modules.Identity.Management;

/// <summary>
///     Сервис аутентификации, управления учетными записями работников, ролями и делегированием гранулярных разрешений.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    ///     Гарантирует наличие хотя бы одного активного администратора в системе (идемпотентная инициализация при старте).
    /// </summary>
    Task<AppUser> EnsureDefaultAdminExistsAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Проверяет логин и пароль пользователя и возвращает результат аутентификации с набором действующих разрешений.
    /// </summary>
    Task<AuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Возвращает список всех пользователей системы, отсортированный по отображаемому имени.
    /// </summary>
    Task<IReadOnlyList<AppUser>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает пользователя по идентификатору или null, если он не найден.
    /// </summary>
    Task<AppUser?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Создает нового работника или администратора. Требует права <see cref="AppPermission.UsersManage" /> у <paramref name="actorUserId" />.
    /// </summary>
    Task<AppUser> CreateUserAsync(
        Guid actorUserId,
        CreateUserRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Обновляет логин, отображаемое имя и роль пользователя.
    /// </summary>
    Task<AppUser> UpdateUserAsync(
        Guid actorUserId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Делегирует (задает) точный список гранулярных разрешений для указанного работника.
    /// </summary>
    Task<AppUser> SetUserPermissionsAsync(
        Guid actorUserId,
        Guid targetUserId,
        IReadOnlyCollection<AppPermission> permissions,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Активирует или деактивирует (блокирует) учетную запись работника.
    /// </summary>
    Task<AppUser> SetUserActiveStatusAsync(
        Guid actorUserId,
        Guid targetUserId,
        bool isActive,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Принудительно сбрасывает пароль работника (выполняется администратором или пользователем с правом <see cref="AppPermission.UsersManage" />).
    /// </summary>
    Task ResetPasswordAsync(
        Guid actorUserId,
        Guid targetUserId,
        string newPassword,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Позволяет активному пользователю самостоятельно сменить свой пароль после проверки текущего пароля.
    /// </summary>
    Task ChangeOwnPasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Удаляет учетную запись пользователя из базы данных.
    /// </summary>
    Task<bool> DeleteUserAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default
    );
}
