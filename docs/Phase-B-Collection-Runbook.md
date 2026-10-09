# Phase B - Collection Runbook

## Before collection

1. Create a new `run_id`, dataset version, and empty output directory.
2. Validate configuration, fixed-point precision, endpoint mappings, and current product metadata.
3. Record collector Git revision, runtime, schema versions, cost-model version, retained depth, sampler policy, and a sanitized configuration hash.
4. Check local clock synchronization, disk headroom, and writer quota. Do not record hostname, IP, credentials, headers, or raw signed URLs.
5. Run a five-minute dry run. Confirm both books reach `Valid`, no queue overflow occurs, and manifest preview checks pass.

## During collection

- Begin the accepted window only after both venue books have completed synchronization.
- Keep one connection epoch per reconnect and log every lifecycle transition.
- Write book snapshots only for valid/fresh/non-crossed state; emit a quality event for every exclusion.
- Sample both decision routes every 100 ms whether their deterministic edge is positive or negative.
- Monitor valid coverage, freshness, update rate, gaps, reconnects, parser rejects, queue depth, candidate rate, insufficient-depth exclusions, and disk headroom.
- A restart starts a new epoch/run segment; it must never silently fill a missing interval.

## Completing a run

1. Gracefully stop collection and flush all writers.
2. Write checksums and row/min/max-time counts to the manifest.
3. Validate Parquet/schema readability, ordered depth, non-crossed BBO, sequence/ID uniqueness, and candidate-to-snapshot references.
4. Produce a quality report with valid coverage, exclusion categories, gap duration, label-ready candidate count, and per-venue health.
5. Mark the run `accepted`, `failed`, or `cancelled`. Failed runs remain immutable evidence and are not silently deleted.

## Acceptance thresholds

- 24 hours minimum continuous accepted collection; seven days is the target for model development.
- 100% manifest checksums/schema validation.
- 0 candidate rows accepted while a source book is invalid/stale/crossed.
- 0 unexplained sequence gaps, overflows, or missing lifecycle intervals.
- 99.5% of elapsed time is valid-candidate eligible or explicitly categorized by a quality event.
- 100 sampled decision rows reproduce exactly from stored source snapshots.

## Current implementation gaps

The Binance synchronizer now exports an immutable, ordered retained-depth snapshot with the last applied receipt timestamp, sequence, and connection epoch. The Coinbase Level 2 parser now normalizes snapshot/update messages into the same transport-neutral `BookDelta` contract and enforces the configured product and fixed-point precision. These are the capture inputs; `BookView` remains the smaller BBO-only eligibility view.

Before running this collector, the backend still needs the Coinbase connection adapter, Binance/Coinbase stream wiring, durable Parquet writers, a capture coordinator/sampler, and an offline replay validator.
