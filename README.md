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

Each cycle the workflow operates on a single region (default `BRUSSELS-CAPITAL`) and:

1. `ForecastDemandActivity` — predicts corridor demand (MW).
2. `MeasureGenerationActivity` — measures available generation (MW).
3. `DispatchPowerActivity` — dispatches power, derives grid frequency, and **persists
   the decision to MongoDB** through the Dapr state store (a `:latest` document plus a
   per-cycle snapshot per region).
4. Waits one cycle (15 s) on a durable timer, then `ContinueAsNew` to loop with
   compacted history.

**[Diagrid dashboard](https://github.com/diagrid-labs/dashboard-aspire)** also runs alongside the workflow, giving a read-only view of the
state stored in MongoDB.

## Projects

| Project | Role                                                                                                                                                  |
| --- |-------------------------------------------------------------------------------------------------------------------------------------------------------|
| `AppHost` | Aspire app host — runs MongoDB, the worker service with its Dapr sidecar, and the Diagrid dashboard. Declares the Dapr state store components inline. |
| `Worker` | ASP.NET Core service (registered as `worker` in Aspire) hosting the workflow, activities, and management endpoints.                                   |
| `ServiceDefaults` | Shared Aspire telemetry/health defaults.                                                                                                              |

The Dapr MongoDB state store component (`statestore`, `actorStateStore: true`) is
generated programmatically in `AppHost.cs` rather than living in a static YAML file. A
second, read-only variant (`directConnection=true`, no replica-set discovery) is emitted
for the dashboard container — see the comments in `AppHost.cs` for why the two consumers
need different connection params.

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
dotnet run --project AppHost
```

Open the Aspire dashboard (URL printed on startup) to watch MongoDB, the workflow
service, its Dapr sidecar, and the Diagrid dashboard come up.

## Drive the demo

The singleton starts automatically. Use the worker service base URL shown in the
Aspire dashboard (`<worker-url>` below) to observe it:

```bash
# Check the live workflow status from the Dapr workflow engine
curl <worker-url>/grid/status

# Read the latest dispatch posture persisted to MongoDB (region is required)
curl "<worker-url>/grid?region=BRUSSELS-CAPITAL"

# Stop the singleton
curl -X POST <worker-url>/grid/stop
```

Restarting the service confirms the singleton behaviour — the hosted service finds the
still-running instance (rehydrated from MongoDB) and leaves it alone rather than
starting a second one.