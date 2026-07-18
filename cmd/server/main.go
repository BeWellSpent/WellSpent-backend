package main

import (
	"context"
	"fmt"
	"log"
	"net/http"

	"connectrpc.com/connect"
	"github.com/rs/cors"
	"golang.org/x/net/http2"
	"golang.org/x/net/http2/h2c"

	"github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1/wellspentv1connect"
	"github.com/BeWellSpent/wellspent-backend/internal/auth"
	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/db"
	"github.com/BeWellSpent/wellspent-backend/internal/handler"
	"github.com/BeWellSpent/wellspent-backend/internal/middleware"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	sqlcdb "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"go.uber.org/zap"
)

func main() {
	cfg, err := config.Load()
	if err != nil {
		log.Fatal(err)
	}

	var logger *zap.Logger
	if cfg.Debug {
		logger, _ = zap.NewDevelopment()
	} else {
		logger, _ = zap.NewProduction()
	}
	defer logger.Sync() //nolint:errcheck

	ctx := context.Background()
	pool, err := db.NewPool(ctx, cfg.DatabaseURL)
	if err != nil {
		log.Fatal(err)
	}
	defer pool.Close()

	queries := sqlcdb.New(pool)

	// Repositories
	userRepo := repository.NewUserRepository(queries)
	budgetProfileRepo := repository.NewBudgetProfileRepository(queries)
	transactionRepo := repository.NewTransactionRepository(queries)
	allocationRepo := repository.NewExpenseAllocationRepository(queries)
	fixedExpenseRepo := repository.NewFixedExpenseRepository(queries)
	inviteRepo := repository.NewInviteRepository(queries)
	plaidRepo := repository.NewPlaidRepository(queries)
	reviewRepo := repository.NewTransactionReviewRepository(queries)

	// Auth
	jwtSvc := auth.NewJWTService(cfg.JWTSecret)
	googleOAuth := auth.NewGoogleOAuth(cfg.GoogleClientID, cfg.GoogleClientSecret, cfg.GoogleRedirectURI)

	// Services
	authSvc := service.NewAuthService(userRepo, jwtSvc, googleOAuth, cfg, logger)
	userSvc := service.NewUserService(userRepo)
	profileSvc := service.NewBudgetProfileService(budgetProfileRepo, transactionRepo, fixedExpenseRepo, userRepo)
	transactionSvc := service.NewTransactionService(transactionRepo, budgetProfileRepo, allocationRepo, fixedExpenseRepo, reviewRepo)
	allocationSvc := service.NewExpenseAllocationService(allocationRepo, budgetProfileRepo)
	inviteSvc := service.NewInviteService(inviteRepo, budgetProfileRepo, userRepo, cfg, logger)

	var plaidSvc *service.PlaidService
	if cfg.PlaidClientID != "" && cfg.PlaidSecret != "" {
		pc, pcErr := plaidclient.New(cfg.PlaidClientID, cfg.PlaidSecret, cfg.PlaidEnv, plaidclient.Options{
			Logger:          logger,
			RedactSensitive: cfg.PlaidLogRedactSensitive,
			MaxRetries:      cfg.PlaidHTTPMaxRetries,
			RetryDelay:      cfg.PlaidHTTPRetryDelay,
		})
		if pcErr != nil {
			log.Fatalf("plaid: init client: %v", pcErr)
		}
		plaidSvc = service.NewPlaidService(pc, plaidRepo, budgetProfileRepo, userRepo, transactionRepo, fixedExpenseRepo, reviewRepo, cfg.EncryptionKey)
	}

	// Procedures that don't require authentication
	bypass := map[string]bool{
		wellspentv1connect.AuthServiceRegisterProcedure:                true,
		wellspentv1connect.AuthServiceLoginProcedure:                   true,
		wellspentv1connect.AuthServiceGetGoogleAuthURLProcedure:        true,
		wellspentv1connect.AuthServiceExchangeGoogleCodeProcedure:      true,
		wellspentv1connect.AuthServiceVerifyEmailProcedure:             true,
		wellspentv1connect.AuthServiceResendVerificationEmailProcedure: true,
		wellspentv1connect.UserServiceListCountriesProcedure:           true,
		wellspentv1connect.InviteServiceGetBudgetInviteProcedure:       true,
	}

	interceptors := connect.WithInterceptors(
		middleware.NewAuthInterceptor(jwtSvc, bypass),
		middleware.NewLoggingInterceptor(logger),
	)

	mux := http.NewServeMux()
	mux.Handle(wellspentv1connect.NewAuthServiceHandler(handler.NewAuthHandler(authSvc), interceptors))
	mux.Handle(wellspentv1connect.NewUserServiceHandler(handler.NewUserHandler(userSvc), interceptors))
	mux.Handle(wellspentv1connect.NewBudgetServiceHandler(handler.NewBudgetHandler(profileSvc, transactionSvc, allocationSvc), interceptors))
	mux.Handle(wellspentv1connect.NewInviteServiceHandler(handler.NewInviteHandler(inviteSvc), interceptors))
	if plaidSvc != nil {
		mux.Handle(wellspentv1connect.NewPlaidServiceHandler(handler.NewPlaidHandler(plaidSvc), interceptors))
	}

	corsHandler := cors.New(cors.Options{
		AllowedOrigins:   cfg.CORSAllowedOrigins,
		AllowedMethods:   []string{"GET", "POST", "PUT", "DELETE", "OPTIONS"},
		AllowedHeaders:   []string{"Authorization", "Content-Type", "Connect-Protocol-Version", "Connect-Timeout-Ms"},
		AllowCredentials: true,
	})

	addr := fmt.Sprintf(":%s", cfg.ServerPort)
	logger.Info("starting server", zap.String("addr", addr), zap.String("env", cfg.Env))
	log.Fatal(http.ListenAndServe(addr, h2c.NewHandler(corsHandler.Handler(mux), &http2.Server{})))
}
