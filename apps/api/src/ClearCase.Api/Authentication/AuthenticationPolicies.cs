using ClearCase.Infrastructure.Authentication;

namespace ClearCase.Api.Authentication;

public static class AuthenticationPolicies
{
    public const string CustomerAccess = "customer-access";
    public const string BankEmployeeAccess = "bank-employee-access";
    public const string SupervisorAccess = "supervisor-access";

    public static IServiceCollection AddClearCaseAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(CustomerAccess, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(SyntheticIdentitySeeder.CustomerRole)
                .RequireClaim(SyntheticIdentitySeeder.ActorTypeClaimType, SyntheticIdentitySeeder.ActorTypeCustomer)
                .RequireClaim(SyntheticIdentitySeeder.CustomerIdClaimType));

            options.AddPolicy(BankEmployeeAccess, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(
                    SyntheticIdentitySeeder.ActorTypeClaimType,
                    SyntheticIdentitySeeder.ActorTypeBankEmployee)
                .RequireRole(SyntheticIdentitySeeder.AgentRole, SyntheticIdentitySeeder.SupervisorRole));

            options.AddPolicy(SupervisorAccess, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(
                    SyntheticIdentitySeeder.ActorTypeClaimType,
                    SyntheticIdentitySeeder.ActorTypeBankEmployee)
                .RequireRole(SyntheticIdentitySeeder.SupervisorRole));
        });

        return services;
    }
}
