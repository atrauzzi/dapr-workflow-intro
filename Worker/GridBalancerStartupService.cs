using Dapr.Workflow;

namespace DaprWorkflowIntro.Workflow;

/// <summary>
/// Auto-starts the singleton grid-balancing workflow when the service comes up.
///
/// Scheduling is idempotent: if the singleton (fixed id <see cref="GridBalancingWorkflow.InstanceId"/>)
/// is already running — e.g. after a restart where Dapr rehydrated it from MongoDB —
/// we leave it alone rather than starting a competing instance.
///
/// Because the workflow engine talks through the Dapr sidecar, which may not be ready
/// the instant the host starts, we retry until the sidecar answers.
/// </summary>
public class GridBalancerStartupService(
    DaprWorkflowClient workflows,
    ILogger<GridBalancerStartupService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var state = await workflows.GetWorkflowStateAsync(GridBalancingWorkflow.InstanceId, cancellation: stoppingToken);

        if (state is { Exists: true })
        {
            logger.LogInformation("Singleton {InstanceId} already running ({Status}); leaving it be.", GridBalancingWorkflow.InstanceId, state.RuntimeStatus);

            return;
        }

        await workflows.ScheduleNewWorkflowAsync(
            nameof(GridBalancingWorkflow),
            GridBalancingWorkflow.InstanceId,
            new GridState()
        );

        logger.LogInformation("Started singleton grid balancer {InstanceId}.", GridBalancingWorkflow.InstanceId);
    }
}
