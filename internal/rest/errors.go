package rest

import (
	"errors"
	"net/http"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
)

// errorCode is the stable, machine-readable `code` field of an error response.
// Values are lowercase and match the OpenAPI contract's examples.
type errorCode string

const (
	codeNotFound        errorCode = "not_found"
	codeForbidden       errorCode = "forbidden"
	codeAlreadyExists   errorCode = "already_exists"
	codeInvalidArgument errorCode = "invalid_argument"
	codeUnauthenticated errorCode = "unauthenticated"
	codeInternal        errorCode = "internal"
)

// errorBody is the JSON shape of every non-2xx response.
type errorBody struct {
	Code    string `json:"code"`
	Message string `json:"message"`
}

// statusForError maps a service-layer error to an HTTP status and code.
//
// Deliberately mirrors internal/handler/errors.go's toConnectError one-for-one:
// the same service error must mean the same thing on both transports, or a
// client that migrates one call inherits different failure semantics for free.
// The Connect codes and these statuses are the standard pairing
// (NotFound→404, PermissionDenied→403, AlreadyExists→409, InvalidArgument→400).
func statusForError(err error) (int, errorCode) {
	switch {
	case err == nil:
		return http.StatusOK, ""
	case errors.As(err, new(*apperr.NotFoundError)):
		return http.StatusNotFound, codeNotFound
	case errors.As(err, new(*apperr.ForbiddenError)):
		return http.StatusForbidden, codeForbidden
	case errors.As(err, new(*apperr.DuplicateError)):
		return http.StatusConflict, codeAlreadyExists
	case errors.As(err, new(*apperr.ValidationError)):
		return http.StatusBadRequest, codeInvalidArgument
	default:
		return http.StatusInternalServerError, codeInternal
	}
}
