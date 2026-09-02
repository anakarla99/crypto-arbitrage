# Phase B - Data Collection Contract

## Purpose

Collect versioned, reproducible **paper-market evidence** for the Phase A decision. Collection records validated exchange books and system-quality events. It does not create labels, train models, send orders, or select only favourable opportunities.

## Dataset layout

Each capture run writes immutable Parquet files and one JSON manifest under a date/run partition:

```text
data/
  raw/book_snapshots/date=YYYY-MM-DD/run_id=<uuid>/part-*.parquet
  raw/quality_events/date=YYYY-MM-DD/run_id=<uuid>/part-*.parquet
  curated/candidate_decisions/date=YYYY-MM-DD/hour=HH/run_id=<uuid>/part-*.parquet
  derived/candidate_outcomes/dataset_version=<id>/part-*.parquet
  manifests/run_id=<uuid>.json
```

Candidate decisions are immutable decision-time facts. Labels are created later in the separate `candidate_outcomes` table. Raw capture files and decision rows are never edited; a corrected interpretation creates a new derived dataset version.

## `book_snapshots` contract

Emit one row for each **valid** local-book update, capped by a configurable sampling policy that is recorded in the manifest. The initial policy is a maximum of 10 snapshots per second per `(venue, instrument)` and must not silently discard a quality event.

| Field | Type | Required | Meaning |
|---|---|---:|---|
| `schema_version` | string | yes | Initial value: `book-snapshot/v1` |
| `run_id` | UUID | yes | Immutable capture-run identifier |
| `received_at_utc` | timestamp UTC | yes | Local receipt time; the only time used for ML feature eligibility |
| `monotonic_ticks` | int64 | yes | In-process ordering/latency measurement |
| `exchange` | enum | yes | `BinanceSpot` or `CoinbaseAdvancedTrade` |
| `canonical_instrument` | string | yes | Initial value: `BTC-USDT` |
| `venue_symbol` | string | yes | `BTCUSDT` or `BTC-USDT` |
| `book_sequence` | int64 | yes | Last validated venue sequence/update ID |
| `book_age_ms` | int32 | yes | Age at capture; must meet freshness policy |
| `retained_depth` | int16 | yes | Number of retained levels per side |
| `best_bid_price_units` | int64 | yes | Fixed-point units; scale is recorded in manifest |
| `best_bid_quantity_units` | int64 | yes | Fixed-point units |
| `best_ask_price_units` | int64 | yes | Fixed-point units; scale is recorded in manifest |
| `best_ask_quantity_units` | int64 | yes | Fixed-point units |
| `bids` | list of `{price_units, quantity_units}` | yes | Retained depth, ordered best-to-worst |
| `asks` | list of `{price_units, quantity_units}` | yes | Retained depth, ordered best-to-worst |
| `source_event_at_utc` | timestamp UTC | no | Venue event time, diagnostic only |

Rows with invalid, stale, crossed, incomplete, or non-comparable books are excluded from `book_snapshots` and represented in `quality_events` instead.

## `candidate_decisions` contract

Emit one row per route at a deterministic **100 ms UTC sampler**, including candidates below the baseline threshold and rejected candidates. This prevents collecting only attractive spreads and makes model coverage measurable.

| Field | Required | Meaning |
|---|---:|---|
| `candidate_id`, `schema_version`, `run_id` | yes | Immutable identifiers; initial schema `candidate-decision/v1` |
| `decision_at_utc`, `decision_monotonic_ticks` | yes | The exact information cutoff `t` |
| `canonical_instrument`, `route_id`, `buy_exchange`, `sell_exchange` | yes | Route identity |
| `buy_snapshot_id`, `sell_snapshot_id` | conditional | Snapshots whose receipt times are `<= t`; null only with a rejection reason |
| `buy_final_sequence`, `sell_final_sequence`, `connection_epoch` | yes | Replay and continuity provenance |
| `buy_book_age_ms`, `sell_book_age_ms`, `inter_book_skew_ms` | yes | Decision-time quality features |
| `data_quality_state`, `rejection_reason` | yes | `eligible` or an explicit exclusion reason |
| `notional_quote_units`, `cost_model_version`, `feature_schema_version` | yes | Fixed 100-USDT contract and reproducibility identifiers |
| `baseline_net_edge_bps_units`, `baseline_selected` | yes | Deterministic comparator |
| typed feature columns | conditional | Only for eligible rows; never hide training features in opaque JSON |

