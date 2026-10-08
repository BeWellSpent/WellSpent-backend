namespace WellSpent.Api.Errors;

// Mirrors internal/apperr's four error kinds. Nothing throws these yet — no
// business domain has been ported — but ErrorHandlingMiddleware's mapping is
// part of this sub-issue's scaffold deliverable, so later domains have
// somewhere to throw into from day one.
//
// AppValidationException (not ValidationException) to avoid colliding with
// System.ComponentModel.DataAnnotations.ValidationException.

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

public sealed class DuplicateException(string message) : Exception(message);

public sealed class AppValidationException(string message) : Exception(message);
