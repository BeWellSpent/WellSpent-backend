package apperr

import "fmt"

type NotFoundError struct {
	Resource string
	ID       string
}

func (e *NotFoundError) Error() string {
	return fmt.Sprintf("%s %q not found", e.Resource, e.ID)
}

type ForbiddenError struct {
	Msg string
}

func (e *ForbiddenError) Error() string {
	return e.Msg
}

type DuplicateError struct {
	Resource string
	Field    string
	Value    string
}

func (e *DuplicateError) Error() string {
	return fmt.Sprintf("%s with %s %q already exists", e.Resource, e.Field, e.Value)
}

type ValidationError struct {
	Msg string
}

func (e *ValidationError) Error() string {
	return e.Msg
}

func NotFound(resource, id string) error {
	return &NotFoundError{Resource: resource, ID: id}
}

func Forbidden(msg string) error {
	return &ForbiddenError{Msg: msg}
}

func Duplicate(resource, field, value string) error {
	return &DuplicateError{Resource: resource, Field: field, Value: value}
}

func Invalid(msg string) error {
	return &ValidationError{Msg: msg}
}
