using ClearCase.Api.Endpoints;

namespace ClearCase.Api.Features;

/// <summary>Explicit composition root for authenticated customer feature endpoints.</summary>
public static class CustomerFeatureEndpointRegistration
{
    public static WebApplication MapCustomerFeatures(this WebApplication app)
    {
        app.MapTransactionEndpoints();
        app.MapDisputeEndpoints();
        return app;
    }
}
