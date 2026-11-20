using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearCase.Domain.Disputes;
using Microsoft.EntityFrameworkCore;

namespace ClearCase.Api.IntegrationTests;

public sealed class CustomerEvidenceEndpointsTests : IClassFixture<SqliteWebApplicationFactory>
{
    private static readonly byte[] PdfHeader = "%PDF-1.7\n"u8.ToArray();
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly SqliteWebApplicationFactory _factory;
    private readonly UxApp _app;

    public CustomerEvidenceEndpointsTests(SqliteWebApplicationFactory factory)
    {
        _factory = factory;
        _app = new UxApp(factory);
    }

    private static byte[] Pdf(int size = 64)
    {
        var bytes = new byte[size];
        PdfHeader.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent Form(
        byte[]? file, string fileName = "receipt.pdf", string declaredType = "application/pdf", string? note = null)
    {
        var form = new MultipartFormDataContent();
        if (file is not null)
        {
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
            form.Add(content, "file", fileName);
        }
        if (note is not null)
            form.Add(new StringContent(note), "note");
        return form;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid id, MultipartFormDataContent form) =>
        client.PostAsync($"/api/v1/disputes/{id}/evidence", form);

    private Task<int> StoredCountAsync(Guid id) =>
        _app.WithDatabaseAsync(database => database.CustomerEvidence.CountAsync(item => item.DisputeId == id));

    [Fact]
    public async Task UploadStoresTheFileAndReturnsItsMetadata()
    {
        var id = await _app.SeedDisputeAsync();
        var bytes = Pdf(1024);
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await UploadAsync(customer, id, Form(bytes, note: "  Receipt from the shop  "));
        using var body = await UxApp.ReadJsonAsync(response);
        var root = body.RootElement;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var evidenceId = root.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/disputes/{id}/evidence/{evidenceId}/content", response.Headers.Location?.OriginalString);
        Assert.Equal("receipt.pdf", root.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", root.GetProperty("contentType").GetString());
        Assert.Equal(1024, root.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(UxApp.Start, root.GetProperty("uploadedAt").GetDateTimeOffset());
        Assert.Equal("Receipt from the shop", root.GetProperty("note").GetString());
        Assert.Equal(["id", "fileName", "contentType", "sizeBytes", "uploadedAt", "note"],
            root.EnumerateObject().Select(property => property.Name).ToArray());

        var stored = await _app.WithDatabaseAsync(database => database.CustomerEvidence.AsNoTracking().SingleAsync(item => item.Id == evidenceId));
        Assert.Equal(bytes, stored.Content);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), stored.Sha256);
        Assert.Equal(id, stored.DisputeId);
    }

    [Fact]
    public async Task TheDeclaredContentTypeIsIgnoredAndTheFileNameIsReducedToADisplayName()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        var png = PngHeader.Concat(new byte[32]).ToArray();
        using var response = await UploadAsync(customer, id,
            Form(png, fileName: @"C:\fakepath\..\bad<name>.png", declaredType: "text/plain"));
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("image/png", body.RootElement.GetProperty("contentType").GetString());
        Assert.Equal("badname.png", body.RootElement.GetProperty("fileName").GetString());
        Assert.False(body.RootElement.TryGetProperty("note", out _));
    }

    [Theory]
    [InlineData("plain text pretending to be a pdf", "notes.pdf", "application/pdf")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>", "image.png", "image/png")]
    [InlineData("GIF89a....", "animation.jpg", "image/jpeg")]
    public async Task FilesWhoseSignatureIsNotPdfPngOrJpegAreRejected(string text, string name, string declared)
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await UploadAsync(customer, id, Form(Encoding.UTF8.GetBytes(text), name, declared));
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("evidence.invalidFile", UxApp.Code(body));
        Assert.Equal(0, await StoredCountAsync(id));
    }

    [Fact]
    public async Task EmptyMissingAndNonMultipartUploadsAreRejected()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var empty = await UploadAsync(customer, id, Form([]));
        using var missing = await UploadAsync(customer, id, Form(null, note: "no file here"));
        foreach (var response in new[] { empty, missing })
        {
            using var body = await UxApp.ReadJsonAsync(response);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("evidence.invalidFile", UxApp.Code(body));
        }

