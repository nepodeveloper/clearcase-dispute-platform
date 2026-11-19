using ClearCase.Api.Authentication;
using ClearCase.Api.Features;
using ClearCase.Application.AgentDisputes;
using ClearCase.Application.Chargebacks;
using ClearCase.Application.DisputeEligibility;
using ClearCase.Application.Disputes;
using ClearCase.Application.SupervisorEscalations;
using ClearCase.Application.Transactions;
using ClearCase.Api.Endpoints;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Domain.DisputeEligibility;
using ClearCase.Infrastructure.AgentDisputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.SupervisorEscalations;
using ClearCase.Infrastructure.Transactions;

namespace ClearCase.Api.Hosting;

public static class ApplicationServiceExtensions
{
    public static WebApplicationBuilder AddClearCaseApplication(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOpenApi();
        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                if (context.HttpContext.Items.TryGetValue(
                        CorrelationIdMiddleware.ContextItemKey, out var correlationId))
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
            };
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICustomerContext, HttpCustomerContext>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.AddDisputeEligibility();
        builder.AddFinancialProcessing();
        builder.AddCaseHandlers();

        services.AddTransactionPersistence(
            builder.Environment.ContentRootPath, builder.Environment.WebRootPath,
            builder.Environment.IsDevelopment());

        if (builder.Environment.IsDevelopment())
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? ["http://localhost:4200"];
            services.AddCors(options => options.AddPolicy(
                "LocalAngularDevelopment",
                policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
        }
        return builder;
    }

    private static void AddDisputeEligibility(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var settings = builder.Configuration.GetSection("DisputeEligibility")
            .Get<DisputeEligibilityPolicySettings>()
            ?? throw new InvalidOperationException("DisputeEligibility configuration is required.");
        services.AddSingleton(settings);
        services.AddSingleton<IDisputeEligibilityPolicy, CardPurchaseEligibilityPolicy>();
        services.AddSingleton<IDisputeEligibilityPolicy, DebitOrderEligibilityPolicy>();
        services.AddSingleton<IDisputeEligibilityPolicy, BankChargeEligibilityPolicy>();
        services.AddScoped<DisputeEligibilityEvaluator>();
        if (SimulationAuthorization.IsEnabled(builder.Environment))
            services.AddSingleton<IDebitOrderMandateReader, MockDebitOrderMandateReader>();
        else
            services.AddSingleton<IDebitOrderMandateReader, UnavailableDebitOrderMandateReader>();
    }

    private static void AddFinancialProcessing(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddScoped<IDebitOrderProcessingHandler, DebitOrderProcessingHandler>();
        services.AddScoped<IBankChargeProcessingHandler, BankChargeProcessingHandler>();
        services.AddScoped<IChargebackProcessingHandler, ChargebackProcessingHandler>();
        // Unavailable adapters are the safe default; simulations below replace them in Development/Testing only.
        services.AddSingleton<IDebitOrderProcessingGateway, UnavailableDisputeProcessingGateway>();
        services.AddSingleton<IBankChargeAdjustmentGateway, UnavailableDisputeProcessingGateway>();
        services.AddSingleton<ICardNetworkGateway, UnavailableDisputeProcessingGateway>();
        builder.AddDebitOrderSimulation();
        builder.AddChargebackSimulation();
        builder.AddBankChargeSimulation();
    }

    private static void AddCaseHandlers(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddScoped<IListTransactionsHandler, ListTransactionsHandler>();
        services.AddScoped<IGetTransactionDetailsHandler, GetTransactionDetailsHandler>();
        services.AddScoped<IGetDisputeEligibilityHandler, GetDisputeEligibilityHandler>();
        services.AddScoped<ISubmitDisputeHandler, SubmitDisputeHandler>();
        services.AddScoped<IGetDisputeHandler, GetDisputeHandler>();
        services.AddScoped<IListDisputesHandler, ListDisputesHandler>();
        services.AddScoped<IAgentDisputeReader, SqliteAgentDisputeReader>();
        services.AddScoped<IAgentDisputeCommandHandler, AgentDisputeCommandHandler>();
        services.AddScoped<ICategoryCommandProcessor, DebitOrderCommandProcessor>();
        services.AddScoped<ICategoryCommandProcessor, BankChargeCommandProcessor>();
        services.AddScoped<ICategoryCommandProcessor, ChargebackCommandProcessor>();
        services.AddScoped<IInvestigationNoteStore, SqliteInvestigationNoteStore>();
        services.AddScoped<IListAgentDisputesHandler, ListAgentDisputesHandler>();
        services.AddScoped<IGetAgentDisputeHandler, GetAgentDisputeHandler>();
        services.AddScoped<IGetInvestigationNotesHandler, GetInvestigationNotesHandler>();
        services.AddScoped<ICreateInvestigationNoteHandler, CreateInvestigationNoteHandler>();
        services.AddScoped<ISupervisorEscalationStore, SqliteSupervisorEscalationStore>();
        services.AddScoped<ListSupervisorEscalationsHandler>();
        services.AddScoped<GetSupervisorEscalationHandler>();
        services.AddScoped<GetActiveDisputeEscalationHandler>();
        services.AddScoped<CreateDisputeEscalationHandler>();
        services.AddScoped<AcknowledgeEscalationHandler>();
        services.AddScoped<ResolveEscalationHandler>();
    }
}
