namespace WellSpent.Domain.Exceptions;

// Mirrors internal/apperr's four error kinds exactly, including the mapping
// ErrorHandlingMiddleware applies in WellSpent.Api (not_found/404,
// forbidden/403, already_exists/409, invalid_argument/400). Living in Domain
// (not Api, where B1 first put them) because Application-layer handlers need
// to throw these too, and Domain is the one project every layer can see.
//
// AppValidationException (not ValidationException) avoids colliding with
// System.ComponentModel.DataAnnotations.ValidationException.

public sealed class NotFoundException(string resource, string id)
    : Exception($"{resource} \"{id}\" not found");

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class DuplicateException(string resource, string field, string value)
    : Exception($"{resource} with {field} \"{value}\" already exists");

public sealed class AppValidationException(string message) : Exception(message);

/// <summary>An unknown, revoked, expired, or device-mismatched refresh token — maps to 401.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);

/// <summary>
/// A tracked row was concurrently modified between read and write (EF Core's
/// DbUpdateConcurrencyException against the xmin concurrency token) — maps
/// to 409, distinct from DuplicateException's own 409. Thrown by repository
/// methods instead of letting the EF Core exception type leak past
/// Infrastructure.
/// </summary>
public sealed class ConflictException(string message, Exception? inner = null) : Exception(message, inner);
