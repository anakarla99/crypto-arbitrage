# Industrial ML Product Roadmap

## Product definition

**Working title:** Predictive Cross-Exchange Opportunity Filter

**Business problem:** a deterministic top-of-book spread threshold creates many paper-trade signals that become unprofitable after fees, slippage, and delay. The product should reduce those false-positive decisions.

**ML decision:** for each valid cross-exchange snapshot, estimate the probability that a paper trade at a defined notional is net-profitable after a fixed horizon. The service may select a signal only when the predicted probability and expected value pass an explicit policy. It never sends real orders.

**Initial target:** binary label `simulated_net_pnl(t + 250ms) > 0`, with a secondary regression target for net P&L in basis points. A one-second outcome is retained as a secondary persistence/sensitivity analysis, not the primary fill label.

## What changes from the original roadmap

The streaming .NET system remains valuable, but it becomes an **evidence and inference layer**. Building more exchanges, a rich React dashboard, or a paper-execution engine is not the next priority until the data proves that the ML decision is useful.

Completed work is retained as the foundation:

- safe market configuration and exact numeric contracts;
- connection-lifecycle and local-book synchronization foundations;
- Binance snapshot/sequence correctness tests;
- strict no-action behavior for invalid or stale books.

## Sequential delivery phases

### Phase A - Business and ML problem statement

**Goal:** produce the first assessed course artifact before collecting data.

- Define the decision owner as a paper-trading analyst.
- Specify the unit of decision: one valid, synchronized Binance/Coinbase snapshot for `BTC-USDT` at a fixed notional.
- Define costs: both venue taker fees, depth-derived slippage, and a conservative execution-delay assumption.
- Define non-goals: no real orders, no claims of guaranteed arbitrage, no comparison of mismatched quote currencies.
- Establish success criteria: improve out-of-sample expected paper P&L and precision of profitable signals against the deterministic threshold baseline.

**Deliverables:** one-page problem statement, decision-policy diagram, data dictionary, risks/assumptions register.

**Exit gate:** an evaluator can state the business decision, label, baseline, and success metric without reading code.

### Phase B - Data collection and reproducible dataset

**Goal:** collect trustworthy examples before choosing a model.

- Finish the Binance JSON/REST adapter and add the matching Coinbase adapter.
- Persist synchronized, valid snapshots and lifecycle/quality events with a schema version.
- Store raw derived features and later label inputs; do not store secrets or personal data.
- Capture data across multiple market regimes and document coverage, outages, gaps, and exclusions.
- Create a reproducible Python data-access layer and data-quality checks.

**Deliverables:** versioned parquet dataset, collection runbook, schema, data-quality report.

**Exit gate:** a notebook can reproduce the sample count, missingness, freshness, and sequence-gap statistics from raw stored data.

### Phase C - Exploratory analysis and label validation

**Goal:** show that there is a learnable, economically meaningful problem.

- Analyze spread lifetime, liquidity, volatility, depth imbalance, latency, and post-signal P&L.
- Validate label timing with strictly future market data; prevent look-ahead leakage.
- Compare several horizons/notionals and select one with a defensible business rationale.
- Identify class imbalance and define temporal train/validation/test partitions.

**Deliverables:** EDA notebook/report, final label specification, feature list, temporal-split diagram.

**Exit gate:** data and label quality are approved; the selected target is not based on future information.

### Phase D - Baseline and experiment design

**Goal:** make the ML claim falsifiable.

- Implement the deterministic baseline: current net-spread threshold plus data-validity checks.
- Define offline metrics: precision at selected signals, PR-AUC, calibration/Brier score, coverage, and net P&L after costs.
- Define acceptance thresholds before fitting models.
- Track every experiment, parameters, dataset version, code revision, metrics, and artifacts with MLflow (or an equivalent lightweight experiment registry).

**Deliverables:** experiment plan, baseline report, metric contract, experiment registry.

**Exit gate:** the baseline is reproducible and the model has explicit economic and statistical thresholds to beat.

### Phase E - Model development and offline validation

**Goal:** build the simplest robust model that improves the baseline.

- Start with logistic regression and a calibrated tree-based model; do not begin with deep learning.
- Use features available at decision time only: spread, order-book imbalance, depth/VWAP, volatility, update rate, data age, and latency.
- Tune only through temporal validation; keep the final test period untouched.
- Report feature importance, calibration, error slices, and stability across market regimes.

**Deliverables:** registered candidate model, training pipeline, model card, final holdout report.

**Exit gate:** the chosen model beats the baseline on the locked temporal test set and has an understandable failure analysis.

### Phase F - Economic simulation and decision policy

**Goal:** prove that quality metrics translate into a better industrial decision.

- Replay historical snapshots chronologically with the same decision delay used for labels.
- Apply fees, depth-based slippage, maximum notional, cooldown, and inventory/risk constraints.
- Compare baseline and ML policy for P&L, drawdown, selected-signal precision, turnover, and confidence intervals.
- Choose a probability threshold by expected value, not accuracy.

**Deliverables:** reproducible backtest, policy configuration, economic-effect report.

**Exit gate:** the expected economic benefit remains positive under conservative costs and sensitivity scenarios.

### Phase G - ML service, monitoring, and demo stand

**Goal:** demonstrate a production-shaped ML application.

- Package inference behind a small Python service or batch scorer; the .NET stream service supplies validated feature events.
- Add a model/version registry and rollback to the threshold baseline.
- Monitor input freshness, feature drift, prediction distribution, selected-signal rate, delayed label quality, and paper P&L.
- Build a focused Streamlit demo: live/replayed feature view, prediction/confidence, baseline versus ML decision, economic summary, and monitoring status.

**Deliverables:** demo service, monitoring dashboard, runbook, short architecture diagram.

**Exit gate:** a reviewer can replay data, see a model decision, inspect why it was made, and observe a monitoring signal.

### Phase H - Course presentation and reproducibility

**Goal:** make the project easy to assess and repeat.

- Prepare a concise narrative: business problem -> data -> baseline -> model -> economics -> monitoring -> limitations.
- Add one-command setup/replay instructions and a small anonymized sample dataset.
- Include a limitations section: non-stationary markets, backtest assumptions, exchange-data quality, and paper-versus-live gap.

**Deliverables:** presentation, reproducibility guide, final project report, demo script.

**Exit gate:** a new reviewer can run the demo and verify the primary conclusion without access to credentials or live trading.

## Scorecard

| Course capability | Concrete proof in this project |
|---|---|
| Business-to-math formulation | Phase A charter and label definition |
| Data analysis | Phase B/C versioned dataset and EDA |
| Quality criteria and experiments | Phase D/E temporal validation and tracked experiments |
| Economic effect | Phase F cost-aware policy backtest |
| Robust ML service | Phase G validated input, versioning, rollback |
| Monitoring | Phase G drift, performance, freshness, P&L |
| Demo stand | Phase G Streamlit replay/live demo |

## Immediate next task

Write the Phase A problem statement before any further exchange integration. It is the project contract for all later data, modelling, and evaluation work.
