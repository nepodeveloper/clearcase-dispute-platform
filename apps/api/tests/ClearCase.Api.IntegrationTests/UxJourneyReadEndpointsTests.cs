using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClearCase.Domain.Disputes;
using ClearCase.Infrastructure.Disputes;
using ClearCase.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClearCase.Api.IntegrationTests;

public sealed class NotificationEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly UxApp _app;

    public NotificationEndpointsTests(SqliteWebApplicationFactory factory) => _app = new UxApp(factory);

    private Task AddEventAsync(Guid disputeId, int sequence, string description, DateTimeOffset at) =>
        _app.WithDatabaseAsync(async database =>
        {
            database.DisputeEvents.Add(new PersistedDisputeEvent
            {
                Id = Guid.NewGuid(), DisputeId = disputeId, SequenceNumber = sequence,
                EventType = "DebitOrderStartReview", CaseStatus = DisputeCaseStatus.UnderReview,
                PublicDescription = description, OccurredAt = at
            });
            await database.SaveChangesAsync();
        });

    [Fact]
    public async Task FeedIsDerivedFromTheCallersOwnTimelinesNewestFirstWithoutStaffNotes()
    {
        var first = await _app.SeedDisputeAsync(createdAt: UxApp.Start.AddDays(-3), reference: "DSP-NOTIF-A");
        await _app.SeedDisputeAsync(createdAt: UxApp.Start.AddDays(-2), reference: "DSP-NOTIF-B");
        await AddEventAsync(first, 2, "Your case is being investigated.", UxApp.Start.AddDays(-1));
        await _app.SeedDisputeAsync(UxApp.OtherCustomerId, reference: "DSP-NOTIF-OTHER");
        using var agent = await _app.SignInAsync("agent.demo");
        using var note = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/agent/disputes/{first}/notes")
        {
            Content = JsonContent.Create(new { text = "Private staff observation about the merchant." })
        };
        note.Headers.Add("Idempotency-Key", "notification-note-1");
        using var noteResponse = await agent.SendAsync(note);
        Assert.Equal(HttpStatusCode.Created, noteResponse.StatusCode);

        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await customer.GetAsync("/api/v1/notifications");
        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(3, body.RootElement.GetProperty("unreadCount").GetInt32());
        Assert.Equal(new[] { "DSP-NOTIF-A", "DSP-NOTIF-B", "DSP-NOTIF-A" },
            items.Select(item => item.GetProperty("disputeReference").GetString()));
        Assert.Equal("Your case is being investigated.", items[0].GetProperty("message").GetString());
        Assert.Equal("DebitOrderStartReview", items[0].GetProperty("eventType").GetString());
        Assert.Equal(UxApp.Start.AddDays(-1), items[0].GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(first, items[0].GetProperty("disputeId").GetGuid());
        Assert.NotEqual(Guid.Empty, items[0].GetProperty("id").GetGuid());
        Assert.All(items, item => Assert.False(item.GetProperty("isRead").GetBoolean()));
        Assert.DoesNotContain("Private staff observation", raw);
        Assert.DoesNotContain("DSP-NOTIF-OTHER", raw);

        using var limited = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/notifications?limit=1"));
        Assert.Equal(1, limited.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(3, limited.RootElement.GetProperty("unreadCount").GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("abc")]
    [InlineData("-1")]
    public async Task LimitMustBeBetweenOneAndFifty(string limit)
    {
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await customer.GetAsync($"/api/v1/notifications?limit={limit}");
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("/limit", body.RootElement.GetProperty("errors")[0].GetProperty("pointer").GetString());
        using var fifty = await customer.GetAsync("/api/v1/notifications?limit=50");
        Assert.Equal(HttpStatusCode.OK, fifty.StatusCode);
    }

    [Fact]
    public async Task MarkingReadMovesTheMarkerForwardOnlyAndIsolatedPerCustomer()
    {
        var id = await _app.SeedDisputeAsync(UxApp.OtherCustomerId, createdAt: UxApp.Start.AddHours(-1));
        using var other = await _app.SignInAsync("customer.other");
        using var demo = await _app.SignInAsync("customer.demo");
        async Task<(int Unread, bool[] Read)> FeedAsync(HttpClient client)
        {
            using var body = await UxApp.ReadJsonAsync(await client.GetAsync("/api/v1/notifications"));
            return (body.RootElement.GetProperty("unreadCount").GetInt32(), body.RootElement.GetProperty("items")
                .EnumerateArray().Select(item => item.GetProperty("isRead").GetBoolean()).ToArray());
        }
        var demoBefore = await FeedAsync(demo);
        Assert.True((await FeedAsync(other)).Unread >= 1);

        using var marked = await other.PostAsync("/api/v1/notifications/read", null);
        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
        var read = await FeedAsync(other);
        Assert.Equal(0, read.Unread);
        Assert.All(read.Read, flag => Assert.True(flag));

        _app.Clock.Now = UxApp.Start.AddMinutes(10);
        await AddEventAsync(id, 2, "Additional review is complete and your case will continue to be investigated.", UxApp.Start.AddMinutes(10));
        var afterNew = await FeedAsync(other);
        Assert.Equal(1, afterNew.Unread);
        Assert.False(afterNew.Read[0]);
        Assert.All(afterNew.Read.Skip(1), flag => Assert.True(flag));

        _app.Clock.Now = UxApp.Start.AddHours(-5);
        using var earlier = await other.PostAsync("/api/v1/notifications/read", null);
        Assert.Equal(HttpStatusCode.NoContent, earlier.StatusCode);
        Assert.Equal(UxApp.Start, await _app.WithDatabaseAsync(database => database.NotificationReadMarkers
            .Where(item => item.CustomerId == Guid.Parse(UxApp.OtherCustomerId)).Select(item => item.ReadThrough).SingleAsync()));

        _app.Clock.Now = UxApp.Start.AddMinutes(20);
        using var again = await other.PostAsync("/api/v1/notifications/read", null);
        var final = await FeedAsync(other);
        Assert.Equal(0, final.Unread);
        Assert.All(final.Read, flag => Assert.True(flag));
        Assert.Equal(demoBefore.Unread, (await FeedAsync(demo)).Unread);
        _app.Clock.Now = UxApp.Start;
    }

    [Fact]
    public async Task MarkingReadRequiresCsrfAndACustomer()
    {
        using var agent = await _app.SignInAsync("agent.demo");
        using var forbidden = await agent.PostAsync("/api/v1/notifications/read", null);
        using var staffFeed = await agent.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffFeed.StatusCode);

        using var customer = await _app.SignInAsync("customer.demo");
        customer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var noCsrf = await customer.PostAsync("/api/v1/notifications/read", null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }
}

public sealed class SupportEndpointTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly SqliteWebApplicationFactory _factory;

    public SupportEndpointTests(SqliteWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SupportDetailsAreAvailableAnonymouslyWithSyntheticDefaults()
    {
        using var client = new UxApp(_factory, "Production").Anonymous();
        using var response = await client.GetAsync("/api/v1/support");
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("support@clearcase.example", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("0800 000 000", body.RootElement.GetProperty("phone").GetString());
        Assert.Equal("Mon-Fri 08:00-17:00 SAST", body.RootElement.GetProperty("hours").GetString());
        Assert.Equal("Within 1 business day", body.RootElement.GetProperty("responseTime").GetString());
    }

    [Fact]
    public async Task SupportDetailsComeFromConfigurationAndAnEmptyPhoneIsOmitted()
    {
        var app = new UxApp(_factory, settings: new Dictionary<string, string?>
        {
            ["Support:Email"] = "help@bank.example", ["Support:Phone"] = "", ["Support:Hours"] = "24/7",
            ["Support:ResponseTime"] = "Within 2 hours"
        });
        using var customer = await app.SignInAsync("customer.demo");
        using var body = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/support"));

        Assert.Equal("help@bank.example", body.RootElement.GetProperty("email").GetString());
        Assert.False(body.RootElement.TryGetProperty("phone", out _));
        Assert.Equal("24/7", body.RootElement.GetProperty("hours").GetString());
        Assert.Equal("Within 2 hours", body.RootElement.GetProperty("responseTime").GetString());
    }
}

public sealed class SessionEmployeeIdTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly UxApp _app;

    public SessionEmployeeIdTests(SqliteWebApplicationFactory factory) => _app = new UxApp(factory);

    [Theory]
    [InlineData("agent.demo")]
    [InlineData("supervisor.demo")]
    public async Task BankEmployeeSessionsExposeTheirEmployeeId(string username)
    {
        var expected = (await _app.UserIdAsync(username)).ToString();
        using var client = _app.Anonymous();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username, password = TestIdentityPasswords.For(username) });
        using var loginBody = await UxApp.ReadJsonAsync(login);
        using var me = await UxApp.ReadJsonAsync(await client.GetAsync("/api/v1/auth/me"));

        Assert.Equal(expected, loginBody.RootElement.GetProperty("employeeId").GetString());
        Assert.Equal(expected, me.RootElement.GetProperty("employeeId").GetString());
    }

    [Fact]
    public async Task CustomerSessionsOmitTheEmployeeId()
    {
        using var client = _app.Anonymous();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = "customer.demo", password = TestIdentityPasswords.For("customer.demo") });
        using var loginBody = await UxApp.ReadJsonAsync(login);
        using var me = await UxApp.ReadJsonAsync(await client.GetAsync("/api/v1/auth/me"));

        Assert.False(loginBody.RootElement.TryGetProperty("employeeId", out _));
        Assert.False(me.RootElement.TryGetProperty("employeeId", out _));
    }
}

