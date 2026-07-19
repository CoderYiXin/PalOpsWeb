namespace PalOps.Web.Contracts;

public sealed record UserAccountResponse(
    string Id,
    string UserName,
    string DisplayName,
    string Role,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(string UserName, string DisplayName, string Password, string Role, bool Enabled = true);
public sealed record UpdateUserRequest(string DisplayName, string Role, bool Enabled);
public sealed record ResetUserPasswordRequest(string Password, string Confirmation);
