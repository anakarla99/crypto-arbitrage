# Crypto Arbitrage

Phase 2 backend scaffold for public market data and paper-trading preparation.

## Current increment

The service binds and validates the single initial market (`BTC-USDT`) and exposes liveness/readiness endpoints. It does not connect to exchanges, persist data, calculate spreads, or execute orders.

## Run

```powershell
dotnet run --project src/CryptoArbitrage.Service
```

Then request `/health/live` or `/health/ready` on the URL printed by ASP.NET Core.

## Local dependencies

`compose.yaml` defines Redis and PostgreSQL for later increments. They are not required for the current service and are not started automatically.
