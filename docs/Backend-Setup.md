# Phase 2, Step 1 — Backend Setup and Configuration

## Completed scope

- Isolated `net10.0` solution with a domain project and ASP.NET Core service.
- Typed `MarketDataOptions`, bound from `appsettings.json` and validated at startup.
- BTC-USDT baseline configuration for Binance Spot and Coinbase Advanced Trade.
- Liveness (`/health/live`) and configuration-backed readiness (`/health/ready`) endpoints.
- Local Redis/PostgreSQL Compose definitions for future increments; neither dependency is used yet.
- Secret-safe repository defaults: no credentials in application settings; `.env` is ignored.

## Startup validation rules

The Phase 2 MVP permits exactly one market and requires:

- canonical symbol, base/quote assets, Binance symbol, and Coinbase product ID to agree (`BTC-USDT`, `BTCUSDT`, `BTC-USDT`);
- secure, absolute WebSocket/REST base URLs with no user info, query, or fragment;
- a supported Binance snapshot depth; retained depth no greater than snapshot depth;
- freshness and queue capacity within bounded ranges;
- bounded reconnect delay, multiplier, jitter, and attempt settings.

`ValidateOnStart` makes configuration failure terminate startup before any hosted market-data service can be added.

## Verification completed

1. `dotnet build CryptoArbitrage.slnx --no-restore` completes with zero warnings/errors.
2. With normal configuration, `/health/live` returns `live` and `/health/ready` returns `BTC-USDT` with execution disabled.
3. With `MarketData__Book__RetainedDepth=600`, startup fails with the expected retained-depth validation error.

## Intentionally deferred

- Runtime exchange-product metadata check: implemented with the Binance/Coinbase adapters, so it can use a mockable HTTP boundary.
- Redis/PostgreSQL clients and migrations.
- WebSocket connection, order-book state, spread calculation, frontend, and all execution behavior.
