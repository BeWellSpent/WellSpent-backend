# ── Build stage ───────────────────────────────────────────────────────────────
FROM golang:1.25-alpine AS builder

WORKDIR /app

RUN apk add --no-cache git

COPY go.mod go.sum ./
RUN go mod download

COPY . .
RUN go build -ldflags="-w -s" -o /bin/server ./cmd/server
RUN go build -ldflags="-w -s" -o /bin/cycle-budgets ./cmd/jobs/cycle-budgets
RUN go build -ldflags="-w -s" -o /bin/plaid-sync ./cmd/jobs/plaid-sync

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM alpine:3.20

RUN apk add --no-cache ca-certificates tzdata

WORKDIR /app

COPY --from=builder /bin/server ./server
COPY --from=builder /bin/cycle-budgets ./cycle-budgets
COPY --from=builder /bin/plaid-sync ./plaid-sync

EXPOSE 8080

ENTRYPOINT ["./server"]
