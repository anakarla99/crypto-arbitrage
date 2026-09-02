# Phase 2, Step 2 — Normalized Domain Contracts

## Completed scope

- Fixed-point `long` units for prices and quantities; no floating-point or decimal value enters an order-book contract.
- Immutable canonical IDs, venue instruments, and an instrument registry with exact reverse lookups.
- Initial per-venue precision is configuration-driven: price scale 8/tick `0.01`, Binance quantity increment `0.00001000`, Coinbase quantity increment `0.00000001`.
- Transport-neutral `BookDelta`, `BookView`, sequence range, status, invalidation-reason, and opportunity-eligibility contracts.
- Strict direct comparability: only the exact same canonical base/quote pair is comparable. `BTC-USD` and `BTC-USDT` reject until a future FX feature exists.

## Boundary rules

1. A future exchange adapter parses raw strings with the resolved `VenueInstrument`; unrecognized symbols, excess non-zero precision, and tick/lot misalignment fail closed.
2. A valid `BookView` requires positive, non-crossed best bid/ask. Non-valid states cannot publish BBO values.
3. Eligibility accepts only fresh valid books for the same canonical instrument. It is a gate only; Step 2 does not calculate a spread or take action.
4. The service constructs the registry at startup from validated configuration, so mapping/precision mistakes fail before adapters are introduced.

## Verification

`dotnet test CryptoArbitrage.slnx` passes eight tests covering exact parsing, increment enforcement, direct comparability, strict lookup, duplicate mapping rejection, and freshness eligibility. A full solution build completes with zero warnings/errors.

## Deferred

- Runtime refresh of venue tick/lot/product rules (implemented alongside HTTP/WebSocket adapters).
- Mutable synchronized order-book implementation and sequence application.
- Spread mathematics, telemetry, persistence, UI, and all execution behavior.
