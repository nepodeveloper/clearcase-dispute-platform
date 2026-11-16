using ClearCase.Application.AgentDisputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Infrastructure.AgentDisputes;

public sealed class SqliteInvestigationNoteStore(TransactionDbContext context) : IInvestigationNoteStore
{
    private const int SqliteConstraintError = 19;

    public async Task<IReadOnlyList<InvestigationNote>> ReadAsync(
        Guid disputeId, CancellationToken cancellationToken) =>
        await context.Set<PersistedInvestigationNote>()
            .AsNoTracking()
            .Where(item => item.DisputeId == disputeId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(ToView())
            .ToArrayAsync(cancellationToken);

    public async Task<InvestigationNoteWriteResult> AddAsync(
        InvestigationNote note,
        string idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        if (!await context.Disputes.AsNoTracking()
                .AnyAsync(item => item.Id == note.DisputeId, cancellationToken))
        {
            return new(InvestigationNoteWriteOutcome.DisputeNotFound, null);
        }

        context.Set<PersistedInvestigationNote>().Add(new PersistedInvestigationNote
        {
            Id = note.Id,
            DisputeId = note.DisputeId,
            EmployeeId = note.EmployeeId,
            Text = note.Text,
            CreatedAt = note.CreatedAt,
            CorrelationId = note.CorrelationId,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(InvestigationNoteWriteOutcome.Created, note);
        }
        catch (DbUpdateException exception) when (IsIdempotencyConflict(exception))
        {
            context.ChangeTracker.Clear();
            var existing = await context.Set<PersistedInvestigationNote>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.DisputeId == note.DisputeId
                    && item.EmployeeId == note.EmployeeId
                    && item.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is null)
                throw;

            var outcome = existing.RequestFingerprint == requestFingerprint
                ? InvestigationNoteWriteOutcome.Replayed
                : InvestigationNoteWriteOutcome.IdempotencyKeyReused;
            return new(outcome, ToView(existing));
        }
    }

    private static System.Linq.Expressions.Expression<Func<PersistedInvestigationNote, InvestigationNote>> ToView() =>
        item => new InvestigationNote(item.Id, item.DisputeId, item.EmployeeId, item.Text,
            item.CreatedAt, item.CorrelationId);

    private static InvestigationNote ToView(PersistedInvestigationNote item) =>
        new(item.Id, item.DisputeId, item.EmployeeId, item.Text, item.CreatedAt, item.CorrelationId);

    private static bool IsIdempotencyConflict(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError } sqlite
        && sqlite.Message.Contains(
            "InvestigationNotes.DisputeId, InvestigationNotes.EmployeeId, InvestigationNotes.IdempotencyKey",
            StringComparison.Ordinal);
}
