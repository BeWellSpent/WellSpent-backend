package handler

import (
	"context"
	"errors"

	"connectrpc.com/connect"
	"github.com/google/uuid"
	v1 "github.com/mauro-afa91/spendsense/gen/spendsense/v1"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/middleware"
	"github.com/mauro-afa91/spendsense/internal/service"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type BudgetHandler struct {
	profiles     *service.BudgetProfileService
	transactions *service.TransactionService
}

func NewBudgetHandler(profiles *service.BudgetProfileService, transactions *service.TransactionService) *BudgetHandler {
	return &BudgetHandler{profiles: profiles, transactions: transactions}
}

func (h *BudgetHandler) currentUserID(ctx context.Context) (uuid.UUID, error) {
	raw, ok := middleware.UserIDFromContext(ctx)
	if !ok {
		return uuid.UUID{}, connect.NewError(connect.CodeUnauthenticated, nil)
	}
	id, err := uuid.Parse(raw)
	if err != nil {
		return uuid.UUID{}, connect.NewError(connect.CodeUnauthenticated, nil)
	}
	return id, nil
}

// ── Transactions ──────────────────────────────────────────────────────────────

func (h *BudgetHandler) ListTransactions(ctx context.Context, req *connect.Request[v1.ListTransactionsRequest]) (*connect.Response[v1.ListTransactionsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	periodID, err := uuid.Parse(req.Msg.BudgetPeriodId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	params := db.ListTransactionsParams{BudgetPeriodID: periodID}
	if req.Msg.CategoryId != 0 {
		v := req.Msg.CategoryId
		params.CategoryID = &v
	}
	if req.Msg.TransactionTypeId != 0 {
		v := req.Msg.TransactionTypeId
		params.TransactionTypeID = &v
	}
	txns, svcErr := h.transactions.List(ctx, params, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.Transaction, len(txns))
	for i, t := range txns {
		protos[i] = toProtoTransaction(t)
	}
	return connect.NewResponse(&v1.ListTransactionsResponse{Transactions: protos}), nil
}

func (h *BudgetHandler) CreateTransaction(ctx context.Context, req *connect.Request[v1.CreateTransactionRequest]) (*connect.Response[v1.CreateTransactionResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	var periodID *uuid.UUID
	if req.Msg.BudgetPeriodId != "" {
		pid, parseErr := uuid.Parse(req.Msg.BudgetPeriodId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		periodID = &pid
	}
	var pmID *uuid.UUID
	if req.Msg.PaymentMethodId != "" {
		pid, parseErr := uuid.Parse(req.Msg.PaymentMethodId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		pmID = &pid
	}
	var catID, freqID, typeID *int32
	if req.Msg.CategoryId != 0 {
		v := req.Msg.CategoryId
		catID = &v
	}
	if req.Msg.TransactionFrequencyId != 0 {
		v := req.Msg.TransactionFrequencyId
		freqID = &v
	}
	if req.Msg.TransactionTypeId != 0 {
		v := req.Msg.TransactionTypeId
		typeID = &v
	}
	recurring := req.Msg.Recurring
	name := req.Msg.Name
	params := db.CreateTransactionParams{
		Name:                   &name,
		Amount:                 numericFromMoney(req.Msg.Amount),
		PlannedAmount:          numericFromMoney(req.Msg.PlannedAmount),
		Date:                   dateFromProtoTS(req.Msg.Date),
		RenewalDate:            dateFromProtoTS(req.Msg.RenewalDate),
		Recurring:              &recurring,
		BudgetPeriodID:         periodID,
		CategoryID:             catID,
		PaymentMethodID:        pmID,
		TransactionFrequencyID: freqID,
		TransactionTypeID:      typeID,
	}
	txn, svcErr := h.transactions.Create(ctx, params, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreateTransactionResponse{Transaction: toProtoTransaction(txn)}), nil
}

func (h *BudgetHandler) UpdateTransaction(ctx context.Context, req *connect.Request[v1.UpdateTransactionRequest]) (*connect.Response[v1.UpdateTransactionResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	var pmID *uuid.UUID
	if req.Msg.PaymentMethodId != "" {
		pid, parseErr := uuid.Parse(req.Msg.PaymentMethodId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		pmID = &pid
	}
	var catID, freqID, typeID *int32
	if req.Msg.CategoryId != 0 {
		v := req.Msg.CategoryId
		catID = &v
	}
	if req.Msg.TransactionFrequencyId != 0 {
		v := req.Msg.TransactionFrequencyId
		freqID = &v
	}
	if req.Msg.TransactionTypeId != 0 {
		v := req.Msg.TransactionTypeId
		typeID = &v
	}
	recurring := req.Msg.Recurring
	name := req.Msg.Name
	params := db.UpdateTransactionParams{
		ID:                     id,
		Name:                   &name,
		Amount:                 numericFromMoney(req.Msg.Amount),
		PlannedAmount:          numericFromMoney(req.Msg.PlannedAmount),
		Date:                   dateFromProtoTS(req.Msg.Date),
		Recurring:              &recurring,
		CategoryID:             catID,
		PaymentMethodID:        pmID,
		TransactionFrequencyID: freqID,
		TransactionTypeID:      typeID,
	}
	updated, svcErr := h.transactions.Update(ctx, params, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateTransactionResponse{Transaction: toProtoTransaction(updated)}), nil
}

func (h *BudgetHandler) DeleteTransaction(ctx context.Context, req *connect.Request[v1.DeleteTransactionRequest]) (*connect.Response[v1.DeleteTransactionResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.transactions.Delete(ctx, id, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteTransactionResponse{}), nil
}

// ── Categories ────────────────────────────────────────────────────────────────

func (h *BudgetHandler) ListCategories(ctx context.Context, _ *connect.Request[v1.ListCategoriesRequest]) (*connect.Response[v1.ListCategoriesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	cats, svcErr := h.transactions.ListCategories(ctx, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.Category, len(cats))
	for i, c := range cats {
		protos[i] = &v1.Category{Id: c.ID, Name: c.Name, IsSystem: c.IsSystem, Color: c.Color}
	}
	return connect.NewResponse(&v1.ListCategoriesResponse{Categories: protos}), nil
}

func (h *BudgetHandler) CreateCategory(ctx context.Context, req *connect.Request[v1.CreateCategoryRequest]) (*connect.Response[v1.CreateCategoryResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	cat, svcErr := h.transactions.CreateCategory(ctx, db.CreateCategoryParams{
		Name:   req.Msg.Name,
		UserID: userID,
		Color:  req.Msg.Color,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreateCategoryResponse{
		Category: &v1.Category{Id: cat.ID, Name: cat.Name, IsSystem: cat.IsSystem, Color: cat.Color},
	}), nil
}

func (h *BudgetHandler) UpdateCategory(ctx context.Context, req *connect.Request[v1.UpdateCategoryRequest]) (*connect.Response[v1.UpdateCategoryResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	cat, svcErr := h.transactions.UpdateCategory(ctx, db.UpdateCategoryParams{
		ID:     req.Msg.Id,
		Name:   req.Msg.Name,
		Color:  req.Msg.Color,
		UserID: userID,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateCategoryResponse{
		Category: &v1.Category{Id: cat.ID, Name: cat.Name, IsSystem: cat.IsSystem, Color: cat.Color},
	}), nil
}

func (h *BudgetHandler) DeleteCategory(ctx context.Context, req *connect.Request[v1.DeleteCategoryRequest]) (*connect.Response[v1.DeleteCategoryResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	svcErr := h.transactions.DeleteCategory(ctx, req.Msg.Id, req.Msg.ReplacementId, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteCategoryResponse{}), nil
}

// ── Payment Methods ───────────────────────────────────────────────────────────

func (h *BudgetHandler) ListPaymentMethods(ctx context.Context, req *connect.Request[v1.ListPaymentMethodsRequest]) (*connect.Response[v1.ListPaymentMethodsResponse], error) {
	if _, err := h.currentUserID(ctx); err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	methods, svcErr := h.transactions.ListPaymentMethods(ctx, profileID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.PaymentMethod, len(methods))
	for i, m := range methods {
		typeVal := v1.PaymentType_PAYMENT_TYPE_UNSPECIFIED
		if m.PaymentTypeID != nil {
			typeVal = v1.PaymentType(*m.PaymentTypeID)
		}
		var personID int64
		if m.BudgetPersonID != nil {
			personID = int64(*m.BudgetPersonID)
		}
		protos[i] = &v1.PaymentMethod{Id: m.ID.String(), Name: m.Name, Type: typeVal, BudgetPersonId: personID, Color: m.Color}
	}
	return connect.NewResponse(&v1.ListPaymentMethodsResponse{Methods: protos}), nil
}

func (h *BudgetHandler) CreatePaymentMethod(ctx context.Context, req *connect.Request[v1.CreatePaymentMethodRequest]) (*connect.Response[v1.CreatePaymentMethodResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	if req.Msg.BudgetPersonId == 0 {
		return nil, connect.NewError(connect.CodeInvalidArgument, errors.New("budget_person_id is required"))
	}
	typeID := int32(req.Msg.Type)
	v := int32(req.Msg.BudgetPersonId)
	personID := &v
	method, svcErr := h.transactions.CreatePaymentMethod(ctx, db.CreatePaymentMethodParams{
		Name:           req.Msg.Name,
		PaymentTypeID:  &typeID,
		UserID:         &userID,
		BudgetPersonID: personID,
		Color:          req.Msg.Color,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	var retPersonID int64
	if method.BudgetPersonID != nil {
		retPersonID = int64(*method.BudgetPersonID)
	}
	return connect.NewResponse(&v1.CreatePaymentMethodResponse{
		Method: &v1.PaymentMethod{
			Id:             method.ID.String(),
			Name:           method.Name,
			Type:           req.Msg.Type,
			BudgetPersonId: retPersonID,
			Color:          method.Color,
		},
	}), nil
}

func (h *BudgetHandler) UpdatePaymentMethod(ctx context.Context, req *connect.Request[v1.UpdatePaymentMethodRequest]) (*connect.Response[v1.UpdatePaymentMethodResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, toConnectError(apperr.Invalid("invalid payment method id"))
	}
	method, svcErr := h.transactions.UpdatePaymentMethod(ctx, db.UpdatePaymentMethodParams{
		ID:     id,
		Name:   req.Msg.Name,
		Color:  req.Msg.Color,
		UserID: userID,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	var typeVal v1.PaymentType
	if method.PaymentTypeID != nil {
		typeVal = v1.PaymentType(*method.PaymentTypeID)
	}
	var personID int64
	if method.BudgetPersonID != nil {
		personID = int64(*method.BudgetPersonID)
	}
	return connect.NewResponse(&v1.UpdatePaymentMethodResponse{
		Method: &v1.PaymentMethod{
			Id:             method.ID.String(),
			Name:           method.Name,
			Type:           typeVal,
			BudgetPersonId: personID,
			Color:          method.Color,
		},
	}), nil
}

func (h *BudgetHandler) DeletePaymentMethod(ctx context.Context, req *connect.Request[v1.DeletePaymentMethodRequest]) (*connect.Response[v1.DeletePaymentMethodResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, toConnectError(apperr.Invalid("invalid payment method id"))
	}
	replacementID, err := uuid.Parse(req.Msg.ReplacementId)
	if err != nil {
		return nil, toConnectError(apperr.Invalid("invalid replacement payment method id"))
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, toConnectError(apperr.Invalid("invalid budget profile id"))
	}
	if svcErr := h.transactions.DeletePaymentMethod(ctx, id, replacementID, profileID, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeletePaymentMethodResponse{}), nil
}

// ── Conversion helpers ────────────────────────────────────────────────────────

func toProtoTransaction(t db.Transaction) *v1.Transaction {
	return &v1.Transaction{
		Id:                     t.ID.String(),
		Name:                   nullStr(t.Name),
		Amount:                 moneyFromNumeric(t.Amount),
		PlannedAmount:          moneyFromNumeric(t.PlannedAmount),
		Date:                   protoTSFromDate(t.Date),
		RenewalDate:            protoTSFromDate(t.RenewalDate),
		Recurring:              t.Recurring != nil && *t.Recurring,
		BudgetPeriodId:         nullUUID(t.BudgetPeriodID),
		CategoryId:             ptrInt32OrZero(t.CategoryID),
		PaymentMethodId:        nullUUID(t.PaymentMethodID),
		TransactionFrequencyId: ptrInt32OrZero(t.TransactionFrequencyID),
		TransactionTypeId:      ptrInt32OrZero(t.TransactionTypeID),
	}
}

func ptrInt32OrZero(p *int32) int32 {
	if p == nil {
		return 0
	}
	return *p
}
