using System.Text;
using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Transactions;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Evidence;
using ClearCase.Infrastructure.Disputes;

namespace ClearCase.UnitTests;

public sealed class DisputeSlaPolicyTests
{
    private static readonly DisputeSlaPolicy Policy = new(TimeSpan.FromHours(120), TimeSpan.FromHours(24));
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, DisputeSlaStatus.OnTrack)]
    [InlineData(95 * 60 + 59, DisputeSlaStatus.OnTrack)]
    [InlineData(96 * 60, DisputeSlaStatus.DueSoon)]
    [InlineData(120 * 60, DisputeSlaStatus.DueSoon)]
    [InlineData(120 * 60 + 1, DisputeSlaStatus.Breached)]
    public void StatusFollowsTheDueSoonAndDueBoundaries(int minutesSinceCreated, DisputeSlaStatus expected)
    {
        var assessment = Policy.Assess(DisputeCaseStatus.UnderReview, Created, Created.AddMinutes(minutesSinceCreated));

        Assert.Equal(expected, assessment.Status);
        Assert.Equal(Created.AddHours(120), assessment.DueAt);
    }

    [Theory]
    [InlineData(DisputeCaseStatus.Closed)]
    [InlineData(DisputeCaseStatus.Rejected)]
    public void FinishedCasesAreCompleteEvenPastTheDueDate(DisputeCaseStatus status)
    {
        Assert.Equal(DisputeSlaStatus.Complete, Policy.Assess(status, Created, Created.AddDays(30)).Status);
        Assert.Null(Policy.ExpectedResolutionBy(status, Created));
    }

    [Fact]
    public void OpenCasesExposeTheExpectedResolutionDate() =>
        Assert.Equal(Created.AddHours(120), Policy.ExpectedResolutionBy(DisputeCaseStatus.ManualReview, Created));
}

public sealed class DisputeWithdrawalRulesTests
{
    [Fact]
    public void UndecidedUnpaidCaseCanBeWithdrawn() =>
        Assert.True(DisputeWithdrawalRules.CanWithdraw(DisputeCaseStatus.UnderReview,
            DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, false, false));

    [Theory]
    [InlineData(DisputeCaseStatus.Closed, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, false, false)]
    [InlineData(DisputeCaseStatus.Rejected, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, false, false)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Approved, DisputeFinancialStatus.NotInitiated, false, false)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Rejected, DisputeFinancialStatus.NotInitiated, false, false)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.Requested, false, false)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.OutcomeUnknown, false, false)]
    [InlineData(DisputeCaseStatus.ManualReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, true, false)]
    [InlineData(DisputeCaseStatus.UnderReview, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, false, true)]
    public void DecidedPaidEscalatedOrChargedBackCasesCannotBeWithdrawn(
        DisputeCaseStatus status, DisputeDecisionStatus decision, DisputeFinancialStatus financial,
        bool escalated, bool chargeback) =>
        Assert.False(DisputeWithdrawalRules.CanWithdraw(status, decision, financial, escalated, chargeback));

    [Fact]
    public void WithdrawalTransitionClosesTheCaseWithCustomerEventAndEnvelope()
    {
        var aggregate = new DisputeLifecycle(Guid.NewGuid(), DisputeCaseStatus.Submitted,
            DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated, 1, 1);

        var transition = aggregate.Advance(DisputeTransitionFamily.CustomerWithdrawal, "Withdrawn",
            DisputeCaseStatus.Closed, DisputeDecisionStatus.Pending, DisputeFinancialStatus.NotInitiated,
            DisputeWithdrawalRules.PublicDescription, DateTimeOffset.UnixEpoch, Guid.NewGuid());

        Assert.Equal("CustomerWithdrawn", transition.Event.EventType);
        Assert.Equal("dispute.withdrawn.v1", transition.EnvelopeEventType);
        Assert.Equal(DisputeCaseStatus.Closed, transition.Next.CaseStatus);
        Assert.Equal(DisputeDecisionStatus.Pending, transition.Next.DecisionStatus);
        Assert.Equal(2, transition.Next.Version);
        Assert.Equal("You withdrew this dispute. No payment adjustment was made.", transition.Event.PublicDescription);
    }
}