public sealed class TransactionCursorEndpointTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly UxApp _app;

    public TransactionCursorEndpointTests(SqliteWebApplicationFactory factory) => _app = new UxApp(factory);

    private static string[] Ids(JsonDocument page) => page.RootElement.GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("id").GetString()!).ToArray();

    private static string? NextCursor(JsonDocument page) =>
        page.RootElement.GetProperty("pageInfo").TryGetProperty("nextCursor", out var cursor) ? cursor.GetString() : null;

    [Fact]
    public async Task OffsetPagingIsUnchangedAndNowAlsoReportsPageInfo()
    {
        using var customer = await _app.SignInAsync("customer.demo");
        using var second = await UxApp.ReadJsonAsync(await customer.GetAsync("/v1/transactions?pageSize=2&pageNumber=2"));
        using var last = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/transactions?pageSize=2&pageNumber=15"));

        Assert.Equal(new[] { "00000000-0000-0000-0000-000000000003", "00000000-0000-0000-0000-000000000004" }, Ids(second));
        Assert.Equal(2, second.RootElement.GetProperty("pageNumber").GetInt32());
        Assert.Equal(30, second.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(15, second.RootElement.GetProperty("totalPages").GetInt32());
        Assert.True(second.RootElement.GetProperty("hasNextPage").GetBoolean());
        Assert.True(second.RootElement.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        Assert.False(string.IsNullOrEmpty(NextCursor(second)));
        Assert.False(last.RootElement.GetProperty("hasNextPage").GetBoolean());
        Assert.False(last.RootElement.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        Assert.Null(NextCursor(last));
    }

    [Fact]
    public async Task CursorPagingVisitsEveryTransactionOnceInTheSameOrderAsPageNumbers()
    {
        using var customer = await _app.SignInAsync("customer.demo");
        using var all = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/transactions?pageSize=100"));
        var collected = new List<string>();
        var url = "/api/v1/transactions?pageSize=7";
        var pages = 0;
        while (true)
        {
            using var response = await customer.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var page = await UxApp.ReadJsonAsync(response);
            collected.AddRange(Ids(page));
            pages++;
            var cursor = NextCursor(page);
            Assert.Equal(cursor is not null, page.RootElement.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
            if (cursor is null) break;
            url = $"/v1/transactions?pageSize=7&cursor={Uri.EscapeDataString(cursor)}";
        }

        Assert.Equal(5, pages);
        Assert.Equal(Ids(all), collected);
    }

    [Fact]
    public async Task CursorIsStableAcrossTransactionsThatShareTheSameDate()
    {
        using var customer = await _app.SignInAsync("customer.demo");
        using var first = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/transactions?pageSize=29"));
        var cursor = NextCursor(first);
        Assert.NotNull(cursor);
        using var second = await UxApp.ReadJsonAsync(
            await customer.GetAsync($"/api/v1/transactions?pageSize=29&cursor={Uri.EscapeDataString(cursor!)}"));

        Assert.Equal("00000000-0000-0000-0000-000000000029", Ids(first)[^1]);
        Assert.Equal(new[] { "00000000-0000-0000-0000-000000000030" }, Ids(second));
        Assert.Null(NextCursor(second));
    }

    [Fact]
    public async Task CursorHonoursFiltersAndAnOffsetPageCursorContinuesTheSequence()
    {
        using var customer = await _app.SignInAsync("customer.demo");
        using var firstPage = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/transactions?pageSize=3&pageNumber=2"));
        var cursor = NextCursor(firstPage)!;
        using var continued = await UxApp.ReadJsonAsync(
            await customer.GetAsync($"/api/v1/transactions?pageSize=3&cursor={Uri.EscapeDataString(cursor)}"));
        Assert.Equal(new[] { "00000000-0000-0000-0000-000000000007", "00000000-0000-0000-0000-000000000008",
            "00000000-0000-0000-0000-000000000009" }, Ids(continued));

        using var filtered = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/transactions?pageSize=4&transactionType=BankCharge"));
        using var filteredNext = await UxApp.ReadJsonAsync(await customer.GetAsync(
            $"/api/v1/transactions?pageSize=4&transactionType=BankCharge&cursor={Uri.EscapeDataString(NextCursor(filtered)!)}"));
        Assert.All(Ids(filteredNext), id => Assert.Equal(0, int.Parse(id[^2..]) % 3));
        Assert.Empty(Ids(filtered).Intersect(Ids(filteredNext)));
    }

    [Fact]
    public async Task CursorTogetherWithPageNumberIsAConflict()
    {
        using var customer = await _app.SignInAsync("customer.demo");
        using var page = await UxApp.ReadJsonAsync(await customer.GetAsync("/api/v1/transactions?pageSize=2"));
        using var response = await customer.GetAsync(
            $"/api/v1/transactions?pageSize=2&pageNumber=1&cursor={Uri.EscapeDataString(NextCursor(page)!)}");
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = body.RootElement.GetProperty("errors")[0];
        Assert.Equal("/cursor", error.GetProperty("pointer").GetString());
        Assert.Equal("cursor.conflict", error.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("MXx4fHk")]
    [InlineData("MXw5OTk5OTk5OTk5OTk5OTk5OTk5fG5vdC1hLWd1aWQ")]
    public async Task InvalidCursorsAreRejected(string cursor)
    {
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await customer.GetAsync($"/api/v1/transactions?cursor={cursor}");
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = body.RootElement.GetProperty("errors")[0];
        Assert.Equal("/cursor", error.GetProperty("pointer").GetString());
        Assert.Equal("cursor.invalid", error.GetProperty("code").GetString());
    }
}

public sealed class UxJourneyMigrationTests
{
    private const string Previous = "20261117100000_AddOutboxParkingAndRecoveryAudit";

    [Fact]
    public async Task UpgradeBackfillsIntentUpdateTimesAndDowngradeRemovesTheNewObjects()
    {
        await using var database = new TemporaryDatabase();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var customerId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var disputeId = Guid.NewGuid();
        var createdTicks = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero).UtcTicks;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Transactions" ("Id","CustomerId","Type","Description","AmountMinorUnits","Currency","TransactionDate",
              "Status","IsDisputable","IsDebit","HasRequiredDisputeInformation")
            VALUES ({transactionId},{customerId},'BankCharge','Fee',1500,'ZAR',{createdTicks},'Posted',1,1,1)
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Disputes" ("Id","Reference","CustomerId","TransactionId","Category","ReasonCode","Description",
              "CaseStatus","DecisionStatus","FinancialStatus","EligibilityOutcome","EligibilityPolicyVersion","Version","CreatedAt")
            VALUES ({disputeId},'DSP-MIG-1',{customerId},{transactionId},'BankCharge','incorrectFee','A synthetic description',
              'Submitted','Pending','NotInitiated','EligibleToSubmit','v1',1,{createdTicks})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FinancialOperationIntents" ("Id","DisputeId","CustomerId","Kind","OperationReference","ExpectedVersion",
              "CorrelationId","Stage","Attempts","CreatedAt")
            VALUES ({Guid.NewGuid()},{disputeId},{customerId},'BankChargeAdjustment','MOCK-FEE-MIG',1,'corr','Pending',0,{createdTicks})
            """);

        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        db.ChangeTracker.Clear();
        Assert.Equal(createdTicks, (await db.FinancialOperationIntents.SingleAsync()).UpdatedAt.UtcTicks);
        Assert.Equal(2, await TriggerCountAsync(db));

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0, await TriggerCountAsync(db));
        Assert.Equal(0, await ObjectCountAsync(db, "table", "CustomerEvidence")
            + await ObjectCountAsync(db, "table", "DisputeAssignments")
            + await ObjectCountAsync(db, "table", "DisputeAssignmentAuditEvents")
            + await ObjectCountAsync(db, "table", "DisputeWithdrawals")
            + await ObjectCountAsync(db, "table", "NotificationReadMarkers"));
        Assert.Equal(1, await db.Disputes.CountAsync());
        Assert.Equal(1, await ObjectCountAsync(db, "table", "FinancialOperationIntents"));
    }

    private static Task<int> TriggerCountAsync(TransactionDbContext db) =>
        db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS "Value" FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'DisputeAssignmentAuditEvents'
            """).SingleAsync();

    private static Task<int> ObjectCountAsync(TransactionDbContext db, string type, string name) =>
        db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = {0} AND name = {1}", type, name).SingleAsync();
}

