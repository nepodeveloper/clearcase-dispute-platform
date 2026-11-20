namespace ClearCase.Domain.Disputes;

/// <summary>Assignment is workload information only; it never gates case processing.</summary>
public static class DisputeAssignmentRules
{
    public static bool CanRelease(Guid assigneeId, Guid actorId, bool actorIsSupervisor) =>
        actorIsSupervisor || assigneeId == actorId;
}

public enum DisputeAssignmentAction { Claimed, Released }
