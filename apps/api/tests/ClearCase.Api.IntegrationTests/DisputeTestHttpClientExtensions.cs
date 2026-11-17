using System.Net.Http.Json;

namespace ClearCase.Api.IntegrationTests;

/// <summary>Each new submission uses a unique retry-safe key; explicit replay tests own their keys.</summary>
internal static class DisputeTestHttpClientExtensions
{
    public static async Task<HttpResponseMessage> SubmitDisputeAsync<T>(
        this HttpClient client, string route, T payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }
}
