using Dapr.Client;
using Dapr.Workflow;

namespace DaprWorkflowIntro.Workflow;

/// <summary>
/// Predicts demand (MW) on the transmission corridor for the upcoming cycle.
/// Activities are where non-deterministic work belongs — the workflow body
/// itself must stay deterministic so Dapr can replay it.
/// </summary>
public class ForecastDemandActivity(ILogger<ForecastDemandActivity> logger)
    : WorkflowActivity<string, double>
{
    public override Task<double> RunAsync(WorkflowActivityContext context, string regionId)
    {
        // A wobble around a 12 GW base load. Random lives here, never in the workflow.
        var demand = 12_000 + Random.Shared.Next(-1_500, 1_500);
        logger.LogInformation("Forecast demand for {Region}: {Demand:N0} MW", regionId, demand);
        return Task.FromResult((double)demand);
    }
}

/// <summary>Reports how much generation capacity is currently available to dispatch.</summary>
public class MeasureGenerationActivity(ILogger<MeasureGenerationActivity> logger)
    : WorkflowActivity<string, double>
{
    public override Task<double> RunAsync(WorkflowActivityContext context, string regionId)
    {
        var available = 13_000 + Random.Shared.Next(-2_000, 1_000);
        logger.LogInformation("Available generation for {Region}: {Available:N0} MW", regionId, available);
        return Task.FromResult((double)available);
    }
}

/// <summary>
/// Decides how much power to dispatch, derives the resulting grid frequency, and
/// persists the decision to MongoDB through the Dapr state store component.
/// </summary>
public class DispatchPowerActivity(DaprClient dapr, ILogger<DispatchPowerActivity> logger)
    : WorkflowActivity<DispatchRequest, DispatchResult>
{
    public const string StateStore = "statestore";

    public override async Task<DispatchResult> RunAsync(WorkflowActivityContext context, DispatchRequest request)
    {
        // Dispatch what we can to meet demand; curtail the surplus.
        var dispatched = Math.Min(request.ForecastMw, request.AvailableMw);
        var curtailed = Math.Max(0, request.AvailableMw - request.ForecastMw);

        // Frequency drifts from the 60 Hz nominal in proportion to the supply gap.
        var gap = request.AvailableMw - request.ForecastMw;
        var frequency = Math.Round(60.0 + gap / 10_000.0, 3);

        var status = frequency switch
        {
            < 59.95 => "UNDER_FREQUENCY",
            > 60.05 => "OVER_FREQUENCY",
            _ => "NOMINAL",
        };

        var result = new DispatchResult(request.RegionId, request.Cycle, dispatched, curtailed, frequency, status);

        // Persist the latest posture to MongoDB (via Dapr). One document per region,
        // overwritten each cycle, plus an append-only-ish keyed snapshot per cycle.
        await dapr.SaveStateAsync(StateStore, $"grid:{request.RegionId}:latest", result);
        await dapr.SaveStateAsync(StateStore, $"grid:{request.RegionId}:cycle:{request.Cycle}", result);

        logger.LogInformation(
            "Cycle {Cycle}: dispatched {Dispatched:N0} MW, curtailed {Curtailed:N0} MW, {Freq} Hz [{Status}]",
            request.Cycle, dispatched, curtailed, frequency, status);

        return result;
    }
}
