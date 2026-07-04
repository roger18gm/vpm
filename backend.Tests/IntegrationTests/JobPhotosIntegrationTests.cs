using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using VisionPaint.Models;
using VisionPaint.Tests.Infrastructure;
using Xunit;

namespace VisionPaint.Tests.IntegrationTests;

public sealed class JobPhotosIntegrationTests : IClassFixture<BackendIntegrationFixture>, IAsyncLifetime
{
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private readonly BackendIntegrationFixture _fixture;

    public JobPhotosIntegrationTests(BackendIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await TestDatabaseInitializer.ResetAsync(_fixture.Database.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UploadPhoto_creates_row_and_returns_metadata()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Photo job" });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TinyPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "test.png");
        form.Add(new StringContent("progress"), "photoKind");

        using var upload = await _fixture.Client.PostAsync($"/api/jobs/{job!.Id}/photos", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var list = await _fixture.Client.GetFromJsonAsync<List<JobPhotoDto>>($"/api/jobs/{job.Id}/photos");
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("progress", list[0].PhotoKind);
    }

    [Fact]
    public async Task GetJobs_includes_photo_count_for_each_job()
    {
        var owner = await _fixture.AuthClient.BootstrapAsync(new BootstrapRequest(
            "VisionPaint Owner",
            $"owner-{Guid.NewGuid():N}@example.com",
            "Password123!"));

        _fixture.AuthClient.SetBearerToken(owner.AccessToken);
        var created = await _fixture.Client.PostAsJsonAsync("/api/jobs", new { title = "Counted job" });
        created.EnsureSuccessStatusCode();
        var job = await created.Content.ReadFromJsonAsync<Job>();

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TinyPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "test.png");
        form.Add(new StringContent("before"), "photoKind");

        using var upload = await _fixture.Client.PostAsync($"/api/jobs/{job!.Id}/photos", form);
        upload.EnsureSuccessStatusCode();

        var jobs = await _fixture.Client.GetFromJsonAsync<List<JobListItemResponse>>("/api/jobs");
        Assert.NotNull(jobs);
        var listed = Assert.Single(jobs!, item => item.Id == job.Id);
        Assert.Equal(1, listed.PhotoCount);
    }

    [Fact]
    public async Task LocalFiles_rejects_paths_outside_photo_root()
    {
        var escapedFileName = $"visionpaint-escape-{Guid.NewGuid():N}.txt";
        var escapedPath = Path.Combine(Path.GetTempPath(), escapedFileName);
        await File.WriteAllTextAsync(escapedPath, "not a photo");

        try
        {
            var escapedStoragePath = Uri.EscapeDataString($"../{escapedFileName}");
            using var response = await _fixture.Client.GetAsync($"/api/local-files/{escapedStoragePath}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            File.Delete(escapedPath);
        }
    }
}
