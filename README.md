# Banking-Grade Platform (Master-grade Portfolio) — .NET 10 (C#)

Backend-only demo inspired by large banking backends.

## Highlights
- **Gateway**: HMAC signing + replay protection + idempotency + Redis rate limiting
- **IAM**: JWT + RBAC roles
- **Accounts**: balances + holds
- **Ledger**: double-entry posting
- **Payments**: event-driven + (demo-grade) posting pipeline
- **Fraud Scoring**: rules + alerts
- **Alerts**: consumer + API
- **Notifications / Reporting**: workers for side effects + projections
- **Audit hash-chain** + verify endpoint

> Synthetic data only. No real bank integrations.

## Run
```bash
docker compose up --build
```

Rabbit UI: http://localhost:15672 (guest/guest)

## Signed request to Gateway
```bash
node scripts/sign-request.js --url http://localhost:8080/v1/transactions --clientId demo-client --secret demo-secret \
  --body '{"userId":"usr_demo","deviceId":"dev_new","amount":12500,"currency":"TRY","country":"TR","channel":"MOBILE","merchantCategory":"5999"}' | bash
```


## Observability
```bash
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build
```
Grafana: http://localhost:3000
Tempo: http://localhost:3200

## Retry/DLQ
Consumers use exponential backoff retry queues and DLQ.
DLQ replay:
```bash
dotnet run --project src/Tools/DlqReplayer/DlqReplayer.csproj -- amqp://guest:guest@localhost:5672 alerts.q --rk alert.created
```
