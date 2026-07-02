package handler

import (
	"context"

	"connectrpc.com/connect"
	"github.com/google/uuid"
	v1 "github.com/mauro-afa91/spendsense/gen/spendsense/v1"
	"github.com/mauro-afa91/spendsense/internal/service"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

// ── Profile CRUD ──────────────────────────────────────────────────────────────

func (h *BudgetHandler) CreateBudgetProfile(ctx context.Context, req *connect.Request[v1.CreateBudgetProfileRequest]) (*connect.Response[v1.CreateBudgetProfileResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	cycle := cycleStringFromProto(req.Msg.Cycle)
	profile, _, svcErr := h.profiles.Create(ctx, userID, req.Msg.Name, cycle)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreateBudgetProfileResponse{Profile: toProtoBudgetProfile(profile)}), nil
}

func (h *BudgetHandler) ListBudgetProfiles(ctx context.Context, _ *connect.Request[v1.ListBudgetProfilesRequest]) (*connect.Response[v1.ListBudgetProfilesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profiles, svcErr := h.profiles.List(ctx, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.BudgetProfile, len(profiles))
	for i, p := range profiles {
		protos[i] = toProtoBudgetProfile(p)
	}
	return connect.NewResponse(&v1.ListBudgetProfilesResponse{Profiles: protos}), nil
}

func (h *BudgetHandler) GetBudgetProfile(ctx context.Context, req *connect.Request[v1.GetBudgetProfileRequest]) (*connect.Response[v1.GetBudgetProfileResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	profile, svcErr := h.profiles.Get(ctx, id, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.GetBudgetProfileResponse{Profile: toProtoBudgetProfile(profile)}), nil
}

func (h *BudgetHandler) UpdateBudgetProfile(ctx context.Context, req *connect.Request[v1.UpdateBudgetProfileRequest]) (*connect.Response[v1.UpdateBudgetProfileResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	cycle := cycleStringFromProto(req.Msg.Cycle)
	profile, svcErr := h.profiles.Update(ctx, id, userID, req.Msg.Name, cycle)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateBudgetProfileResponse{Profile: toProtoBudgetProfile(profile)}), nil
}

func (h *BudgetHandler) DeleteBudgetProfile(ctx context.Context, req *connect.Request[v1.DeleteBudgetProfileRequest]) (*connect.Response[v1.DeleteBudgetProfileResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.profiles.Delete(ctx, id, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteBudgetProfileResponse{}), nil
}

// ── Period ────────────────────────────────────────────────────────────────────

func (h *BudgetHandler) CreateBudgetPeriod(ctx context.Context, req *connect.Request[v1.CreateBudgetPeriodRequest]) (*connect.Response[v1.CreateBudgetPeriodResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	period, svcErr := h.profiles.CreateBudgetPeriod(ctx, profileID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.CreateBudgetPeriodResponse{Period: toProtoBudgetPeriod(period)}), nil
}

func (h *BudgetHandler) ListBudgetPeriods(ctx context.Context, req *connect.Request[v1.ListBudgetPeriodsRequest]) (*connect.Response[v1.ListBudgetPeriodsResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	periods, svcErr := h.profiles.ListBudgetPeriods(ctx, profileID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.BudgetPeriod, len(periods))
	for i, p := range periods {
		protos[i] = toProtoBudgetPeriod(p)
	}
	return connect.NewResponse(&v1.ListBudgetPeriodsResponse{Periods: protos}), nil
}

func (h *BudgetHandler) GetBudgetPeriod(ctx context.Context, req *connect.Request[v1.GetBudgetPeriodRequest]) (*connect.Response[v1.GetBudgetPeriodResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	id, err := uuid.Parse(req.Msg.Id)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	period, svcErr := h.profiles.GetBudgetPeriod(ctx, id, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.GetBudgetPeriodResponse{Period: toProtoBudgetPeriod(period)}), nil
}

// ── People ────────────────────────────────────────────────────────────────────

func (h *BudgetHandler) AddBudgetPeople(ctx context.Context, req *connect.Request[v1.AddBudgetPeopleRequest]) (*connect.Response[v1.AddBudgetPeopleResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	people := make([]service.ProfilePersonInput, len(req.Msg.People))
	for i, p := range req.Msg.People {
		pi := service.ProfilePersonInput{UserName: p.UserName, Color: p.Color}
		if p.UserId != "" {
			uid, parseErr := uuid.Parse(p.UserId)
			if parseErr != nil {
				return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
			}
			pi.UserID = &uid
		}
		people[i] = pi
	}
	mappings, svcErr := h.profiles.AddPeople(ctx, profileID, userID, people)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.BudgetPerson, len(mappings))
	for i, m := range mappings {
		protos[i] = toProtoBudgetPerson(m)
	}
	return connect.NewResponse(&v1.AddBudgetPeopleResponse{People: protos}), nil
}

func (h *BudgetHandler) ListBudgetPeople(ctx context.Context, req *connect.Request[v1.ListBudgetPeopleRequest]) (*connect.Response[v1.ListBudgetPeopleResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	mappings, svcErr := h.profiles.ListPeople(ctx, profileID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.BudgetPerson, len(mappings))
	for i, m := range mappings {
		protos[i] = toProtoBudgetPerson(m)
	}
	return connect.NewResponse(&v1.ListBudgetPeopleResponse{People: protos}), nil
}

func (h *BudgetHandler) UpdateBudgetPerson(ctx context.Context, req *connect.Request[v1.UpdateBudgetPersonRequest]) (*connect.Response[v1.UpdateBudgetPersonResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	m, svcErr := h.profiles.UpdatePerson(ctx, profileID, int32(req.Msg.Id), req.Msg.Color, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateBudgetPersonResponse{Person: toProtoBudgetPerson(m)}), nil
}

func (h *BudgetHandler) RemoveBudgetPerson(ctx context.Context, req *connect.Request[v1.RemoveBudgetPersonRequest]) (*connect.Response[v1.RemoveBudgetPersonResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	var replacementPMID uuid.UUID
	if req.Msg.ReplacementPaymentMethodId != "" {
		parsed, parseErr := uuid.Parse(req.Msg.ReplacementPaymentMethodId)
		if parseErr != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, parseErr)
		}
		replacementPMID = parsed
	}
	svcErr := h.profiles.RemovePerson(ctx, profileID, int32(req.Msg.PersonId), int32(req.Msg.ReplacementPersonId), replacementPMID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.RemoveBudgetPersonResponse{}), nil
}

// ── Income Sources ────────────────────────────────────────────────────────────

func (h *BudgetHandler) AddIncomeSource(ctx context.Context, req *connect.Request[v1.AddIncomeSourceRequest]) (*connect.Response[v1.AddIncomeSourceResponse], error) {
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
	src, svcErr := h.profiles.AddIncomeSource(ctx, profileID, userID, service.IncomeSourceInput{
		Name:             req.Msg.Name,
		IncomeType:       incomeTypeStringFromProto(req.Msg.IncomeType),
		DefaultAmount:    numericFromMoney(req.Msg.DefaultAmount),
		Recurring:        req.Msg.Recurring,
		BudgetPersonID:   personID,
		PaymentFrequency: recurringTypeStringFromProto(req.Msg.PaymentFrequency),
		BeforeTax:        req.Msg.BeforeTax,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.AddIncomeSourceResponse{Source: toProtoIncomeSource(src)}), nil
}

func (h *BudgetHandler) ListIncomeSources(ctx context.Context, req *connect.Request[v1.ListIncomeSourcesRequest]) (*connect.Response[v1.ListIncomeSourcesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	sources, svcErr := h.profiles.ListIncomeSources(ctx, profileID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.IncomeSource, len(sources))
	for i, s := range sources {
		protos[i] = toProtoIncomeSource(s)
	}
	return connect.NewResponse(&v1.ListIncomeSourcesResponse{Sources: protos}), nil
}

func (h *BudgetHandler) UpdateIncomeSource(ctx context.Context, req *connect.Request[v1.UpdateIncomeSourceRequest]) (*connect.Response[v1.UpdateIncomeSourceResponse], error) {
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
	src, svcErr := h.profiles.UpdateIncomeSource(ctx, int32(req.Msg.Id), profileID, userID, service.IncomeSourceInput{
		Name:             req.Msg.Name,
		IncomeType:       incomeTypeStringFromProto(req.Msg.IncomeType),
		DefaultAmount:    numericFromMoney(req.Msg.DefaultAmount),
		Recurring:        req.Msg.Recurring,
		BudgetPersonID:   personID,
		PaymentFrequency: recurringTypeStringFromProto(req.Msg.PaymentFrequency),
		BeforeTax:        req.Msg.BeforeTax,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateIncomeSourceResponse{Source: toProtoIncomeSource(src)}), nil
}

func (h *BudgetHandler) DeleteIncomeSource(ctx context.Context, req *connect.Request[v1.DeleteIncomeSourceRequest]) (*connect.Response[v1.DeleteIncomeSourceResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.profiles.DeleteIncomeSource(ctx, int32(req.Msg.Id), profileID, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteIncomeSourceResponse{}), nil
}

// ── Savings Sources ───────────────────────────────────────────────────────────

func (h *BudgetHandler) AddSavingsSource(ctx context.Context, req *connect.Request[v1.AddSavingsSourceRequest]) (*connect.Response[v1.AddSavingsSourceResponse], error) {
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
	var pmID *uuid.UUID
	if req.Msg.PaymentMethodId != "" {
		parsed, err := uuid.Parse(req.Msg.PaymentMethodId)
		if err != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, err)
		}
		pmID = &parsed
	}
	src, svcErr := h.profiles.AddSavingsSource(ctx, profileID, userID, service.SavingsSourceInput{
		Name:            req.Msg.Name,
		Amount:          numericFromMoney(req.Msg.Amount),
		Frequency:       recurringTypeStringFromProto(req.Msg.Frequency),
		BudgetPersonID:  personID,
		PaymentMethodID: pmID,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.AddSavingsSourceResponse{Source: toProtoSavingsSource(src)}), nil
}

func (h *BudgetHandler) ListSavingsSources(ctx context.Context, req *connect.Request[v1.ListSavingsSourcesRequest]) (*connect.Response[v1.ListSavingsSourcesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	sources, svcErr := h.profiles.ListSavingsSources(ctx, profileID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.SavingsSource, len(sources))
	for i, s := range sources {
		protos[i] = toProtoSavingsSource(s)
	}
	return connect.NewResponse(&v1.ListSavingsSourcesResponse{Sources: protos}), nil
}

func (h *BudgetHandler) UpdateSavingsSource(ctx context.Context, req *connect.Request[v1.UpdateSavingsSourceRequest]) (*connect.Response[v1.UpdateSavingsSourceResponse], error) {
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
	var pmIDUpdate *uuid.UUID
	if req.Msg.PaymentMethodId != "" {
		parsed, err := uuid.Parse(req.Msg.PaymentMethodId)
		if err != nil {
			return nil, connect.NewError(connect.CodeInvalidArgument, err)
		}
		pmIDUpdate = &parsed
	}
	src, svcErr := h.profiles.UpdateSavingsSource(ctx, int32(req.Msg.Id), profileID, userID, service.SavingsSourceInput{
		Name:            req.Msg.Name,
		Amount:          numericFromMoney(req.Msg.Amount),
		Frequency:       recurringTypeStringFromProto(req.Msg.Frequency),
		BudgetPersonID:  personID,
		PaymentMethodID: pmIDUpdate,
	})
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateSavingsSourceResponse{Source: toProtoSavingsSource(src)}), nil
}

func (h *BudgetHandler) DeleteSavingsSource(ctx context.Context, req *connect.Request[v1.DeleteSavingsSourceRequest]) (*connect.Response[v1.DeleteSavingsSourceResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	profileID, err := uuid.Parse(req.Msg.BudgetProfileId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	if svcErr := h.profiles.DeleteSavingsSource(ctx, int32(req.Msg.Id), profileID, userID); svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.DeleteSavingsSourceResponse{}), nil
}

// ── Income Entries ────────────────────────────────────────────────────────────

func (h *BudgetHandler) ListIncomeEntries(ctx context.Context, req *connect.Request[v1.ListIncomeEntriesRequest]) (*connect.Response[v1.ListIncomeEntriesResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	periodID, err := uuid.Parse(req.Msg.BudgetPeriodId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	entries, svcErr := h.profiles.ListIncomeEntries(ctx, periodID, userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	protos := make([]*v1.IncomeEntry, len(entries))
	for i, e := range entries {
		protos[i] = toProtoIncomeEntry(e)
	}
	return connect.NewResponse(&v1.ListIncomeEntriesResponse{Entries: protos}), nil
}

func (h *BudgetHandler) UpdateIncomeEntry(ctx context.Context, req *connect.Request[v1.UpdateIncomeEntryRequest]) (*connect.Response[v1.UpdateIncomeEntryResponse], error) {
	userID, err := h.currentUserID(ctx)
	if err != nil {
		return nil, err
	}
	periodID, err := uuid.Parse(req.Msg.BudgetPeriodId)
	if err != nil {
		return nil, connect.NewError(connect.CodeInvalidArgument, err)
	}
	entry, svcErr := h.profiles.UpdateIncomeEntry(ctx, int32(req.Msg.Id), periodID, numericFromMoney(req.Msg.Amount), userID)
	if svcErr != nil {
		return nil, toConnectError(svcErr)
	}
	return connect.NewResponse(&v1.UpdateIncomeEntryResponse{Entry: toProtoIncomeEntry(entry)}), nil
}

// ── Conversion helpers ────────────────────────────────────────────────────────

func toProtoBudgetProfile(p db.BudgetProfile) *v1.BudgetProfile {
	return &v1.BudgetProfile{
		Id:          p.ID.String(),
		UserId:      p.UserID.String(),
		Name:        p.Name,
		Cycle:       protoCycleFromString(p.Cycle),
		CountryCode: nullStr(p.CountryCode),
	}
}

func toProtoBudgetPeriod(p db.BudgetPeriod) *v1.BudgetPeriod {
	return &v1.BudgetPeriod{
		Id:              p.ID.String(),
		BudgetProfileId: p.BudgetProfileID.String(),
		StartDate:       protoTSFromDate(p.StartDate),
		EndDate:         protoTSFromDate(p.EndDate),
		IsArchived:      p.IsArchived,
	}
}

func toProtoBudgetPerson(m db.BudgetToProfileMapping) *v1.BudgetPerson {
	return &v1.BudgetPerson{
		Id:              int64(m.ID),
		BudgetProfileId: m.BudgetProfileID.String(),
		UserName:        nullStr(m.UserName),
		UserId:          nullUUID(m.UserID),
		Color:           m.Color,
	}
}

func toProtoIncomeSource(s db.IncomeSource) *v1.IncomeSource {
	var personID int64
	if s.BudgetPersonID != nil {
		personID = int64(*s.BudgetPersonID)
	}
	return &v1.IncomeSource{
		Id:               int64(s.ID),
		BudgetProfileId:  s.BudgetProfileID.String(),
		Name:             s.Name,
		IncomeType:       protoIncomeTypeFromString(s.IncomeType),
		DefaultAmount:    moneyFromNumeric(s.DefaultAmount),
		Recurring:        s.Recurring,
		BudgetPersonId:   personID,
		PaymentFrequency: protoRecurringTypeFromString(s.PaymentFrequency),
		BeforeTax:        s.BeforeTax,
	}
}

func toProtoSavingsSource(s db.SavingsSource) *v1.SavingsSource {
	var personID int64
	if s.BudgetPersonID != nil {
		personID = int64(*s.BudgetPersonID)
	}
	var federalAmount, stateAmount *v1.Money
	if s.FederalAmount.Valid {
		federalAmount = moneyFromNumeric(s.FederalAmount)
	}
	if s.StateAmount.Valid {
		stateAmount = moneyFromNumeric(s.StateAmount)
	}
	var pmID string
	if s.PaymentMethodID != nil {
		pmID = s.PaymentMethodID.String()
	}
	return &v1.SavingsSource{
		Id:              int64(s.ID),
		BudgetProfileId: s.BudgetProfileID.String(),
		Name:            s.Name,
		Amount:          moneyFromNumeric(s.Amount),
		Frequency:       protoRecurringTypeFromString(s.Frequency),
		BudgetPersonId:  personID,
		IsTaxReserve:    s.IsTaxReserve,
		FederalAmount:   federalAmount,
		StateAmount:     stateAmount,
		PaymentMethodId: pmID,
	}
}

func toProtoIncomeEntry(e db.IncomeEntry) *v1.IncomeEntry {
	var sourceID, personID int64
	if e.IncomeSourceID != nil {
		sourceID = int64(*e.IncomeSourceID)
	}
	if e.BudgetPersonID != nil {
		personID = int64(*e.BudgetPersonID)
	}
	return &v1.IncomeEntry{
		Id:             int64(e.ID),
		BudgetPeriodId: e.BudgetPeriodID.String(),
		IncomeSourceId: sourceID,
		Name:           nullStr(e.Name),
		Amount:         moneyFromNumeric(e.Amount),
		BudgetPersonId: personID,
	}
}

// ── Enum helpers ──────────────────────────────────────────────────────────────

func cycleStringFromProto(c v1.BudgetCycle) string {
	switch c {
	case v1.BudgetCycle_BUDGET_CYCLE_WEEKLY:
		return "weekly"
	case v1.BudgetCycle_BUDGET_CYCLE_BI_WEEKLY:
		return "bi_weekly"
	case v1.BudgetCycle_BUDGET_CYCLE_YEARLY:
		return "yearly"
	default:
		return "monthly"
	}
}

func protoCycleFromString(s string) v1.BudgetCycle {
	switch s {
	case "weekly":
		return v1.BudgetCycle_BUDGET_CYCLE_WEEKLY
	case "bi_weekly":
		return v1.BudgetCycle_BUDGET_CYCLE_BI_WEEKLY
	case "yearly":
		return v1.BudgetCycle_BUDGET_CYCLE_YEARLY
	default:
		return v1.BudgetCycle_BUDGET_CYCLE_MONTHLY
	}
}

func incomeTypeStringFromProto(t v1.IncomeType) string {
	switch t {
	case v1.IncomeType_INCOME_TYPE_SALARY:
		return "salary"
	case v1.IncomeType_INCOME_TYPE_HOURLY:
		return "hourly"
	case v1.IncomeType_INCOME_TYPE_FREELANCE:
		return "freelance"
	case v1.IncomeType_INCOME_TYPE_CONTRACTOR:
		return "contractor"
	case v1.IncomeType_INCOME_TYPE_INVESTMENT:
		return "investment"
	default:
		return "other"
	}
}

func protoIncomeTypeFromString(s string) v1.IncomeType {
	switch s {
	case "salary":
		return v1.IncomeType_INCOME_TYPE_SALARY
	case "hourly":
		return v1.IncomeType_INCOME_TYPE_HOURLY
	case "freelance":
		return v1.IncomeType_INCOME_TYPE_FREELANCE
	case "contractor":
		return v1.IncomeType_INCOME_TYPE_CONTRACTOR
	case "investment":
		return v1.IncomeType_INCOME_TYPE_INVESTMENT
	case "interest":
		return v1.IncomeType_INCOME_TYPE_INTEREST
	case "one_time":
		return v1.IncomeType_INCOME_TYPE_ONE_TIME
	case "gift":
		return v1.IncomeType_INCOME_TYPE_GIFT
	default:
		return v1.IncomeType_INCOME_TYPE_OTHER
	}
}

func recurringTypeStringFromProto(t v1.RecurringType) string {
	switch t {
	case v1.RecurringType_RECURRING_TYPE_WEEKLY:
		return "weekly"
	case v1.RecurringType_RECURRING_TYPE_BI_WEEKLY:
		return "bi_weekly"
	case v1.RecurringType_RECURRING_TYPE_YEARLY:
		return "yearly"
	case v1.RecurringType_RECURRING_TYPE_ONE_OFF:
		return "one_off"
	default:
		return "monthly"
	}
}

func protoRecurringTypeFromString(s string) v1.RecurringType {
	switch s {
	case "weekly":
		return v1.RecurringType_RECURRING_TYPE_WEEKLY
	case "bi_weekly":
		return v1.RecurringType_RECURRING_TYPE_BI_WEEKLY
	case "yearly":
		return v1.RecurringType_RECURRING_TYPE_YEARLY
	case "one_off":
		return v1.RecurringType_RECURRING_TYPE_ONE_OFF
	default:
		return v1.RecurringType_RECURRING_TYPE_MONTHLY
	}
}