        using var json = await customer.PostAsync($"/api/v1/disputes/{id}/evidence",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);
        Assert.Equal(0, await StoredCountAsync(id));
    }

    [Fact]
    public async Task FilesAboveFiveMebibytesAreRejectedButExactlyFiveMebibytesIsAccepted()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var tooBig = await UploadAsync(customer, id, Form(Pdf(5 * 1024 * 1024 + 1)));
        using var tooBigBody = await UxApp.ReadJsonAsync(tooBig);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal("evidence.invalidFile", UxApp.Code(tooBigBody));
        Assert.Equal(0, await StoredCountAsync(id));

        using var exact = await UploadAsync(customer, id, Form(Pdf(5 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.Created, exact.StatusCode);
    }

    [Fact]
    public async Task NoteIsLimitedToTwoHundredCharacters()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var tooLong = await UploadAsync(customer, id, Form(Pdf(), note: new string('n', 201)));
        using var tooLongBody = await UxApp.ReadJsonAsync(tooLong);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("evidence.invalidFile", UxApp.Code(tooLongBody));
        Assert.Equal("/note", tooLongBody.RootElement.GetProperty("errors")[0].GetProperty("pointer").GetString());

        using var allowed = await UploadAsync(customer, id, Form(Pdf(), note: new string('n', 200)));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task AtMostFiveFilesPerDisputeAreAccepted()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        for (var index = 0; index < 5; index++)
        {
            using var accepted = await UploadAsync(customer, id, Form(Pdf(), fileName: $"page-{index}.pdf"));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var sixth = await UploadAsync(customer, id, Form(Pdf(), fileName: "page-6.pdf"));
        using var body = await UxApp.ReadJsonAsync(sixth);

        Assert.Equal(HttpStatusCode.Conflict, sixth.StatusCode);
        Assert.Equal("evidence.limitReached", UxApp.Code(body));
        Assert.Equal(5, await StoredCountAsync(id));
    }

    [Fact]
    public async Task ClosedDisputesRejectEvidence()
    {
        var id = await _app.SeedDisputeAsync(status: DisputeCaseStatus.Closed);
        using var customer = await _app.SignInAsync("customer.demo");
        using var response = await UploadAsync(customer, id, Form(Pdf()));
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("dispute.closed", UxApp.Code(body));
        Assert.Equal(0, await StoredCountAsync(id));
    }

    [Fact]
    public async Task AFileTheScannerFlagsAsAnEicarTestFileIsRejectedAndNotStored()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        var infected = PdfHeader.Concat(Encoding.ASCII.GetBytes("EICAR-STANDARD-ANTIVIRUS-TEST-FILE")).ToArray();
        using var response = await UploadAsync(customer, id, Form(infected));
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("evidence.rejectedByScan", UxApp.Code(body));
        Assert.Equal(0, await StoredCountAsync(id));
    }

    [Fact]
    public async Task WithoutAConfiguredScannerUploadsFailClosedAndStoreNothing()
    {
        var production = new UxApp(_factory, "Production");
        var id = await production.SeedDisputeAsync();
        using var customer = await production.SignInAsync("customer.demo");
        using var response = await UploadAsync(customer, id, Form(Pdf()));
        using var body = await UxApp.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("evidence.scanUnavailable", UxApp.Code(body));
        Assert.Equal(0, await StoredCountAsync(id));
    }

    [Fact]
    public async Task ListReturnsOldestFirstAndDownloadReturnsTheExactBytesAsAnAttachment()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        var first = Pdf(100);
        var second = PngHeader.Concat(new byte[50]).ToArray();
        using var one = await UploadAsync(customer, id, Form(first, "first.pdf"));
        _app.Clock.Now = UxApp.Start.AddMinutes(5);
        using var two = await UploadAsync(customer, id, Form(second, "second.png", "image/png"));
        _app.Clock.Now = UxApp.Start;

        using var list = await customer.GetAsync($"/api/v1/disputes/{id}/evidence");
        using var listBody = await UxApp.ReadJsonAsync(list);
        var items = listBody.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        Assert.Equal(new[] { "first.pdf", "second.png" }, items.Select(item => item.GetProperty("fileName").GetString()));

        var evidenceId = items[1].GetProperty("id").GetGuid();
        using var download = await customer.GetAsync($"/api/v1/disputes/{id}/evidence/{evidenceId}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(second, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("second.png", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(download.Headers.CacheControl?.NoStore);
        Assert.True(download.Headers.CacheControl?.Private);
    }

    [Fact]
    public async Task EmptyListIsAnEmptyArray()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var body = await UxApp.ReadJsonAsync(await customer.GetAsync($"/api/v1/disputes/{id}/evidence"));

        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task OtherCustomersCannotUploadListOrDownloadAnotherCustomersEvidence()
    {
        var ownId = await _app.SeedDisputeAsync();
        var otherId = await _app.SeedDisputeAsync(UxApp.OtherCustomerId);
        using var owner = await _app.SignInAsync("customer.other");
        using var uploaded = await UploadAsync(owner, otherId, Form(Pdf()));
        using var uploadedBody = await UxApp.ReadJsonAsync(uploaded);
        var evidenceId = uploadedBody.RootElement.GetProperty("id").GetGuid();

        using var demo = await _app.SignInAsync("customer.demo");
        using var upload = await UploadAsync(demo, otherId, Form(Pdf()));
        using var list = await demo.GetAsync($"/api/v1/disputes/{otherId}/evidence");
        using var download = await demo.GetAsync($"/api/v1/disputes/{otherId}/evidence/{evidenceId}/content");
        using var crossDispute = await demo.GetAsync($"/api/v1/disputes/{ownId}/evidence/{evidenceId}/content");

        Assert.All(new[] { upload, list, download, crossDispute },
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(1, await StoredCountAsync(otherId));
        Assert.Equal(0, await StoredCountAsync(ownId));
    }

    [Fact]
    public async Task StaffCanReadEvidenceAcrossCustomersButCannotUploadAndCustomersCannotUseStaffRoutes()
    {
        var id = await _app.SeedDisputeAsync(UxApp.OtherCustomerId);
        using var owner = await _app.SignInAsync("customer.other");
        var bytes = Pdf(77);
        using var uploaded = await UploadAsync(owner, id, Form(bytes, "evidence.pdf"));
        using var uploadedBody = await UxApp.ReadJsonAsync(uploaded);
        var evidenceId = uploadedBody.RootElement.GetProperty("id").GetGuid();

        foreach (var username in new[] { "agent.demo", "supervisor.demo" })
        {
            using var staff = await _app.SignInAsync(username);
            using var list = await UxApp.ReadJsonAsync(await staff.GetAsync($"/api/v1/agent/disputes/{id}/evidence"));
            Assert.Equal(evidenceId, Assert.Single(list.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            using var download = await staff.GetAsync($"/api/v1/agent/disputes/{id}/evidence/{evidenceId}/content");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
            Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.True(download.Headers.CacheControl?.NoStore);
            using var unknown = await staff.GetAsync($"/api/v1/agent/disputes/{Guid.NewGuid()}/evidence");
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            using var staffUpload = await UploadAsync(staff, id, Form(Pdf()));
            Assert.Equal(HttpStatusCode.Forbidden, staffUpload.StatusCode);
        }

        using var customer = await _app.SignInAsync("customer.demo");
        using var customerList = await customer.GetAsync($"/api/v1/agent/disputes/{id}/evidence");
        using var customerDownload = await customer.GetAsync($"/api/v1/agent/disputes/{id}/evidence/{evidenceId}/content");
        Assert.Equal(HttpStatusCode.Forbidden, customerList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customerDownload.StatusCode);
    }

    [Fact]
    public async Task UploadRequiresCsrfAndValidIdentifiers()
    {
        var id = await _app.SeedDisputeAsync();
        using var customer = await _app.SignInAsync("customer.demo");
        using var malformed = await UploadAsync(customer, Guid.Empty, Form(Pdf()));
        using var badRoute = await customer.PostAsync("/api/v1/disputes/not-a-guid/evidence", Form(Pdf()));
        using var badEvidence = await customer.GetAsync($"/api/v1/disputes/{id}/evidence/not-a-guid/content");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRoute.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badEvidence.StatusCode);

        customer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var noCsrf = await UploadAsync(customer, id, Form(Pdf()));
        using var body = await UxApp.ReadJsonAsync(noCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal("auth.csrfInvalid", UxApp.Code(body));
        Assert.Equal(0, await StoredCountAsync(id));
    }
}
