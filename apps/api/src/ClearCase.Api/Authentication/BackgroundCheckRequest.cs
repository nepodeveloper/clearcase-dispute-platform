namespace ClearCase.Api.Authentication;

public static class BackgroundCheckRequest
{
    private const string HeaderName = "X-Background-Check";

    public static bool IsRequested(HttpRequest request) =>
        string.Equals(request.Headers[HeaderName], "true", StringComparison.OrdinalIgnoreCase);
}
