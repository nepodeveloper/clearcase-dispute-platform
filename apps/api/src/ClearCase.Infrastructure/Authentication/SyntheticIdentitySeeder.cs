using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ClearCase.Infrastructure.Transactions;

namespace ClearCase.Infrastructure.Authentication;

public static class SyntheticIdentitySeeder
{
    public const string CustomerRole = "Customer";
    public const string AgentRole = "Agent";
    public const string SupervisorRole = "Supervisor";
    public const string CustomerIdClaimType = "customer_id";
    public const string ActorTypeClaimType = "actor_type";
    public const string ActorTypeCustomer = "customer";
    public const string ActorTypeBankEmployee = "bankEmployee";

    private static readonly Guid DemoCustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly SeededIdentity[] Users =
    [
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333331"),
            "customer.demo",
            "Customer",
            CustomerRole,
            ActorTypeCustomer,
            DemoCustomerId),
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333332"),
            "customer.other",
            "OtherCustomer",
            CustomerRole,
            ActorTypeCustomer,
            OtherCustomerId),
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "agent.demo",
            "Agent",
            AgentRole,
            ActorTypeBankEmployee,
            null),
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333334"),
            "supervisor.demo",
            "Supervisor",
            SupervisorRole,
            ActorTypeBankEmployee,
            null)
    ];

    public static async Task SeedAsync(
        TransactionDbContext context,
        IReadOnlyDictionary<string, string?> passwords,
        CancellationToken cancellationToken)
    {
        var hasher = new PasswordHasher<ApplicationUser>();
        await EnsureRolesAsync(context, cancellationToken);
        foreach (var seeded in Users)
        {
            var user = await context.Users.SingleOrDefaultAsync(
                item => item.NormalizedUserName == seeded.NormalizedUserName, cancellationToken);
            if (user is null)
            {
                if (!passwords.TryGetValue(seeded.PasswordSetting, out var password)
                    || string.IsNullOrWhiteSpace(password) || password.Length < 12
                    || !password.Any(char.IsUpper) || !password.Any(char.IsLower)
                    || !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit))
                {
                    throw new InvalidOperationException(
                        $"Configure Authentication:SeedPasswords:{seeded.PasswordSetting} with a strong local seed password (12+ characters, upper/lowercase, digit and symbol).");
                }
                user = new ApplicationUser
                {
                    Id = seeded.UserId,
                    UserName = seeded.UserName,
                    NormalizedUserName = seeded.NormalizedUserName,
                    EmailConfirmed = true,
                    LockoutEnabled = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N")
                };
                user.PasswordHash = hasher.HashPassword(user, password);
                context.Users.Add(user);
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        foreach (var seeded in Users)
        {
            var user = await context.Users.SingleAsync(
                item => item.NormalizedUserName == seeded.NormalizedUserName, cancellationToken);
            var role = await context.Roles.SingleAsync(item => item.Name == seeded.Role, cancellationToken);
            if (!await context.UserRoles.AnyAsync(
                    item => item.UserId == user.Id && item.RoleId == role.Id, cancellationToken))
            {
                context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
            }

            await EnsureClaimAsync(context, user.Id, ActorTypeClaimType, seeded.ActorType, cancellationToken);
            if (seeded.CustomerId is { } customerId)
            {
                await EnsureClaimAsync(
                    context,
                    user.Id,
                    CustomerIdClaimType,
                    customerId.ToString(),
                    cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureRolesAsync(TransactionDbContext context, CancellationToken cancellationToken)
    {
        foreach (var roleName in new[] { CustomerRole, AgentRole, SupervisorRole })
        {
            if (await context.Roles.AnyAsync(item => item.Name == roleName, cancellationToken))
                continue;

            context.Roles.Add(new IdentityRole<Guid>
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant()
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureClaimAsync(
        TransactionDbContext context,
        Guid userId,
        string claimType,
        string claimValue,
        CancellationToken cancellationToken)
    {
        var claims = await context.UserClaims
            .Where(item => item.UserId == userId && item.ClaimType == claimType)
            .ToListAsync(cancellationToken);
        var existing = claims.SingleOrDefault(item => item.ClaimValue == claimValue);
        if (existing is not null)
            return;

        context.UserClaims.RemoveRange(claims);
        context.UserClaims.Add(new IdentityUserClaim<Guid>
        {
            UserId = userId,
            ClaimType = claimType,
            ClaimValue = claimValue
        });
    }

    private sealed record SeededIdentity(
        Guid UserId,
        string UserName,
        string PasswordSetting,
        string Role,
        string ActorType,
        Guid? CustomerId)
    {
        public string NormalizedUserName => UserName.ToUpperInvariant();
    }
}