public sealed class UxJourneyOpenApiTests : IClassFixture<SqliteWebApplicationFactory>
{
    private readonly SqliteWebApplicationFactory _factory;

    public UxJourneyOpenApiTests(SqliteWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task OpenApiDescribesEveryNewRouteStatusAndMultipartBody()
    {
        using var client = new UxApp(_factory, "Development").Anonymous();
        using var response = await client.GetAsync("/openapi/v1.json");
        using var body = await UxApp.ReadJsonAsync(response);
        var paths = body.RootElement.GetProperty("paths");

        void Expect(string path, string method, params string[] statuses)
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            foreach (var status in statuses)
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _), $"{method} {path} {status}");
        }

        Expect("/api/v1/disputes/{id}/withdrawals", "post", "200", "400", "401", "403", "404", "409");
        Expect("/api/v1/disputes/{id}/evidence", "post", "201", "400", "404", "409", "415", "422", "503");
        Expect("/api/v1/disputes/{id}/evidence", "get", "200", "404");
        Expect("/api/v1/disputes/{id}/evidence/{evidenceId}/content", "get", "200", "404");
        Expect("/api/v1/agent/disputes/{id}/evidence", "get", "200", "403", "404");
        Expect("/api/v1/agent/disputes/{id}/evidence/{evidenceId}/content", "get", "200", "404");
        Expect("/api/v1/agent/disputes/{id}/assignment", "post", "200", "403", "404", "409");
        Expect("/api/v1/agent/disputes/{id}/assignment", "delete", "200", "403", "404");
        Expect("/api/v1/notifications", "get", "200", "400");
        Expect("/api/v1/notifications/read", "post", "204");
        Expect("/api/v1/support", "get", "200");
        Assert.True(paths.GetProperty("/api/v1/disputes/{id}/evidence").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
    }
}
