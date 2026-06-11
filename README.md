# Dapr Workflows + MongoDB + Aspire

A small .NET 10 demo of a **singleton Dapr workflow** that continuously balances an
electrical transmission region, backed by **MongoDB** as the Dapr state store and
orchestrated locally with **.NET Aspire**.

## What it does

`GridBalancingWorkflow` is an *eternal singleton*: exactly one instance, identified
by the fixed id `grid-balancer`. It **auto-starts on launch** via a hosted service
(`GridBalancerStartupService`) — no API call needed. Starting is idempotent: if it's
already running (e.g. rehydrated from MongoDB after a restart), the service leaves it
be, so the grid is never controlled by two competing instances.

Each cycle the workflow:

1. `ForecastDemandActivity` — predicts corridor demand (MW).
2. `MeasureGenerationActivity` — measures available generation (MW).
3. `DispatchPowerActivity` — dispatches power, derives grid frequency, and **persists
   the decision to MongoDB** through the Dapr state store.
4. Waits one cycle on a durable timer, then `ContinueAsNew` to loop with compacted history.

## Projects

| Project | Role |
| --- | --- |
| `DaprWorkflowIntro.AppHost` | Aspire app host — runs MongoDB + the workflow service with its Dapr sidecar. |
| `DaprWorkflowIntro.Workflow` | ASP.NET Core service hosting the workflow, activities, and management endpoints. |
| `DaprWorkflowIntro.ServiceDefaults` | Shared Aspire telemetry/health defaults. |
| `dapr/components/statestore.yaml` | Dapr MongoDB state store component (`actorStateStore: true`). |

## Prerequisites

- .NET 10 SDK
- A container runtime (Docker/Podman) for the Aspire-managed MongoDB
- Dapr CLI **and the daprd runtime binaries** installed locally. The Aspire Dapr
  integration launches `daprd` (plus the placement/scheduler services that workflows
  rely on) from your local Dapr install. If you've only installed the CLI, run:

  ```bash
  dapr init --slim
  ```

  This installs `daprd`, the placement service, and the scheduler to `~/.dapr/bin`
  without requiring the default Redis/Zipkin containers.

## Run

```bash
dotnet run --project DaprWorkflowIntro.AppHost
```

Open the Aspire dashboard (URL printed on startup) to watch MongoDB, the workflow
service, and its Dapr sidecar come up.

## Drive the demo

The singleton starts automatically. Use the workflow service base URL shown in the
Aspire dashboard (`<workflow-url>` below) to observe it:

```bash
# Check the live workflow status from the Dapr engine
curl <workflow-url>/grid/status

# Read the latest dispatch posture persisted to MongoDB
curl <workflow-url>/grid

# Stop the singleton
curl -X POST <workflow-url>/grid/stop
```

Restarting the service confirms the singleton behaviour — the hosted service finds the
still-running instance (rehydrated from MongoDB) and leaves it alone rather than
starting a second one.
