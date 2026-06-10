package handler

import (
	"errors"

	"connectrpc.com/connect"
	"github.com/mauro-afa91/spendsense/internal/apperr"
)

func toConnectError(err error) error {
	if err == nil {
		return nil
	}
	var notFound *apperr.NotFoundError
	if errors.As(err, &notFound) {
		return connect.NewError(connect.CodeNotFound, err)
	}
	var forbidden *apperr.ForbiddenError
	if errors.As(err, &forbidden) {
		return connect.NewError(connect.CodePermissionDenied, err)
	}
	var duplicate *apperr.DuplicateError
	if errors.As(err, &duplicate) {
		return connect.NewError(connect.CodeAlreadyExists, err)
	}
	var validation *apperr.ValidationError
	if errors.As(err, &validation) {
		return connect.NewError(connect.CodeInvalidArgument, err)
	}
	return connect.NewError(connect.CodeInternal, err)
}
