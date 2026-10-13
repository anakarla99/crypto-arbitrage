# Crypto Arbitrage

**A safety-first .NET 10 backend for cross-exchange market-data normalization and paper-trading preparation.**

This repository demonstrates the foundations of an arbitrage platform: validated fixed-point market contracts, canonical instrument mapping, exchange-book lifecycle handling, freshness gates, and an executable spread-evaluation vertical slice. It is intentionally not an order-execution bot.

## Current implementation

- Configures and validates the initial `BTC-USDT` market across Binance Spot and Coinbase Advanced Trade.
- Models order-book updates and synchronized snapshots without floating-point prices or quantities.
- Handles WebSocket lifecycle and exchange-specific depth synchronization in infrastructure adapters.
- Rejects stale, invalid, crossed, or non-comparable books before opportunity evaluation.
- Exposes `POST /api/opportunities/evaluate`, which accepts normalized top-of-book values, calculates a gross spread, persists the latest result in an in-memory store, and returns the API representation.
- Exposes `GET /api/opportunities/latest` to read the persisted result.
- Keeps execution disabled; the current persistence store is deliberately in-memory until a durable event/data model is introduced.

## Run

```powershell
dotnet run --project src/CryptoArbitrage.Service
```

Then request `/health/live` or `/health/ready` on the URL printed by ASP.NET Core. Evaluate a paper-only opportunity:

```powershell
Invoke-RestMethod http://localhost:5000/api/opportunities/evaluate -Method Post -ContentType 'application/json' -Body '{"buyAskPrice":"65000","buyAskQuantity":"0.25","sellBidPrice":"65026","sellBidQuantity":"0.10"}'
Invoke-RestMethod http://localhost:5000/api/opportunities/latest
```

The evaluation endpoint is a deterministic integration seam for the next increment; it does not claim live exchange connectivity or place orders.

## Architecture and roadmap

The solution is split into Domain, Infrastructure, and Service projects. Start with [Domain-Contracts.md](docs/Domain-Contracts.md), then see [Backend-Setup.md](docs/Backend-Setup.md), [Phase-B-Data-Collection-Contract.md](docs/Phase-B-Data-Collection-Contract.md), and the [Industrial ML roadmap](docs/Industrial-ML-Roadmap.md).

## Local dependencies

`compose.yaml` defines Redis and PostgreSQL for later durable collection/persistence increments. They are not required for the current service and are not started automatically.
