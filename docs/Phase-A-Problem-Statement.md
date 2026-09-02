# Phase A - Business and ML Problem Statement

## Product and decision

**Product:** Predictive Cross-Exchange Opportunity Filter - paper-trading decision support.

**Decision owner:** a paper-trading analyst. The system assists the analyst; it does not provide investment advice, hold funds, or send real orders.

**Decision:** for each eligible, synchronized `BTC-USDT` snapshot and each route (`buy Binance/sell Coinbase` or the reverse), decide whether to select a fixed-notional paper-trade candidate.

**Business problem:** deterministic after-cost spread thresholds generate false positives: the price difference may disappear before both simulated legs fill. The product should select fewer, higher-quality candidates and improve after-cost paper P&L.

## Mathematical formulation

At decision time `t`, let `x(t)` contain only validated information received by `t`: best prices, retained depth, depth imbalance, net spread, volatility proxies, update rate, book age, and processing latency.

For each route, simulate both legs at the first complete, valid books received at or after `t + E`, where the initial decision-to-fill delay is **E = 250 ms**. The primary classification label is:

```text
y(t) = 1  if simulatedNetPnl(t + E, route, q) > 0
        0  otherwise
```

`q` is an initial fixed **100 USDT** paper notional. The regression target is `simulatedNetPnlBps(t + E, route, q)`. A one-second post-decision outcome is a secondary persistence/sensitivity analysis, not the primary label.

The first model estimates `P(y(t)=1 | x(t))` and optionally expected net P&L. A candidate is selected only when data-quality gates pass and the prediction satisfies the approved decision policy.

## Scope and assumptions

| In scope | Explicitly out of scope |
|---|---|
| Binance Spot and Coinbase Advanced Trade public `BTC-USDT` data | Live execution, API trading keys, custody, or investment advice |
| Both route directions, fixed 100 USDT paper notional | USD/USDT conversion, multiple pairs, transfers, or withdrawals |
| Depth/VWAP paper fills, fee/slippage/delay sensitivity | Claims of guaranteed arbitrage or live profitability |
| Offline replay, monitored inference, Streamlit demo | Training on data unavailable at the decision time |

The paper model assumes pre-funded balances on both venues. Transfer and withdrawal costs are therefore excluded, and this limitation must be prominent in the final demo.

## Baseline, costs, and policy

The deterministic baseline selects an eligible route only when its depth-aware net edge at `t` exceeds **5 bps**. It uses exactly the same notional, freshness, cost model, and route rules as the ML policy.

Until documented account-tier schedules are collected, the conservative initial paper cost model is:

- 10 bps taker fee per venue (20 bps total);
- depth-derived VWAP for each leg at `q`;
- 2 bps adverse-fill haircut per leg;
- 250 ms decision-to-fill delay;
- reject candidates with invalid/stale/crossed books or insufficient retained depth.

These are configurable assumptions, not claims about current exchange fees. Phase C must test sensitivity to worse fee, slippage, and delay scenarios.

## Quality and economic success criteria

Use chronological train/validation/test partitions. The test period is locked before model selection.

| Category | Measure | Pre-registered acceptance requirement |
|---|---|---|
| Economic | Cumulative and mean after-cost paper P&L | Incremental mean P&L over baseline has bootstrap 95% CI lower bound above zero |
| Decision quality | Precision among selected candidates | At least 5 percentage points above baseline |
| Coverage | Selected-candidate rate | At least 25% of baseline coverage |
| Model quality | PR-AUC and Brier score/calibration | Beat a prevalence-only predictor; report calibration plot |
| Risk | Max drawdown, turnover, regime slices | No unacceptable degradation in any pre-defined regime slice |

The selection threshold is chosen only on validation data by expected value. Accuracy alone is not a success measure.

## Data and leakage contract

Every stored candidate must include snapshot identifiers, venue sequences, receipt timestamps, feature-schema version, route, notional, data-quality state, and cost-model version.

Features may use only values received no later than `t`. Labels use the first valid, complete books received at or after `t + E`. Do not forward-fill across a disconnect or sequence gap. Use chronological splits with an embargo of at least `E` plus the secondary-label horizon; fit transformations and choose thresholds on training/validation data only.

## Risks and controls

| Risk | Control |
|---|---|
| Look-ahead leakage | Receipt-time feature cutoff, temporal split, embargo, locked test set |
| Paper-fill bias | Depth-aware VWAP, conservative cost scenarios, documented limitations |
| Market non-stationarity | Time-based evaluation, regime slices, drift and delayed-P&L monitoring |
| Bad exchange data | Sequence/freshness validity gates and exclusion logs |
| Class imbalance / selection bias | Report prevalence, PR-AUC, coverage, and collection-regime coverage |
| Threshold overfitting | Threshold selected once on validation; final test untouched |

## Phase A exit criteria

Phase A is complete when this statement, the cost model, the label definition, the baseline, and the acceptance criteria are approved and used unchanged to guide dataset collection. Any later change requires a versioned amendment explaining why it was necessary.
