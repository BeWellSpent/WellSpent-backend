package handler

import (
	"context"
	"errors"
	"time"

	"connectrpc.com/connect"
	"github.com/google/uuid"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"google.golang.org/protobuf/types/known/timestamppb"
)

type BudgetHandler struct {
	profiles     *service.BudgetProfileService
	transactions *service.TransactionService
	allocations  *service.ExpenseAllocationService
}

func NewBudgetHandler(profiles *service.BudgetProfileService, transactions *service.TransactionService, allocations *service.ExpenseAllocationService) *BudgetHandler {
	return &BudgetHandler{profiles: profiles, transactions: transactions, allocations: allocations}
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

func (h *BudgetHandler) MarkTransactionAsPaid(ctx context.Context, req *connect.Request[v1.MarkTransactionAsPaidRequest]) (*connect.Response[v1.MarkTransactionAsPaidResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	periodID, err := uuid.Parse(req.Msg.BudgetPeriodId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	tx, svcErr := h.transactions.MarkTransactionAsPaid(ctx, id, periodID, numericFromMoney(req.Msg.PaidAmount), dateFromProtoTS(req.Msg.PaidAt), userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.MarkTransactionAsPaidResponse{Transaction: toProtoTransaction(tx)}), nil
}

func (h *BudgetHandler) UnmarkTransactionAsPaid(ctx context.Context, req *connect.Request[v1.UnmarkTransactionAsPaidRequest]) (*connect.Response[v1.UnmarkTransactionAsPaidResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	periodID, err := uuid.Parse(req.Msg.BudgetPeriodId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	tx, svcErr := h.transactions.UnmarkTransactionAsPaid(ctx, id, periodID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UnmarkTransactionAsPaidResponse{Transaction: toProtoTransaction(tx)}), nil
}

// ── Categories ────────────────────────────────────────────────────────────────

func (h *BudgetHandler) ListCategories(ctx context.Context, req *connect.Request[v1.ListCategoriesRequest]) (*connect.Response[v1.ListCategoriesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	var budgetProfileID *uuid.UUID
	if req.Msg.BudgetProfileId != "" {
		id, parseErr := uuid.Parse(req.Msg.BudgetProfileId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, errors.New("invalid budget_profile_id"))
		}
		budgetProfileID = &id
	}
	cats, svcErr := h.transactions.ListCategories(ctx, userID, budgetProfileID)
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
		ID:    id,
		Name:  req.Msg.Name,
		Color: req.Msg.Color,
	}, userID)
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
	proto := &v1.Transaction{
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
		IsPaid:                 t.IsPaid,
	}
	if t.PaidDate.Valid {
		proto.PaidAt = protoTSFromDate(t.PaidDate)
	}
	if t.FixedExpenseID != nil {
		proto.FixedExpenseId = t.FixedExpenseID.String()
	}
	return proto
}

func toProtoFixedExpense(fe db.FixedExpense) *v1.FixedExpense {
	proto := &v1.FixedExpense{
		Id:              fe.ID.String(),
		BudgetProfileId: fe.BudgetProfileID.String(),
		Name:            fe.Name,
		PlannedAmount:   moneyFromNumeric(fe.PlannedAmount),
		DayOfMonth:      fe.DayOfMonth,
		IsActive:        fe.IsActive,
		IntervalMonths:  fe.IntervalMonths,
		NextDueDate:     timestamppb.New(service.FixedExpenseNextDueDate(fe, time.Now().UTC())),
		FrequencyUnit:   v1.FrequencyUnit(fe.FrequencyUnit),
		IntervalWeeks:   fe.IntervalWeeks,
		DayOfWeek:       int32(fe.DayOfWeek),
	}
	if fe.CategoryID != nil {
		proto.CategoryId = *fe.CategoryID
	}
	if fe.PaymentMethodID != nil {
		proto.PaymentMethodId = fe.PaymentMethodID.String()
	}
	if fe.AnchorDate.Valid {
		proto.AnchorDate = timestamppb.New(fe.AnchorDate.Time)
	}
	if fe.EndDate.Valid {
		proto.EndDate = timestamppb.New(fe.EndDate.Time)
	}
	if fe.TotalPayments != nil {
		proto.TotalPayments = *fe.TotalPayments
	}
	return proto
}

func ptrInt32OrZero(p *int32) int32 {
	if p == nil {
		return 0
	}
	return *p
}

// ── Fixed Expenses ────────────────────────────────────────────────────────────

func (h *BudgetHandler) ListFixedExpenses(ctx context.Context, req *connect.Request[v1.ListFixedExpensesRequest]) (*connect.Response[v1.ListFixedExpensesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	expenses, svcErr := h.profiles.ListFixedExpenses(ctx, profileID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.FixedExpense, len(expenses))
	for i, fe := range expenses {
		protos[i] = toProtoFixedExpense(fe)
	}
	return connect.NewResponse(&v1.ListFixedExpensesResponse{Expenses: protos}), nil
}

func (h *BudgetHandler) CreateFixedExpense(ctx context.Context, req *connect.Request[v1.CreateFixedExpenseRequest]) (*connect.Response[v1.CreateFixedExpenseResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	var catID *int32
	if req.Msg.CategoryId != 0 {
		v := req.Msg.CategoryId
		catID = &v
	}
	var pmID *uuid.UUID
	if req.Msg.PaymentMethodId != "" {
		pid, parseErr := uuid.Parse(req.Msg.PaymentMethodId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		pmID = &pid
	}
	var anchorDate *time.Time
	if req.Msg.AnchorDate != nil {
		t := req.Msg.AnchorDate.AsTime()
		anchorDate = &t
	}
	var endDate *time.Time
	if req.Msg.EndDate != nil {
		t := req.Msg.EndDate.AsTime()
		endDate = &t
	}
	fe, tx, svcErr := h.profiles.CreateFixedExpense(ctx, profileID, userID, service.FixedExpenseInput{
		Name:            req.Msg.Name,
		PlannedAmount:   numericFromMoney(req.Msg.PlannedAmount),
		CategoryID:      catID,
		PaymentMethodID: pmID,
		DayOfMonth:      req.Msg.DayOfMonth,
		IntervalMonths:  req.Msg.IntervalMonths,
		AnchorDate:      anchorDate,
		FrequencyUnit:   int16(req.Msg.FrequencyUnit),
		IntervalWeeks:   req.Msg.IntervalWeeks,
		DayOfWeek:       req.Msg.DayOfWeek,
		EndDate:         endDate,
		TotalPayments:   req.Msg.TotalPayments,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	resp := &v1.CreateFixedExpenseResponse{Expense: toProtoFixedExpense(fe)}
	if tx != nil {
		resp.Transaction = toProtoTransaction(*tx)
	}
	return connect.NewResponse(resp), nil
}

func (h *BudgetHandler) UpdateFixedExpense(ctx context.Context, req *connect.Request[v1.UpdateFixedExpenseRequest]) (*connect.Response[v1.UpdateFixedExpenseResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	var catID *int32
	if req.Msg.CategoryId != 0 {
		v := req.Msg.CategoryId
		catID = &v
	}
	var pmID *uuid.UUID
	if req.Msg.PaymentMethodId != "" {
		pid, parseErr := uuid.Parse(req.Msg.PaymentMethodId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		pmID = &pid
	}
	var anchorDate *time.Time
	if req.Msg.AnchorDate != nil {
		t := req.Msg.AnchorDate.AsTime()
		anchorDate = &t
	}
	var endDate *time.Time
	if req.Msg.EndDate != nil {
		t := req.Msg.EndDate.AsTime()
		endDate = &t
	}
	fe, svcErr := h.profiles.UpdateFixedExpense(ctx, id, profileID, userID, service.FixedExpenseInput{
		Name:            req.Msg.Name,
		PlannedAmount:   numericFromMoney(req.Msg.PlannedAmount),
		CategoryID:      catID,
		PaymentMethodID: pmID,
		DayOfMonth:      req.Msg.DayOfMonth,
		IntervalMonths:  req.Msg.IntervalMonths,
		AnchorDate:      anchorDate,
		FrequencyUnit:   int16(req.Msg.FrequencyUnit),
		IntervalWeeks:   req.Msg.IntervalWeeks,
		DayOfWeek:       req.Msg.DayOfWeek,
		EndDate:         endDate,
		TotalPayments:   req.Msg.TotalPayments,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateFixedExpenseResponse{Expense: toProtoFixedExpense(fe)}), nil
}

func (h *BudgetHandler) DeleteFixedExpense(ctx context.Context, req *connect.Request[v1.DeleteFixedExpenseRequest]) (*connect.Response[v1.DeleteFixedExpenseResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.profiles.DeleteFixedExpense(ctx, id, profileID, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteFixedExpenseResponse{}), nil
}

// ── Expense Allocations ───────────────────────────────────────────────────────

func (h *BudgetHandler) ListExpenseAllocations(ctx context.Context, req *connect.Request[v1.ListExpenseAllocationsRequest]) (*connect.Response[v1.ListExpenseAllocationsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	rows, svcErr := h.allocations.List(ctx, profileID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.ExpenseAllocation, len(rows))
	for i, a := range rows {
		protos[i] = toProtoExpenseAllocation(a)
	}
	return connect.NewResponse(&v1.ListExpenseAllocationsResponse{Allocations: protos}), nil
}

func (h *BudgetHandler) UpsertExpenseAllocation(ctx context.Context, req *connect.Request[v1.UpsertExpenseAllocationRequest]) (*connect.Response[v1.UpsertExpenseAllocationResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	var personID *int32
	if req.Msg.BudgetPersonId != 0 {
		v := int32(req.Msg.BudgetPersonId)
		personID = &v
	}
	alloc, svcErr := h.allocations.Upsert(ctx, profileID, userID, req.Msg.CategoryId, personID, numericFromMoney(req.Msg.PlannedAmount))
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpsertExpenseAllocationResponse{Allocation: toProtoExpenseAllocation(alloc)}), nil
}

func (h *BudgetHandler) DeleteExpenseAllocation(ctx context.Context, req *connect.Request[v1.DeleteExpenseAllocationRequest]) (*connect.Response[v1.DeleteExpenseAllocationResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.allocations.Delete(ctx, int32(req.Msg.Id), profileID, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteExpenseAllocationResponse{}), nil
}

// ── Transaction review ────────────────────────────────────────────────────────

func (h *BudgetHandler) ListTransactionReviews(ctx context.Context, req *connect.Request[v1.ListTransactionReviewsRequest]) (*connect.Response[v1.ListTransactionReviewsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	rows, svcErr := h.transactions.ListTransactionReviews(ctx, userID, profileID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	reviews := make([]*v1.TransactionReview, 0, len(rows))
	for _, r := range rows {
		score, _ := r.MatchScore.Float64Value()
		txName := ""
		if r.TransactionName != nil {
			txName = *r.TransactionName
		}
		matchedName := ""
		if r.MatchedTransactionName != nil {
			matchedName = *r.MatchedTransactionName
		}
		review := &v1.TransactionReview{
			Id:                     r.ID.String(),
			BudgetPeriodId:         r.BudgetPeriodID.String(),
			TransactionId:          r.TransactionID.String(),
			MatchScore:             score.Float64,
			Status:                 r.Status,
			TransactionName:        txName,
			TransactionAmount:      moneyFromNumeric(r.TransactionAmount),
			MatchedTransactionId:   r.MatchedTransactionID.String(),
			MatchedTransactionName: matchedName,
		}
		if r.CreatedAt.Valid {
			review.CreatedAt = timestamppb.New(r.CreatedAt.Time)
		}
		reviews = append(reviews, review)
	}
	return connect.NewResponse(&v1.ListTransactionReviewsResponse{Reviews: reviews}), nil
}

func (h *BudgetHandler) ConfirmTransactionReview(ctx context.Context, req *connect.Request[v1.ConfirmTransactionReviewRequest]) (*connect.Response[v1.ConfirmTransactionReviewResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	reviewID, err := uuid.Parse(req.Msg.ReviewId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.transactions.ConfirmTransactionReview(ctx, userID, reviewID, profileID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.ConfirmTransactionReviewResponse{}), nil
}

func (h *BudgetHandler) DismissTransactionReview(ctx context.Context, req *connect.Request[v1.DismissTransactionReviewRequest]) (*connect.Response[v1.DismissTransactionReviewResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	reviewID, err := uuid.Parse(req.Msg.ReviewId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.transactions.DismissTransactionReview(ctx, userID, reviewID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DismissTransactionReviewResponse{}), nil
}

func (h *BudgetHandler) MarkTransactionForReview(ctx context.Context, req *connect.Request[v1.MarkTransactionForReviewRequest]) (*connect.Response[v1.MarkTransactionForReviewResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	txID, err := uuid.Parse(req.Msg.TransactionId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	matchedTxID, err := uuid.Parse(req.Msg.MatchedTransactionId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	review, svcErr := h.transactions.MarkTransactionForReview(ctx, userID, txID, matchedTxID, profileID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	scoreF8, _ := review.MatchScore.Float64Value()
	protoReview := &v1.TransactionReview{
		Id:                   review.ID.String(),
		BudgetPeriodId:       review.BudgetPeriodID.String(),
		TransactionId:        review.TransactionID.String(),
		MatchedTransactionId: review.MatchedTransactionID.String(),
		MatchScore:           scoreF8.Float64,
		Status:               string(review.Status),
	}
	if review.CreatedAt.Valid {
		protoReview.CreatedAt = timestamppb.New(review.CreatedAt.Time)
	}
	return connect.NewResponse(&v1.MarkTransactionForReviewResponse{Review: protoReview}), nil
}

func toProtoExpenseAllocation(a db.ExpenseAllocation) *v1.ExpenseAllocation {
	var personID int64
	if a.BudgetPersonID != nil {
		personID = int64(*a.BudgetPersonID)
	}
	return &v1.ExpenseAllocation{
		Id:              int64(a.ID),
		BudgetProfileId: a.BudgetProfileID.String(),
		CategoryId:      a.CategoryID,
		BudgetPersonId:  personID,
		PlannedAmount:   moneyFromNumeric(a.PlannedAmount),
	}
}
