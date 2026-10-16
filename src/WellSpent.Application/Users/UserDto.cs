namespace WellSpent.Application.Users;

/// <summary>Wire shape for GetMe/UpdateMe/ChangeEmail — mirrors proto `User` field-for-field.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool IsActive,
    bool IsVerified,
    DateTime CreatedAt,
    string? CountryCode,
    string? StateCode,
    int FilingStatus,
    int TaxPaymentFrequency,
    string Language,
    string Currency,
    string Plan,
    bool HasApplePrivateEmail);
