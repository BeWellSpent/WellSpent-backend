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

	"github.com/mauro-afa91/spendsense/gen/spendsense/v1/spendsensev1connect"
	"github.com/mauro-afa91/spendsense/internal/auth"
	"github.com/mauro-afa91/spendsense/internal/config"
	"github.com/mauro-afa91/spendsense/internal/db"
	"github.com/mauro-afa91/spendsense/internal/handler"
	"github.com/mauro-afa91/spendsense/internal/middleware"
	"github.com/mauro-afa91/spendsense/internal/repository"
	"github.com/mauro-afa91/spendsense/internal/service"
	sqlcdb "github.com/mauro-afa91/spendsense/internal/sqlc"
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
	userRepo        := repository.NewUserRepository(queries)
	budgetRepo      := repository.NewBudgetRepository(queries)
	transactionRepo := repository.NewTransactionRepository(queries)

	// Auth
	jwtSvc     := auth.NewJWTService(cfg.JWTSecret, cfg.JWTLifetimeSeconds)
	googleOAuth := auth.NewGoogleOAuth(cfg.GoogleClientID, cfg.GoogleClientSecret, "")

	// Services
	authSvc         := service.NewAuthService(userRepo, jwtSvc, googleOAuth)
	userSvc         := service.NewUserService(userRepo)
	budgetSvc       := service.NewBudgetService(budgetRepo)
	transactionSvc  := service.NewTransactionService(transactionRepo, budgetRepo)

	// Procedures that don't require authentication
	bypass := map[string]bool{
		spendsensev1connect.AuthServiceLoginProcedure:              true,
		spendsensev1connect.AuthServiceGetGoogleAuthURLProcedure:   true,
		spendsensev1connect.AuthServiceExchangeGoogleCodeProcedure: true,
	}

	interceptors := connect.WithInterceptors(
		middleware.NewAuthInterceptor(jwtSvc, bypass),
		middleware.NewLoggingInterceptor(logger),
	)

	mux := http.NewServeMux()
	mux.Handle(spendsensev1connect.NewAuthServiceHandler(handler.NewAuthHandler(authSvc), interceptors))
	mux.Handle(spendsensev1connect.NewUserServiceHandler(handler.NewUserHandler(userSvc), interceptors))
	mux.Handle(spendsensev1connect.NewBudgetServiceHandler(handler.NewBudgetHandler(budgetSvc, transactionSvc), interceptors))

	corsHandler := cors.New(cors.Options{
		AllowedOrigins:   []string{"http://localhost:5173", "http://localhost:3000"},
		AllowedMethods:   []string{"GET", "POST", "PUT", "DELETE", "OPTIONS"},
		AllowedHeaders:   []string{"Authorization", "Content-Type", "Connect-Protocol-Version", "Connect-Timeout-Ms"},
		AllowCredentials: true,
	})

	addr := fmt.Sprintf(":%s", cfg.ServerPort)
	logger.Info("starting server", zap.String("addr", addr), zap.String("env", cfg.Env))
	log.Fatal(http.ListenAndServe(addr, h2c.NewHandler(corsHandler.Handler(mux), &http2.Server{})))
}