public sealed class DisputeAssignmentRulesTests
{
    private static readonly Guid Assignee = Guid.NewGuid();

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void OnlyTheAssigneeOrASupervisorMayRelease(bool actorIsAssignee, bool supervisor, bool allowed) =>
        Assert.Equal(allowed, DisputeAssignmentRules.CanRelease(
            Assignee, actorIsAssignee ? Assignee : Guid.NewGuid(), supervisor));
}

public sealed class CustomerEvidenceRulesTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];

    [Fact]
    public void SignaturesDecideTheContentType()
    {
        Assert.Equal("application/pdf", CustomerEvidenceRules.Sniff("%PDF-1.7\n"u8)?.ContentType);
        Assert.Equal("image/png", CustomerEvidenceRules.Sniff(PngSignature)?.ContentType);
        Assert.Equal("image/jpeg", CustomerEvidenceRules.Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 })?.ContentType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MZ executable pretending to be a pdf")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("%PDF")]
    public void UnsupportedOrTruncatedContentIsRejected(string text) =>
        Assert.Null(CustomerEvidenceRules.Sniff(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void GifAndZipSignaturesAreRejected()
    {
        Assert.Null(CustomerEvidenceRules.Sniff("GIF89a"u8));
        Assert.Null(CustomerEvidenceRules.Sniff(new byte[] { 0x50, 0x4B, 0x03, 0x04 }));
        Assert.Null(CustomerEvidenceRules.Sniff(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    [Theory]
    [InlineData(@"C:\Users\me\receipt.pdf", "receipt.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("bad<name>|?.png", "badname.png")]
    [InlineData("", "evidence")]
    [InlineData("  ...  ", "evidence")]
    [InlineData(null, "evidence")]
    public void FileNamesAreReducedToASafeDisplayName(string? original, string expected) =>
        Assert.Equal(expected, CustomerEvidenceRules.SanitizeFileName(original));

    [Fact]
    public void LongFileNamesAreTruncated() =>
        Assert.Equal(CustomerEvidenceRules.FileNameMaxLength,
            CustomerEvidenceRules.SanitizeFileName(new string('a', 300) + ".pdf").Length);
}

public sealed class TransactionCursorTests
{
    [Fact]
    public void CursorRoundTripsThroughItsOpaqueForm()
    {
        var cursor = new TransactionCursor(new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero), Guid.NewGuid());

        Assert.True(TransactionCursor.TryDecode(cursor.Encode(), out var decoded));
        Assert.Equal(cursor, decoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor!!")]
    [InlineData("MXx4fHk")]
    public void MalformedCursorsAreRejected(string value) =>
        Assert.False(TransactionCursor.TryDecode(value, out _));
}

public sealed class FinancialOperationStatusMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FinancialOperationStage.Pending, null, FinancialOperationStatus.Pending)]
    [InlineData(FinancialOperationStage.ResultRecorded, "Confirmed", FinancialOperationStatus.Succeeded)]
    [InlineData(FinancialOperationStage.Completed, "Succeeded", FinancialOperationStatus.Succeeded)]
    [InlineData(FinancialOperationStage.Completed, "Failed", FinancialOperationStatus.Failed)]
    [InlineData(FinancialOperationStage.Completed, "Unknown", FinancialOperationStatus.Unknown)]
    [InlineData(FinancialOperationStage.Completed, "OutcomeUnknown", FinancialOperationStatus.Unknown)]
    public void StageAndOutcomeMapToStaffStatus(
        FinancialOperationStage stage, string? outcome, FinancialOperationStatus expected) =>
        Assert.Equal(expected, FinancialOperationStatusMapper.Map(stage, outcome, null, Now));

    [Fact]
    public void ADispatchedSendIsInFlightOnlyWhileItsLeaseIsValid()
    {
        Assert.Equal(FinancialOperationStatus.InFlight,
            FinancialOperationStatusMapper.Map(FinancialOperationStage.Dispatched, null, Now.AddMinutes(1), Now));
        Assert.Equal(FinancialOperationStatus.Unknown,
            FinancialOperationStatusMapper.Map(FinancialOperationStage.Dispatched, null, Now.AddMinutes(-1), Now));
    }
}