Initial typed features are both-leg VWAP inputs, available depth, depth imbalance, gross/net edge, book age/skew, one-second update rate, one-second spread volatility, and processing latency. A feature-definition version records unit, window, and missingness semantics for each field.

## `candidate_outcomes` contract

This is a later immutable derivative, never an update to a decision row. It links `candidate_id` to the first complete, valid snapshot pair received at or after `decision_at_utc + 250 ms`.

Required fields: `outcome_id`, `candidate_id`, `labelled_at_utc`, `target_horizon_ms`, `label_status`, outcome snapshot IDs and receipt times, `cost_model_version`, simulated buy/sell VWAP, simulated net P&L in quote units and bps, `profitable_label`, `fill_status`, and `label_exclusion_reason` where applicable.

If future data is invalid, stale, disconnected, or lacks sufficient depth, write an excluded/unlabelled outcome. Never fabricate a negative label and never forward-fill across a gap.

## `quality_events` contract

Every exclusion and recovery event is durable evidence. Required fields: `schema_version`, `run_id`, `occurred_at_utc`, `exchange`, `canonical_instrument`, `event_type`, `reason`, `connection_generation`, `last_sequence`, `queue_depth`, `details_json`.

Initial `event_type` values: `connection_state_changed`, `sequence_gap`, `snapshot_mismatch`, `queue_overflow`, `parse_rejection`, `liveness_timeout`, `stale_book`, `sampling_drop`, `run_started`, and `run_stopped`.

## Manifest contract

The manifest is written once at run completion and includes:

- run start/end UTC, host/software/git revision, schema versions, and row counts;
- configured instrument/venue mappings, fixed-point scales, retained depth, sampling policy, freshness policy, and reconnect settings;
- source endpoints only (never credentials), parser/synchronizer versions, and checksums for every output file;
- quality-event counts, valid coverage by venue, gap/reconnect counts, and known limitations;
- a declaration that collection is public market data and paper-trading only.

## Collection workflow

1. Validate market configuration and fetch current venue metadata; fail closed on mismatch or disabled product. Record a sanitized configuration hash and fixed-point scales.
2. Start a capture run and write `run_started` before opening streams.
3. Synchronize each venue book; do not write market rows until it is valid and fresh.
4. Persist valid snapshots, deterministic 100-ms candidate decisions, and every quality event through independent bounded writers. A persistence backlog must produce a quality event; it must never block book synchronization.
5. Periodically flush durable Parquet parts, then write the completion manifest with checksums.
6. Validate the run offline: schema, ordering, no duplicate `(exchange, sequence)` records, non-crossed BBO, depth ordering, freshness, and manifest row/checksum agreement.

## Data quality gates

A run is eligible for Phase C only when:

- each used venue has at least 95% valid coverage over the recorded run duration;
- every retained market row is fresh (`<= 250 ms`) and sequence-consistent;
- every gap, reconnect, parse rejection, or sampling drop is represented in `quality_events`;
- no credentials or customer/personal data are present;
- raw records and manifest pass schema/checksum validation;
- collection includes a minimum 24-hour accepted run and at least three distinct UTC sessions/regimes, documented rather than inferred;
- a sampled replay reproduces candidate validity, source sequences, and typed features exactly.

## Reproducibility and retention

Store all amounts as fixed-point integers, preserve the schema/cost-model version in derived data, and retain raw capture plus manifest for the course project duration. A Python data-access package reads Parquet and rejects unknown schema versions. Dataset changes are additive/versioned; no historical file is overwritten.

## Phase B exit criteria

Phase B is complete when a new machine can validate a capture manifest, read the raw data with the documented schema, reproduce coverage/quality statistics, and generate a temporally ordered input table for Phase C without accessing live exchanges.
