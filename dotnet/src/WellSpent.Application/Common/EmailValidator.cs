using System.Net.Mail;

namespace WellSpent.Application.Common;

/// <summary>Mirrors Go's email handling: lowercase + trim, then net/mail.ParseAddress-equivalent format validation.</summary>
public static class EmailValidator
{
    /// <summary>Normalizes (lowercase, trim) and validates format. Throws <see cref="WellSpent.Domain.Exceptions.AppValidationException"/> on an invalid address.</summary>
    public static string NormalizeAndValidate(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        try
        {
            _ = new MailAddress(normalized);
        }
        catch (FormatException)
        {
            throw new Domain.Exceptions.AppValidationException("invalid email address");
        }
        return normalized;
    }
}
