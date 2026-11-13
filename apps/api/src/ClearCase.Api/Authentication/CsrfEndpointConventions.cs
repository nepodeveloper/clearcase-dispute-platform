using ClearCase.Api.Middleware;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClearCase.Api.Authentication;

public static class CsrfEndpointConventions
{
    public static RouteHandlerBuilder RequireCsrfProtection(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid anti-forgery token",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "auth.csrfInvalid",
                        ["correlationId"] = context.HttpContext.Items[CorrelationIdMiddleware.ContextItemKey]
                    });
            }

            return await next(context);
        });
}
