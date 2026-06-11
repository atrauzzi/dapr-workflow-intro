namespace DaprWorkflowIntro.Workflow;

/// <summary>
/// The durable state carried by the singleton grid-balancing workflow across
/// dispatch cycles. Because the workflow uses <c>ContinueAsNew</c>, this record
/// is both the input and the output of every iteration.
/// </summary>
public record GridState
{
    /// <summary>The transmission region this controller is responsible for.</summary>
    public string RegionId { get; init; } = "BRUSSELS-CAPITAL";

    /// <summary>Monotonically increasing dispatch cycle counter.</summary>
    public int Cycle { get; init; }

    /// <summary>Megawatts dispatched in the most recent cycle.</summary>
    public double LastDispatchedMw { get; init; }

    /// <summary>Frequency (Hz) of the grid after the most recent dispatch.</summary>
    public double LastFrequencyHz { get; init; }
}

/// <summary>Input to the dispatch activity: what we forecast vs. what we can generate.</summary>
public record DispatchRequest(string RegionId, int Cycle, double ForecastMw, double AvailableMw);

/// <summary>
/// The outcome of a single dispatch decision. Persisted to MongoDB via the Dapr
/// state store so operators can inspect the latest grid posture out of band.
/// </summary>
public record DispatchResult(
    string RegionId,
    int Cycle,
    double DispatchedMw,
    double CurtailedMw,
    double FrequencyHz,
    string Status);
