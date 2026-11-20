using ClearCase.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains(WebApplicationExtensions.MigrateOnlyArgument, StringComparer.Ordinal))
    builder.Configuration["Persistence:MigrateOnStartup"] = "true";

builder.AddClearCaseObservability();
builder.AddClearCaseSecurity();
builder.AddClearCaseApplication();
builder.AddClearCaseBackgroundWork();

var app = builder.Build();

await app.PrepareDatabaseAsync(args);
if (args.Contains(WebApplicationExtensions.MigrateOnlyArgument, StringComparer.Ordinal))
    return;

app.UseClearCasePipeline();
app.MapClearCaseEndpoints();

app.Run();

public partial class Program { }
