using ClearCase.Api.Endpoints;
using ClearCase.Api.Features.Notifications;
using ClearCase.Api.Features.Support.GetSupport;

namespace ClearCase.Api.Features;

/// <summary>Explicit composition root for authenticated customer feature endpoints.</summary>
public static class CustomerFeatureEndpointRegistration
{
    public static WebApplication MapCustomerFeatures(this WebApplication app)
    {
        app.MapTransactionEndpoints();
        app.MapDisputeEndpoints();
        NotificationEndpoints.Map(app);
        GetSupportEndpoint.Map(app);
        return app;
    }
}
