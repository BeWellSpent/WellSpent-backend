package handler

import (
	"context"

	"connectrpc.com/connect"
	"github.com/google/uuid"
	v1 "github.com/mauro-afa91/spendsense/gen/spendsense/v1"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/middleware"
	"github.com/mauro-afa91/spendsense/internal/service"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type BudgetHandler struct {
	budgets      *service.BudgetService
	transactions *service.TransactionService
}

func NewBudgetHandler(budgets *service.BudgetService, transactions *service.TransactionService) *BudgetHandler {
	return &BudgetHandler{budgets: budgets, transactions: transactions}
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

// ── Budget CRUD ───────────────────────────────────────────────────────────────

func (h *BudgetHandler) ListBudgets(ctx context.Context, _ *connect.Request[v1.ListBudgetsRequest]) (*connect.Response[v1.ListBudgetsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgets, svcErr := h.budgets.List(ctx, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.Budget, len(budgets))
	for i, b := range budgets {
		protos[i] = toProtoBudget(b)
	}
	return connect.NewResponse(&v1.ListBudgetsResponse{Budgets: protos}), nil
}

func (h *BudgetHandler) GetBudget(ctx context.Context, req *connect.Request[v1.GetBudgetRequest]) (*connect.Response[v1.GetBudgetResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	budget, svcErr := h.budgets.Get(ctx, id, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.GetBudgetResponse{Budget: toProtoBudget(budget)}), nil
}

func (h *BudgetHandler) CreateBudget(ctx context.Context, req *connect.Request[v1.CreateBudgetRequest]) (*connect.Response[v1.CreateBudgetResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budget, svcErr := h.budgets.Create(ctx, userID, req.Msg.Name)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreateBudgetResponse{Budget: toProtoBudget(budget)}), nil
}

func (h *BudgetHandler) UpdateBudget(ctx context.Context, req *connect.Request[v1.UpdateBudgetRequest]) (*connect.Response[v1.UpdateBudgetResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	budget, svcErr := h.budgets.Update(ctx, id, userID, req.Msg.Name, req.Msg.Active)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateBudgetResponse{Budget: toProtoBudget(budget)}), nil
}

func (h *BudgetHandler) DeleteBudget(ctx context.Context, req *connect.Request[v1.DeleteBudgetRequest]) (*connect.Response[v1.DeleteBudgetResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.budgets.Delete(ctx, id, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteBudgetResponse{}), nil
}

// ── People ────────────────────────────────────────────────────────────────────

func (h *BudgetHandler) AddBudgetPeople(ctx context.Context, req *connect.Request[v1.AddBudgetPeopleRequest]) (*connect.Response[v1.AddBudgetPeopleResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	people := make([]service.PersonInput, len(req.Msg.People))
	for i, p := range req.Msg.People {
		pi := service.PersonInput{UserName: p.UserName}
		if p.UserId != "" {
			uid, err := uuid.Parse(p.UserId)
			if err != nil {
				return nil, connect.NewError(connect.CodeInvalidArgument, err)
			}
			pi.UserID = &uid
		}
		people[i] = pi
	}
	mappings, svcErr := h.budgets.AddPeople(ctx, budgetID, userID, people)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.BudgetPerson, len(mappings))
	for i, m := range mappings {
		protos[i] = toProtoPerson(m)
	}
	return connect.NewResponse(&v1.AddBudgetPeopleResponse{People: protos}), nil
}

func (h *BudgetHandler) ListBudgetPeople(ctx context.Context, req *connect.Request[v1.ListBudgetPeopleRequest]) (*connect.Response[v1.ListBudgetPeopleResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	mappings, svcErr := h.budgets.ListPeople(ctx, budgetID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.BudgetPerson, len(mappings))
	for i, m := range mappings {
		protos[i] = toProtoPerson(m)
	}
	return connect.NewResponse(&v1.ListBudgetPeopleResponse{People: protos}), nil
}

func (h *BudgetHandler) RemoveBudgetPerson(ctx context.Context, req *connect.Request[v1.RemoveBudgetPersonRequest]) (*connect.Response[v1.RemoveBudgetPersonResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.budgets.RemovePerson(ctx, budgetID, int32(req.Msg.PersonId), userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.RemoveBudgetPersonResponse{}), nil
}

// ── Income ────────────────────────────────────────────────────────────────────

func (h *BudgetHandler) AddIncomeEntry(ctx context.Context, req *connect.Request[v1.AddIncomeEntryRequest]) (*connect.Response[v1.AddIncomeEntryResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	var entryUserID *uuid.UUID
	if req.Msg.UserId != "" {
		uid, err := uuid.Parse(req.Msg.UserId)
		if err != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, err)
		}
		entryUserID = &uid
	}
	entries, svcErr := h.budgets.AddIncome(ctx, budgetID, userID, []service.IncomeInput{{
		Name:      req.Msg.Name,
		Amount:    numericFromMoney(req.Msg.Amount),
		Recurring: req.Msg.Recurring,
		UserID:    entryUserID,
	}})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.AddIncomeEntryResponse{Entry: toProtoIncome(entries[0])}), nil
}

func (h *BudgetHandler) ListIncomeEntries(ctx context.Context, req *connect.Request[v1.ListIncomeEntriesRequest]) (*connect.Response[v1.ListIncomeEntriesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	entries, svcErr := h.budgets.ListIncome(ctx, budgetID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.IncomeEntry, len(entries))
	for i, e := range entries {
		protos[i] = toProtoIncome(e)
	}
	return connect.NewResponse(&v1.ListIncomeEntriesResponse{Entries: protos}), nil
}

func (h *BudgetHandler) UpdateIncomeEntry(ctx context.Context, req *connect.Request[v1.UpdateIncomeEntryRequest]) (*connect.Response[v1.UpdateIncomeEntryResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	entry, svcErr := h.budgets.UpdateIncome(ctx, int32(req.Msg.Id), budgetID, userID, req.Msg.Name, numericFromMoney(req.Msg.Amount), req.Msg.Recurring)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateIncomeEntryResponse{Entry: toProtoIncome(entry)}), nil
}

func (h *BudgetHandler) DeleteIncomeEntry(ctx context.Context, req *connect.Request[v1.DeleteIncomeEntryRequest]) (*connect.Response[v1.DeleteIncomeEntryResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.budgets.DeleteIncome(ctx, int32(req.Msg.Id), budgetID, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteIncomeEntryResponse{}), nil
}

// ── Transactions ──────────────────────────────────────────────────────────────

func (h *BudgetHandler) ListTransactions(ctx context.Context, req *connect.Request[v1.ListTransactionsRequest]) (*connect.Response[v1.ListTransactionsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	params := db.ListTransactionsParams{BudgetID: budgetID}
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
	var budgetID *uuid.UUID
	if req.Msg.BudgetId != "" {
		bid, err := uuid.Parse(req.Msg.BudgetId)
		if err != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, err)
		}
		budgetID = &bid
	}
	var pmID *uuid.UUID
	if req.Msg.PaymentMethodId != "" {
		pid, err := uuid.Parse(req.Msg.PaymentMethodId)
		if err != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, err)
		}
		pmID = &pid
	}
	var catID, freqID, typeID *int32
	if req.Msg.CategoryId != 0 {
		v := req.Msg.CategoryId; catID = &v
	}
	if req.Msg.TransactionFrequencyId != 0 {
		v := req.Msg.TransactionFrequencyId; freqID = &v
	}
	if req.Msg.TransactionTypeId != 0 {
		v := req.Msg.TransactionTypeId; typeID = &v
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
		BudgetID:               budgetID,
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
		pid, err := uuid.Parse(req.Msg.PaymentMethodId)
		if err != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, err)
		}
		pmID = &pid
	}
	var catID, freqID, typeID *int32
	if req.Msg.CategoryId != 0 {
		v := req.Msg.CategoryId; catID = &v
	}
	if req.Msg.TransactionFrequencyId != 0 {
		v := req.Msg.TransactionFrequencyId; freqID = &v
	}
	if req.Msg.TransactionTypeId != 0 {
		v := req.Msg.TransactionTypeId; typeID = &v
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
	// Budget ownership enforced by service using budgetID from the existing transaction
	existing, fetchErr := h.transactions.GetByID(ctx, id)
	if fetchErr != nil {
		return nil, toConnectError(fetchErr)
	}
	var budgetID uuid.UUID
	if existing.BudgetID != nil {
		budgetID = *existing.BudgetID
	}
	updated, svcErr2 := h.transactions.Update(ctx, params, budgetID, userID)
	if svcErr2 != nil {
		return nil, toConnectError(svcErr2)
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
	budgetID, err := uuid.Parse(req.Msg.BudgetId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.transactions.Delete(ctx, id, budgetID, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteTransactionResponse{}), nil
}

// ── Categories & Payment Methods ──────────────────────────────────────────────

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
		typeID := int32(0)
		if c.TypeID != nil {
			typeID = *c.TypeID
		}
		protos[i] = &v1.Category{Id: c.ID, Name: c.Name, TypeId: typeID}
	}
	return connect.NewResponse(&v1.ListCategoriesResponse{Categories: protos}), nil
}

func (h *BudgetHandler) ListPaymentMethods(ctx context.Context, _ *connect.Request[v1.ListPaymentMethodsRequest]) (*connect.Response[v1.ListPaymentMethodsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	methods, svcErr := h.transactions.ListPaymentMethods(ctx, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.PaymentMethod, len(methods))
	for i, m := range methods {
		typeVal := v1.PaymentType_PAYMENT_TYPE_UNSPECIFIED
		if m.PaymentTypeID != nil {
			typeVal = v1.PaymentType(*m.PaymentTypeID)
		}
		protos[i] = &v1.PaymentMethod{Id: m.ID.String(), Name: m.Name, Type: typeVal}
	}
	return connect.NewResponse(&v1.ListPaymentMethodsResponse{Methods: protos}), nil
}

func (h *BudgetHandler) CreatePaymentMethod(ctx context.Context, req *connect.Request[v1.CreatePaymentMethodRequest]) (*connect.Response[v1.CreatePaymentMethodResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	typeID := int32(req.Msg.Type)
	method, svcErr := h.transactions.CreatePaymentMethod(ctx, db.CreatePaymentMethodParams{
		Name:          req.Msg.Name,
		PaymentTypeID: &typeID,
		UserID:        &userID,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreatePaymentMethodResponse{
		Method: &v1.PaymentMethod{
			Id:   method.ID.String(),
			Name: method.Name,
			Type: req.Msg.Type,
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
		UserID: userID,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	var typeVal v1.PaymentType
	if method.PaymentTypeID != nil {
		typeVal = v1.PaymentType(*method.PaymentTypeID)
	}
	return connect.NewResponse(&v1.UpdatePaymentMethodResponse{
		Method: &v1.PaymentMethod{
			Id:   method.ID.String(),
			Name: method.Name,
			Type: typeVal,
		},
	}), nil
}

// ── Helper conversions ────────────────────────────────────────────────────────

func toProtoBudget(b db.Budget) *v1.Budget {
	return &v1.Budget{
		Id:        b.ID.String(),
		UserId:    b.UserID.String(),
		Name:      b.Name,
		StartDate: protoTSFromDate(b.StartDate),
		EndDate:   protoTSFromDate(b.EndDate),
		Active:    b.Active,
	}
}

func toProtoPerson(m db.BudgetToUserMapping) *v1.BudgetPerson {
	return &v1.BudgetPerson{
		Id:       int64(m.ID),
		BudgetId: m.BudgetID.String(),
		UserName: nullStr(m.UserName),
		UserId:   nullUUID(m.UserID),
	}
}

func toProtoIncome(m db.IncomeToBudgetMapping) *v1.IncomeEntry {
	return &v1.IncomeEntry{
		Id:        int64(m.ID),
		BudgetId:  m.BudgetID.String(),
		UserId:    nullUUID(m.UserID),
		Name:      nullStr(m.Name),
		Amount:    moneyFromNumeric(m.Amount),
		Recurring: m.Recurring,
	}
}

func toProtoTransaction(t db.Transaction) *v1.Transaction {
	return &v1.Transaction{
		Id:                     t.ID.String(),
		Name:                   nullStr(t.Name),
		Amount:                 moneyFromNumeric(t.Amount),
		PlannedAmount:          moneyFromNumeric(t.PlannedAmount),
		Date:                   protoTSFromDate(t.Date),
		RenewalDate:            protoTSFromDate(t.RenewalDate),
		Recurring:              t.Recurring != nil && *t.Recurring,
		BudgetId:               nullUUID(t.BudgetID),
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

