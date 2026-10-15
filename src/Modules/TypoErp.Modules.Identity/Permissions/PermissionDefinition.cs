namespace TypoErp.Modules.Identity.Permissions;

/// <summary>
///     Описатель гранулярного разрешения для отображения в интерфейсе управления правами.
/// </summary>
public sealed record PermissionDefinition(
    AppPermission Permission,
    string GroupName,
    string DisplayName,
    string Description
);
