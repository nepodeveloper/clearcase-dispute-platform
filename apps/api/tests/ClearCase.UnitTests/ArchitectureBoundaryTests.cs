using System.Reflection;
using ClearCase.Application.AgentDisputes;
using ClearCase.Domain.Disputes;
using ClearCase.Domain.Transactions;
using ClearCase.Infrastructure.Disputes;

namespace ClearCase.UnitTests;

/// <summary>Compiled dependency rules; they complement the static scripts/ci/architecture_guard.py check.</summary>
public sealed class ArchitectureBoundaryTests
{
    private static readonly Assembly Domain = typeof(Dispute).Assembly;
    private static readonly Assembly Application = typeof(IAgentDisputeCommandHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(SqliteDisputeStore).Assembly;

    [Fact]
    public void DomainDependsOnNoOtherClearCaseLayerOrPersistenceFramework()
    {
        var references = ReferencedNames(Domain);

        Assert.DoesNotContain(references, name => name.StartsWith("ClearCase.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationDependsOnlyOnTheDomainAmongClearCaseLayers()
    {
        var references = ReferencedNames(Application);

        Assert.Contains("ClearCase.Domain", references);
        Assert.DoesNotContain("ClearCase.Infrastructure", references);
        Assert.DoesNotContain("ClearCase.Api", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void InfrastructureNeverReferencesTheApiLayer()
    {
        Assert.DoesNotContain("ClearCase.Api", ReferencedNames(Infrastructure));
    }

    [Fact]
    public void EveryTransactionCategoryHasExactlyOneCommandProcessor()
    {
        ICategoryCommandProcessor[] processors =
        [
            new DebitOrderCommandProcessor(null!),
            new BankChargeCommandProcessor(null!),
            new ChargebackCommandProcessor(null!)
        ];

        var covered = processors.Select(processor => processor.Category).Order().ToArray();
        Assert.Equal(Enum.GetValues<TransactionCategory>().Order().ToArray(), covered);
    }

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToArray();
}
