using ClearCase.Api.Endpoints;
using ClearCase.Api.Middleware;
using ClearCase.Api.Transactions;
using ClearCase.Api.Authentication;
using ClearCase.Application.Transactions;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
var demoAuthenticationEnabled = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var authentication = builder.Services.AddAuthentication(demoAuthenticationEnabled ? "Demo" : "Unavailable");
if (demoAuthenticationEnabled)
{
    authentication.AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>("Demo", _ => { });
}
else
{
    authentication.AddScheme<AuthenticationSchemeOptions, UnavailableAuthenticationHandler>("Unavailable", _ => { });
}
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICustomerContext, HttpCustomerContext>();
builder.Services.AddScoped<IListTransactionsHandler, ListTransactionsHandler>();
builder.Services.AddScoped<IGetTransactionDetailsHandler, GetTransactionDetailsHandler>();
builder.Services.AddSingleton<ITransactionReader, InMemoryTransactionReader>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        if (context.HttpContext.Items.TryGetValue(
                CorrelationIdMiddleware.ContextItemKey,
                out var correlationId))
        {
            context.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});

if (builder.Environment.IsDevelopment())
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:4200"];

    builder.Services.AddCors(options => options.AddPolicy(
        "LocalAngularDevelopment",
        policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseCors("LocalAngularDevelopment");
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapTransactionEndpoints();

app.Run();

public partial class Program { }
