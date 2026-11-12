using System.Security.Claims;
using ClearCase.Application.Transactions;

namespace ClearCase.Api.Transactions;

public sealed class HttpCustomerContext(IHttpContextAccessor httpContextAccessor) : ICustomerContext
{
    public Guid CustomerId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(claim, out var customerId))
                throw new InvalidOperationException("The authenticated principal has no valid customer identity.");

            return customerId;
        }
    }
}
