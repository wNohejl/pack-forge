using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using PackForge.Core;
using PackForge.Core.Models;
using PackForge.Web.Builds;
using PackForge.Web.Components;
using PackForge.Web.Data;
using PackForge.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<PackForgeDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton(new BlobServiceClient(builder.Configuration.GetConnectionString("Blob")));
builder.Services.AddSingleton<BlobStorageService>();
builder.Services.AddSingleton(new QueueClient(builder.Configuration.GetConnectionString("Blob"), BuildWorker.QueueName));
builder.Services.AddHostedService<BuildWorker>();
builder.Services.AddHttpClient();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ---- Upload API ------------------------------------------------------------

app.MapPost("/api/uploads/begin", async (BeginUploadRequest req, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs) =>
{
    if (string.IsNullOrWhiteSpace(req.FileName) || req.SizeBytes <= 0)
        return Results.BadRequest("fileName and a positive sizeBytes are required.");

    var upload = new Upload
    {
        Id = Guid.NewGuid(),
        FileName = Path.GetFileName(req.FileName),
        SizeBytes = req.SizeBytes,
    };
    upload.BlobName = $"{upload.Id}/{upload.FileName}";

    await using var db = await dbf.CreateDbContextAsync();
    db.Uploads.Add(upload);
    await db.SaveChangesAsync();

    return Results.Ok(new { id = upload.Id, uploadUrl = blobs.GetUploadSasUri(upload.BlobName).ToString() });
});

app.MapPost("/api/uploads/{id:guid}/complete", async (Guid id, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var upload = await db.Uploads.FindAsync(id);
    if (upload is null)
        return Results.NotFound();

    var props = await blobs.GetBlobPropertiesAsync(BlobStorageService.ModelsContainer, upload.BlobName);
    if (props is null || props.ContentLength != upload.SizeBytes)
    {
        upload.Status = UploadStatus.Failed;
        await db.SaveChangesAsync();
        return Results.BadRequest($"Blob missing or size mismatch (expected {upload.SizeBytes}, got {props?.ContentLength ?? 0}).");
    }

    upload.Sha256 = await blobs.ComputeSha256Async(BlobStorageService.ModelsContainer, upload.BlobName);
    upload.Status = UploadStatus.Ready;
    await db.SaveChangesAsync();

    return Results.Ok(new { upload.Id, upload.Sha256, status = upload.Status.ToString() });
});

app.MapGet("/api/uploads", async (IDbContextFactory<PackForgeDbContext> dbf) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    return await db.Uploads.OrderByDescending(u => u.CreatedAt).Take(100).ToListAsync();
});

// Redirects to a short-lived read SAS — the download also bypasses the app server.
app.MapGet("/api/uploads/{id:guid}/download", async (Guid id, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var upload = await db.Uploads.FindAsync(id);
    return upload is null or { Status: not UploadStatus.Ready }
        ? Results.NotFound()
        : Results.Redirect(blobs.GetDownloadSasUri(BlobStorageService.ModelsContainer, upload.BlobName).ToString());
});

// ---- Package builds ----------------------------------------------------------

app.MapPost("/api/packages", async (BuildRequest req, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs, QueueClient queue) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var upload = await db.Uploads.FindAsync(req.UploadId);
    if (upload is null or { Status: not UploadStatus.Ready })
        return Results.BadRequest("Upload not found or not ready.");
    if (upload.SizeBytes > 10 * 1024 * 1024)
        return Results.BadRequest("Model definitions are limited to 10 MB.");

    ModelDefinition model;
    try
    {
        model = ModelDefinition.FromJson(await blobs.DownloadTextAsync(BlobStorageService.ModelsContainer, upload.BlobName));
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Not a valid model definition: {ex.Message}");
    }

    var errors = ModelValidator.Validate(model);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors
            .GroupBy(e => e.Target)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));

    var modelSha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(model.ToJson())));

    // Reproducibility contract: identical model content maps to the existing package.
    var existing = await db.PackageBuilds
        .Where(b => b.ModelName == model.Name && b.ModelSha256 == modelSha && b.Status != BuildStatus.Failed)
        .OrderBy(b => b.Version)
        .FirstOrDefaultAsync();
    if (existing is not null)
        return Results.Ok(new { existing.Id, existing.Version, reused = true });

    var version = (await db.PackageBuilds.Where(b => b.ModelName == model.Name).MaxAsync(b => (int?)b.Version)) + 1 ?? 1;
    var build = new PackageBuild
    {
        Id = Guid.NewGuid(),
        UploadId = upload.Id,
        ModelName = model.Name,
        Version = version,
        ModelSha256 = modelSha,
    };
    db.PackageBuilds.Add(build);
    await db.SaveChangesAsync();
    await queue.SendMessageAsync(build.Id.ToString());

    return Results.Ok(new { build.Id, build.Version, reused = false });
});

app.MapGet("/api/packages", async (IDbContextFactory<PackForgeDbContext> dbf) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    return await db.PackageBuilds.OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync();
});

app.MapGet("/api/packages/{id:guid}/download", async (Guid id, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var build = await db.PackageBuilds.FindAsync(id);
    return build is null or { Status: not BuildStatus.Ready } || build.BlobName is null
        ? Results.NotFound()
        : Results.Redirect(blobs.GetDownloadSasUri(BlobStorageService.PackagesContainer, build.BlobName).ToString());
});

// ---- Startup: schema + dev storage bootstrap --------------------------------

using (var scope = app.Services.CreateScope())
{
    var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<PackForgeDbContext>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
        await scope.ServiceProvider.GetRequiredService<BlobStorageService>().InitializeDevAsync();
}

app.Run();

public record BeginUploadRequest(string FileName, long SizeBytes);

public record BuildRequest(Guid UploadId);
