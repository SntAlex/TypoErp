using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TypoErp.Modules.Identity.Authentication;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;
using TypoErp.Modules.Identity.Persistence;
using TypoErp.Modules.Identity.Security;
using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Identity.Management;

/// <summary>
///     Реализация сервиса аутентификации, управления работниками и делегирования гранулярных разрешений.
/// </summary>
public sealed class UserManagementService : IUserManagementService
{
    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 64;
    private const int MaxDisplayNameLength = 150;
    private const int MinPasswordLength = 6;

    private readonly IdentityDbContext _dbContext;
    private readonly ILogger<UserManagementService> _logger;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;

    public UserManagementService(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        TimeProvider? timeProvider = null,
        ILogger<UserManagementService>? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(passwordHasher);

        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<UserManagementService>.Instance;
    }

    public async Task<AppUser> EnsureDefaultAdminExistsAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        var existingAdmin = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.Role == UserRole.Administrator && u.IsActive,
            cancellationToken
        );

        if (existingAdmin is not null)
            return existingAdmin;

        var (trimmedUsername, normalizedUsername) = ValidateAndNormalizeUsername(username);
        var trimmedDisplayName = ValidateDisplayName(displayName);
        ValidatePassword(password);

        var now = _timeProvider.GetUtcNow();
        var passwordHash = _passwordHasher.HashPassword(password);

        var existingByUsername = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.NormalizedUsername == normalizedUsername,
            cancellationToken
        );

        if (existingByUsername is not null)
        {
            existingByUsername.UpdateProfile(
                trimmedUsername,
                trimmedDisplayName,
                UserRole.Administrator,
                now
            );
            existingByUsername.SetActive(true, now);
            existingByUsername.ChangePasswordHash(passwordHash, now);
            existingByUsername.SetPermissions(AppPermissions.All, now);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Promoted and activated existing user '{Username}' ({UserId}) as default Administrator",
                existingByUsername.Username,
                existingByUsername.Id
            );

            return existingByUsername;
        }

        var admin = new AppUser(
            trimmedUsername,
            trimmedDisplayName,
            passwordHash,
            UserRole.Administrator,
            now,
            AppPermissions.All
        );

        _dbContext.Users.Add(admin);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created default Administrator account '{Username}' ({UserId})",
            admin.Username,
            admin.Id
        );

        return admin;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default
    )
    {
        var normalizedUsername = StringNormalizer.Normalize(username);
        if (string.IsNullOrEmpty(normalizedUsername) || string.IsNullOrEmpty(password))
            return new AuthenticationResult(AuthenticationStatus.InvalidCredentials, null, []);

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.NormalizedUsername == normalizedUsername,
            cancellationToken
        );

        if (user is null || !_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            _logger.LogWarning(
                "Failed authentication attempt for username '{Username}': invalid credentials",
                normalizedUsername
            );
            return new AuthenticationResult(AuthenticationStatus.InvalidCredentials, null, []);
        }

        if (!user.IsActive)
        {
            _logger.LogWarning(
                "Rejected authentication for deactivated user '{Username}' ({UserId})",
                user.Username,
                user.Id
            );
            return new AuthenticationResult(AuthenticationStatus.UserDeactivated, null, []);
        }

        var now = _timeProvider.GetUtcNow();
        user.RecordLogin(now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var effectivePermissions = user.GetEffectivePermissions();

        _logger.LogInformation(
            "User '{Username}' ('{DisplayName}', {UserId}, Role={Role}) authenticated successfully",
            user.Username,
            user.DisplayName,
            user.Id,
            user.Role
        );

        return new AuthenticationResult(AuthenticationStatus.Succeeded, user, effectivePermissions);
    }

    public async Task<IReadOnlyList<AppUser>> GetAllUsersAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await _dbContext
            .Users.AsNoTracking()
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<AppUser?> GetUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        return await _dbContext
            .Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<AppUser> CreateUserAsync(
        Guid actorUserId,
        CreateUserRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await GetAuthorizedManagerActorAsync(actorUserId, cancellationToken);

        if (request.Role == UserRole.Administrator && actor.Role != UserRole.Administrator)
        {
            _logger.LogWarning(
                "Actor '{ActorUsername}' ({ActorId}) attempted to create an Administrator without Administrator role",
                actor.Username,
                actor.Id
            );
            throw new IdentityManagementException(
                IdentityManagementErrorCode.AccessDenied,
                "Назначать роль «Администратор» может только действующий администратор системы."
            );
        }

        var (trimmedUsername, normalizedUsername) = ValidateAndNormalizeUsername(request.Username);
        var trimmedDisplayName = ValidateDisplayName(request.DisplayName);
        ValidatePassword(request.Password);

        var duplicateExists = await _dbContext.Users.AnyAsync(
            u => u.NormalizedUsername == normalizedUsername,
            cancellationToken
        );

        if (duplicateExists)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.DuplicateUsername,
                $"Пользователь с логином '{trimmedUsername}' уже существует."
            );

        var now = _timeProvider.GetUtcNow();
        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var initialPermissions =
            request.Role == UserRole.Administrator ? AppPermissions.All : request.Permissions ?? [];

        var user = new AppUser(
            trimmedUsername,
            trimmedDisplayName,
            passwordHash,
            request.Role,
            now,
            initialPermissions
        );

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Actor '{ActorUsername}' ({ActorId}) created user '{Username}' ('{DisplayName}', {UserId}, Role={Role}, PermissionsCount={PermissionsCount})",
            actor.Username,
            actor.Id,
            user.Username,
            user.DisplayName,
            user.Id,
            user.Role,
            user.Permissions.Count
        );

        return user;
    }

    public async Task<AppUser> UpdateUserAsync(
        Guid actorUserId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await GetAuthorizedManagerActorAsync(actorUserId, cancellationToken);
        var target = await GetRequiredTargetUserAsync(request.UserId, cancellationToken);

        EnsureActorCanManageTarget(actor, target);

        if (request.Role == UserRole.Administrator && actor.Role != UserRole.Administrator)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.AccessDenied,
                "Назначать роль «Администратор» может только действующий администратор системы."
            );

        var (trimmedUsername, normalizedUsername) = ValidateAndNormalizeUsername(request.Username);
        var trimmedDisplayName = ValidateDisplayName(request.DisplayName);

        var duplicateExists = await _dbContext.Users.AnyAsync(
            u => u.Id != target.Id && u.NormalizedUsername == normalizedUsername,
            cancellationToken
        );

        if (duplicateExists)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.DuplicateUsername,
                $"Пользователь с логином '{trimmedUsername}' уже существует."
            );

        if (
            target.Role == UserRole.Administrator
            && target.IsActive
            && request.Role != UserRole.Administrator
        )
        {
            await EnsureNotLastActiveAdministratorAsync(target.Id, cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        var previousRole = target.Role;
        target.UpdateProfile(trimmedUsername, trimmedDisplayName, request.Role, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Actor '{ActorUsername}' ({ActorId}) updated user '{Username}' ({UserId}): DisplayName='{DisplayName}', Role={PreviousRole}->{NewRole}",
            actor.Username,
            actor.Id,
            target.Username,
            target.Id,
            target.DisplayName,
            previousRole,
            target.Role
        );

        return target;
    }

    public async Task<AppUser> SetUserPermissionsAsync(
        Guid actorUserId,
        Guid targetUserId,
        IReadOnlyCollection<AppPermission> permissions,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var actor = await GetAuthorizedManagerActorAsync(actorUserId, cancellationToken);
        var target = await GetRequiredTargetUserAsync(targetUserId, cancellationToken);

        EnsureActorCanManageTarget(actor, target);

        if (
            actor.Id == target.Id
            && actor.Role != UserRole.Administrator
            && !permissions.Contains(AppPermission.UsersManage)
        )
            throw new IdentityManagementException(
                IdentityManagementErrorCode.CannotRevokeOwnUsersManagePermission,
                "Нельзя снять право управления работниками со своей собственной учетной записи."
            );

        var now = _timeProvider.GetUtcNow();
        target.SetPermissions(permissions, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Actor '{ActorUsername}' ({ActorId}) updated permissions for user '{TargetUsername}' ({TargetId}): [{Permissions}]",
            actor.Username,
            actor.Id,
            target.Username,
            target.Id,
            string.Join(", ", target.Permissions)
        );

        return target;
    }

    public async Task<AppUser> SetUserActiveStatusAsync(
        Guid actorUserId,
        Guid targetUserId,
        bool isActive,
        CancellationToken cancellationToken = default
    )
    {
        var actor = await GetAuthorizedManagerActorAsync(actorUserId, cancellationToken);
        var target = await GetRequiredTargetUserAsync(targetUserId, cancellationToken);

        EnsureActorCanManageTarget(actor, target);

        if (!isActive && actor.Id == target.Id)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.CannotDeactivateSelf,
                "Нельзя деактивировать собственную учетную запись."
            );

        if (!isActive && target.Role == UserRole.Administrator && target.IsActive)
            await EnsureNotLastActiveAdministratorAsync(target.Id, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        target.SetActive(isActive, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Actor '{ActorUsername}' ({ActorId}) changed IsActive={IsActive} for user '{TargetUsername}' ({TargetId})",
            actor.Username,
            actor.Id,
            isActive,
            target.Username,
            target.Id
        );

        return target;
    }

    public async Task ResetPasswordAsync(
        Guid actorUserId,
        Guid targetUserId,
        string newPassword,
        CancellationToken cancellationToken = default
    )
    {
        var actor = await GetAuthorizedManagerActorAsync(actorUserId, cancellationToken);
        var target = await GetRequiredTargetUserAsync(targetUserId, cancellationToken);

        EnsureActorCanManageTarget(actor, target);
        ValidatePassword(newPassword);

        var now = _timeProvider.GetUtcNow();
        var passwordHash = _passwordHasher.HashPassword(newPassword);
        target.ChangePasswordHash(passwordHash, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Actor '{ActorUsername}' ({ActorId}) reset password for user '{TargetUsername}' ({TargetId})",
            actor.Username,
            actor.Id,
            target.Username,
            target.Id
        );
    }

    public async Task ChangeOwnPasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default
    )
    {
        var user = await GetRequiredTargetUserAsync(userId, cancellationToken);
        if (!user.IsActive)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.AccessDenied,
                "Учетная запись деактивирована."
            );

        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
            throw new IdentityManagementException(
                IdentityManagementErrorCode.InvalidCurrentPassword,
                "Текущий пароль указан неверно."
            );

        ValidatePassword(newPassword);

        var now = _timeProvider.GetUtcNow();
        var newHash = _passwordHasher.HashPassword(newPassword);
        user.ChangePasswordHash(newHash, now);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User '{Username}' ({UserId}) changed their own password",
            user.Username,
            user.Id
        );
    }

    public async Task<bool> DeleteUserAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default
    )
    {
        var actor = await GetAuthorizedManagerActorAsync(actorUserId, cancellationToken);
        var target = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.Id == targetUserId,
            cancellationToken
        );

        if (target is null)
            return false;

        EnsureActorCanManageTarget(actor, target);

        if (actor.Id == target.Id)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.CannotDeleteSelf,
                "Нельзя удалить собственную учетную запись."
            );

        if (target.Role == UserRole.Administrator && target.IsActive)
            await EnsureNotLastActiveAdministratorAsync(target.Id, cancellationToken);

        _dbContext.Users.Remove(target);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Actor '{ActorUsername}' ({ActorId}) deleted user '{TargetUsername}' ({TargetId})",
            actor.Username,
            actor.Id,
            target.Username,
            target.Id
        );

        return true;
    }

    private async Task<AppUser> GetAuthorizedManagerActorAsync(
        Guid actorUserId,
        CancellationToken cancellationToken
    )
    {
        var actor = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.Id == actorUserId,
            cancellationToken
        );

        if (actor is null || !actor.IsActive || !actor.HasPermission(AppPermission.UsersManage))
        {
            _logger.LogWarning(
                "Unauthorized user management attempt by ActorId={ActorId}",
                actorUserId
            );
            throw new IdentityManagementException(
                IdentityManagementErrorCode.AccessDenied,
                "Недостаточно прав для управления пользователями и разрешениями."
            );
        }

        return actor;
    }

    private async Task<AppUser> GetRequiredTargetUserAsync(
        Guid targetUserId,
        CancellationToken cancellationToken
    )
    {
        var target = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.Id == targetUserId,
            cancellationToken
        );

        if (target is null)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.UserNotFound,
                $"Пользователь с идентификатором '{targetUserId}' не найден."
            );

        return target;
    }

    private static void EnsureActorCanManageTarget(AppUser actor, AppUser target)
    {
        if (target.Role == UserRole.Administrator && actor.Role != UserRole.Administrator)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.AccessDenied,
                "Управлять учетной записью администратора может только другой администратор."
            );
    }

    private async Task EnsureNotLastActiveAdministratorAsync(
        Guid targetAdminId,
        CancellationToken cancellationToken
    )
    {
        var otherActiveAdminExists = await _dbContext.Users.AnyAsync(
            u => u.Id != targetAdminId && u.Role == UserRole.Administrator && u.IsActive,
            cancellationToken
        );

        if (!otherActiveAdminExists)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.CannotRemoveLastAdministrator,
                "В системе должен оставаться хотя бы один активный администратор."
            );
    }

    private static (string TrimmedUsername, string NormalizedUsername) ValidateAndNormalizeUsername(
        string? username
    )
    {
        var trimmed = username?.Trim() ?? string.Empty;
        var normalized = StringNormalizer.Normalize(trimmed);

        if (
            string.IsNullOrEmpty(normalized)
            || normalized.Length < MinUsernameLength
            || normalized.Length > MaxUsernameLength
            || normalized.Contains(' ')
        )
            throw new IdentityManagementException(
                IdentityManagementErrorCode.InvalidUsername,
                $"Логин должен содержать от {MinUsernameLength} до {MaxUsernameLength} символов и не содержать пробелов."
            );

        return (trimmed, normalized);
    }

    private static string ValidateDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxDisplayNameLength)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.InvalidDisplayName,
                $"Отображаемое имя не может быть пустым и не должно превышать {MaxDisplayNameLength} символов."
            );

        return trimmed;
    }

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            throw new IdentityManagementException(
                IdentityManagementErrorCode.InvalidPassword,
                $"Пароль должен содержать не менее {MinPasswordLength} символов."
            );
    }
}
