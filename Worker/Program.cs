using Dapr.Client;
using Dapr.Workflow;

using DaprWorkflowIntro.ServiceDefaults;
using DaprWorkflowIntro.Workflow;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// A DaprClient for reading the persisted grid state out of MongoDB.
builder.Services.AddDaprClient();

// Register the singleton workflow and its activities with the Dapr workflow engine.
builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<GridBalancingWorkflow>();
        options.RegisterActivity<ForecastDemandActivity>();
        options.RegisterActivity<MeasureGenerationActivity>();
        options.RegisterActivity<DispatchPowerActivity>();
});

// Auto-start the singleton on startup instead of via an API call.
builder.Services.AddHostedService<GridBalancerStartupService>();

//
//
//

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () =>
    "Dapr Workflows + MongoDB grid-balancing demo. "
    + "The singleton auto-starts on launch; GET /grid/status or GET /grid to inspect it.");

// Inspect the live workflow status from the Dapr workflow engine.
app.MapGet("/grid/status", async (DaprWorkflowClient workflows) =>
{
    var state = await workflows.GetWorkflowStateAsync(GridBalancingWorkflow.InstanceId);
    if (state?.Exists is not true)
    {
        return Results.NotFound(new { message = "Singleton has never been started." });
    }

    return Results.Ok(new
    {
        instanceId = GridBalancingWorkflow.InstanceId,
        state.RuntimeStatus,
        state.IsWorkflowRunning,
        state.CreatedAt,
        state.LastUpdatedAt,
    });
});

// Stop the singleton.
app.MapPost("/grid/stop", async (DaprWorkflowClient workflows) =>
{
    await workflows.TerminateWorkflowAsync(GridBalancingWorkflow.InstanceId);
    return Results.Ok(new { message = "Termination requested.", instanceId = GridBalancingWorkflow.InstanceId });
});

// Read the latest dispatch posture that the workflow persisted to MongoDB (via Dapr).
app.MapGet("/grid", async (DaprClient dapr, string region) =>
{
    var latest = await dapr.GetStateAsync<DispatchResult?>(
        DispatchPowerActivity.StateStore, $"grid:{region}:latest");

    return latest is null
        ? Results.NotFound(new { message = $"No dispatch recorded yet for {region}." })
        : Results.Ok(latest);
});

//
//
//

var running = app.RunAsync();

await running;
