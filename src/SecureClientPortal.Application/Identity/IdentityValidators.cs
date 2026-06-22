using Microsoft.AspNetCore.Http;
using SecureClientPortal.Backend.Application.Common;
using SecureClientPortal.Backend.Application.Contracts;

namespace SecureClientPortal.Backend.Application.Identity;

public static class IdentityValidators
{
    private static readonly HashSet<string> AllowedUserStatuses = ["active", "disabled", "locked", "invited", "reset_pending", "password_reset_required"];

    public static void ValidateCreateUser(CreateUserRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.FullName)) errors.Add("Full name is required.");
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@")) errors.Add("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.Role)) errors.Add("Role is required.");
        ThrowIfAny(errors);
    }

    public static void ValidateUpdateUserActivation(UpdateUserActivationRequest request)
    {
        if (!request.IsActive && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AppValidationException("A reason is required when deactivating a user.");
        }
    }

    public static void ValidateAdminCreateUser(AdminCreateUserRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.FullName)) errors.Add("Full name is required.");
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@")) errors.Add("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.Role)) errors.Add("Role is required.");
        ThrowIfAny(errors);
    }

    public static void ValidateAdminUpdateRole(AdminUpdateRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Role))
        {
            throw new AppValidationException("Role is required.");
        }
    }

    public static void ValidateAdminUpdateStatus(AdminUpdateStatusRequest request)
    {
        var normalized = request.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedUserStatuses.Contains(normalized))
        {
            throw new AppValidationException("Invalid user status.");
        }
    }

    public static void ValidateAdminResetAccess(AdminResetAccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AppValidationException("A reason is required for access reset.");
        }
    }

    public static void ValidateAdminResetPassword(AdminResetPasswordRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.NewPassword) && request.NewPassword.Trim().Length < 8)
        {
            throw new AppValidationException("Temporary password must be at least 8 characters long.");
        }
    }

    public static void ValidateAdminSetting(AdminSettingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ValueJson))
        {
            throw new AppValidationException("Setting value is required.");
        }
    }

    public static void ValidateLogin(LoginRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@")) errors.Add("A valid email address is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) errors.Add("Password is required.");
        ThrowIfAny(errors);
    }

    public static void ValidateCompleteInvite(CompleteInviteRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@")) errors.Add("A valid invite email is required.");
        if (string.IsNullOrWhiteSpace(request.Token)) errors.Add("A setup token is required.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Trim().Length < 8) errors.Add("Password must be at least 8 characters long.");
        ThrowIfAny(errors);
    }

    public static void ValidateForgotPassword(ForgotPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@"))
        {
            throw new AppValidationException("A valid email address is required.");
        }
    }

    public static void ValidateRefresh(RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new AppValidationException("A refresh token is required.");
        }
    }

    public static void ValidateChangePassword(ChangePasswordRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.CurrentPassword)) errors.Add("Your current password is required.");
        if (string.IsNullOrWhiteSpace(request.NextPassword) || request.NextPassword.Trim().Length < 8) errors.Add("Password must be at least 8 characters long.");
        if (!string.IsNullOrWhiteSpace(request.CurrentPassword) && request.CurrentPassword.Trim() == request.NextPassword.Trim()) errors.Add("Choose a new password that is different from the current one.");
        ThrowIfAny(errors);
    }

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors.ToArray());
        }
    }
}
