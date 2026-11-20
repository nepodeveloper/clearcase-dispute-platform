using ClearCase.Api.Authentication;
using ClearCase.Api.Features.Disputes.SubmitDispute;
using ClearCase.Api.Features.Disputes.ListDisputes;
using ClearCase.Api.Features.Disputes.GetDispute;
using ClearCase.Api.Features.Disputes.GetDisputeTimeline;
using ClearCase.Api.Features.Disputes.WithdrawDispute;
using ClearCase.Api.Features.Evidence;

namespace ClearCase.Api.Endpoints;

public static class DisputeEndpoints
{
    public static WebApplication MapDisputeEndpoints(this WebApplication app)
    {
        foreach (var (prefix, suffix) in new[] { ("/api/v1", ""), ("/v1", "Alias") })
        {
            ListDisputesEndpoint.Map(app, prefix, suffix);

            GetDisputeTimelineEndpoint.Map(app, prefix, suffix);

            SubmitDisputeEndpoint.Map(app, prefix, suffix);

            GetDisputeEndpoint.Map(app, prefix, suffix);
        }

        WithdrawDisputeEndpoint.Map(app);
        CustomerEvidenceEndpoints.Map(app);
        return app;
    }


}
