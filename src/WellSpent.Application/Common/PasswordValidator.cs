using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Common;

/// <summary>Mirrors internal/service/auth_service.go's validatePassword exactly.</summary>
public static class PasswordValidator
{
    public static void Validate(string password)
    {
        if (password.Length < 8)
        {
            throw new AppValidationException("password must be at least 8 characters");
        }

        bool hasUpper = false, hasLower = false, hasDigit = false, hasSpecial = false;
        foreach (var c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else if (char.IsPunctuation(c) || char.IsSymbol(c)) hasSpecial = true;
        }

        if (!hasUpper || !hasLower || !hasDigit || !hasSpecial)
        {
            throw new AppValidationException("password must contain uppercase, lowercase, digit, and special character");
        }
    }
}
