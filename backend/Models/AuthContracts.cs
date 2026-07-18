using System.ComponentModel.DataAnnotations;

namespace VisionPaint.Models;

public sealed record AuthStatusResponse(bool IsAuthenticated, bool CanBootstrap, AuthenticatedUserResponse? User);

public sealed record AuthenticatedUserResponse(
    Guid AuthUserId,
    int PersonId,
    int CompanyId,
    string CompanyRole,
    string PersonName,
    string Email);

public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    AuthenticatedUserResponse User);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record BootstrapRequest(
    [Required] string Name,
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record RefreshTokenRequest(
    [Required] string RefreshToken);

public sealed record ForgotPasswordRequest(
    [Required, EmailAddress] string Email);

public sealed record ResetPasswordRequest(
    [Required] string Token,
    [Required, MinLength(8)] string NewPassword);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(8)] string NewPassword);
