using System.Text.Json.Serialization;

namespace ClearCase.Api.Features.Support.GetSupport;

public sealed record SupportContactResponse(
    string Email,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Phone,
    string Hours,
    string ResponseTime);

public static class GetSupportEndpoint
{
    private const string DefaultEmail = "support@clearcase.example";
    private const string DefaultPhone = "0800 000 000";
    private const string DefaultHours = "Mon-Fri 08:00-17:00 SAST";
    private const string DefaultResponseTime = "Within 1 business day";

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/v1/support", Get)
            .WithName("GetSupportContact")
            .WithSummary("How to reach ClearCase support")
            .WithDescription("Available anonymously (the sign-in page shows it) and returns only static contact "
                + "details. Values come from Support:Email, Support:Phone, "
                + "Support:Hours and Support:ResponseTime; an empty phone is omitted.")
            .Produces<SupportContactResponse>()
            .AllowAnonymous();

    private static SupportContactResponse Get(IConfiguration configuration)
    {
        var phone = configuration["Support:Phone"] ?? DefaultPhone;
        return new SupportContactResponse(
            Setting(configuration, "Support:Email", DefaultEmail),
            string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Setting(configuration, "Support:Hours", DefaultHours),
            Setting(configuration, "Support:ResponseTime", DefaultResponseTime));
    }

    private static string Setting(IConfiguration configuration, string key, string fallback) =>
        configuration[key] is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
}
