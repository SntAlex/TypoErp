namespace TypoErp.Web.Workspace;

/// <summary>
///     Данные уведомления о принудительном перехвате блокировки раздела «Товары» администратором.
/// </summary>
public sealed record WorkspaceTakeoverAlert(
    string TakenOverByDisplayName,
    DateTimeOffset TakenOverAtUtc,
    string? Reason
);
