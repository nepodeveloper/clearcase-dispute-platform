namespace ClearCase.Domain.Disputes;

public enum DisputeSlaStatus { OnTrack, DueSoon, Breached, Complete }

public sealed record DisputeSlaAssessment(DateTimeOffset DueAt, DisputeSlaStatus Status);

/// <summary>
/// Review-time commitment measured from case creation. A case is DueSoon from the start of the
/// due-soon window up to and including the due instant, and Breached strictly after it.
/// </summary>
public sealed record DisputeSlaPolicy(TimeSpan ReviewWindow, TimeSpan DueSoonWindow)
{
    public const int DefaultReviewHours = 120;
    public const int DefaultDueSoonHours = 24;

    public static DisputeSlaPolicy Default { get; } = new(
        TimeSpan.FromHours(DefaultReviewHours), TimeSpan.FromHours(DefaultDueSoonHours));

    public DateTimeOffset DueAt(DateTimeOffset createdAt) => createdAt + ReviewWindow;

    public DisputeSlaAssessment Assess(DisputeCaseStatus caseStatus, DateTimeOffset createdAt, DateTimeOffset now)
    {
        var dueAt = DueAt(createdAt);
        if (IsFinished(caseStatus))
            return new DisputeSlaAssessment(dueAt, DisputeSlaStatus.Complete);
        if (now > dueAt)
            return new DisputeSlaAssessment(dueAt, DisputeSlaStatus.Breached);
        return new DisputeSlaAssessment(dueAt,
            dueAt - now <= DueSoonWindow ? DisputeSlaStatus.DueSoon : DisputeSlaStatus.OnTrack);
    }

    public DateTimeOffset? ExpectedResolutionBy(DisputeCaseStatus caseStatus, DateTimeOffset createdAt) =>
        IsFinished(caseStatus) ? null : DueAt(createdAt);

    private static bool IsFinished(DisputeCaseStatus status) =>
        status is DisputeCaseStatus.Closed or DisputeCaseStatus.Rejected;
}
