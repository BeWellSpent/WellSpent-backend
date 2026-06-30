package main

import (
	"context"
	"database/sql"
	"fmt"
	"log"
	"os"
	"strings"

	"github.com/joho/godotenv"
	"github.com/pressly/goose/v3"
	_ "github.com/jackc/pgx/v5/stdlib"
)

func main() {
	cmd := "up"
	env := "dev"
	if len(os.Args) > 1 {
		cmd = os.Args[1]
	}
	if len(os.Args) > 2 {
		env = os.Args[2]
	}

	_ = godotenv.Load(fmt.Sprintf(".env.%s", env))

	dsn := strings.TrimSpace(os.Getenv("DATABASE_URL"))
	if dsn == "" {
		log.Fatal("DATABASE_URL is required")
	}

	db, err := sql.Open("pgx", dsn)
	if err != nil {
		log.Fatalf("open db: %v", err)
	}
	defer db.Close()

	if err := db.PingContext(context.Background()); err != nil {
		log.Fatalf("ping db: %v", err)
	}

	if err := goose.SetDialect("postgres"); err != nil {
		log.Fatalf("set dialect: %v", err)
	}

	dir := "internal/db/migrations"

	switch cmd {
	case "up":
		if err := goose.Up(db, dir); err != nil {
			log.Fatalf("migrate up: %v", err)
		}
	case "down":
		if err := goose.Down(db, dir); err != nil {
			log.Fatalf("migrate down: %v", err)
		}
	default:
		log.Fatalf("unknown command %q — use 'up' or 'down'", cmd)
	}
}
