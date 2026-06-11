using Dapr.Workflow;

namespace DaprWorkflowIntro.Workflow;

/// <summary>
/// A singleton, eternal workflow that continuously balances a transmission region.
///
/// "Singleton" here means there is only ever one instance, identified by a fixed,
/// well-known instance id (<see cref="InstanceId"/>). Starting it is idempotent:
/// scheduling with the same id while it is already running is a no-op, so the grid
/// is never controlled by two competing instances.
///
/// Each iteration forecasts demand, measures generation, dispatches power, waits a
/// cycle, then calls <c>ContinueAsNew</c>. ContinueAsNew restarts the workflow with
/// fresh state and a truncated history, which is what keeps an "eternal" workflow
/// from accumulating unbounded event history.
/// </summary>
public class GridBalancingWorkflow : Workflow<GridState, GridState>
{
    /// <summary>The one and only instance id for the singleton grid controller.</summary>
    public const string InstanceId = "grid-balancer";

    /// <summary>How long to hold a dispatch posture before re-balancing.</summary>
    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(15);

    public override async Task<GridState> RunAsync(WorkflowContext context, GridState input)
    {
        var logger = context.CreateReplaySafeLogger<GridBalancingWorkflow>();
        logger.LogInformation("Balancing {Region}, cycle {Cycle}", input.RegionId, input.Cycle);

        var forecastMw = await context.CallActivityAsync<double>(
            nameof(ForecastDemandActivity), input.RegionId);

        var availableMw = await context.CallActivityAsync<double>(
            nameof(MeasureGenerationActivity), input.RegionId);

        var dispatch = await context.CallActivityAsync<DispatchResult>(
            nameof(DispatchPowerActivity),
            new DispatchRequest(input.RegionId, input.Cycle, forecastMw, availableMw));

        // Durable timer — survives process restarts, unlike Task.Delay.
        await context.CreateTimer(CycleInterval, CancellationToken.None);

        var next = input with
        {
            Cycle = input.Cycle + 1,
            LastDispatchedMw = dispatch.DispatchedMw,
            LastFrequencyHz = dispatch.FrequencyHz,
        };

        // Restart as a brand-new run with compacted history. This is the eternal loop.
        context.ContinueAsNew(next);
        return next;
    }
}
