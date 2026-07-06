using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using PackForge.Core;
using PackForge.Core.Migration;
using PackForge.Core.Models;
using PackForge.Web.Auth;
using PackForge.Web.Builds;
using PackForge.Web.Backfill;
using PackForge.Web.Observability;
using PackForge.Web.Realtime;
using PackForge.Web.Components;
using MudBlazor.Services;
using PackForge.Web.Data;
using PackForge.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();
builder.Services.AddSingleton<ProgressNotifier>();
builder.Services.AddMudServices();

builder.Services.AddPackForgeObservability(builder.Configuration);
var authEnabled = builder.Services.AddEntraAuthentication(builder.Configuration);

builder.Services.AddDbContextFactory<PackForgeDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Storage: connection string in dev (Azurite); account URL + managed identity in Azure.
var blobConn = builder.Configuration.GetConnectionString("Blob")!;
var queueUri = builder.Configuration["Storage:QueueUri"];
if (blobConn.StartsWith("http", StringComparison.OrdinalIgnoreCase))
{
    var credential = new DefaultAzureCredential();
    builder.Services.AddSingleton(new BlobServiceClient(new Uri(blobConn), credential));
    builder.Services.AddSingleton(new QueueClient(new Uri($"{queueUri!.TrimEnd('/')}/{BuildWorker.QueueName}"), credential));
    builder.Services.AddKeyedSingleton(BuildWorker.PoisonQueueKey, new QueueClient(new Uri($"{queueUri!.TrimEnd('/')}/{BuildWorker.PoisonQueueName}"), credential));
}
else
{
    builder.Services.AddSingleton(new BlobServiceClient(blobConn));
    builder.Services.AddSingleton(new QueueClient(blobConn, BuildWorker.QueueName));
    builder.Services.AddKeyedSingleton(BuildWorker.PoisonQueueKey, new QueueClient(blobConn, BuildWorker.PoisonQueueName));
}
builder.Services.AddSingleton<BlobStorageService>();
builder.Services.AddHostedService<BuildWorker>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHttpClient();

var legacyRoot = builder.Configuration["Migration:LegacyRoot"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".legacy-share"));
builder.Services.AddSingleton<IMigrationSource>(new LocalFolderSource("fileshare", Path.Combine(legacyRoot, "fileshare")));
builder.Services.AddSingleton<IMigrationSource>(new ThrottledGraphSource("sharepoint", Path.Combine(legacyRoot, "sharepoint")));
builder.Services.AddSingleton<MigrationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<ProgressHub>("/hubs/progress");

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

app.MapPost("/api/packages", async (BuildRequest req, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs) =>
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
    // Outbox: the build row and its enqueue intent commit together. The dispatcher
    // relays it to the queue — no orphaned build if we crash before the send.
    db.OutboxMessages.Add(new OutboxMessage { QueueName = BuildWorker.QueueName, Body = build.Id.ToString() });
    await db.SaveChangesAsync();

    return Results.Ok(new { build.Id, build.Version, reused = false });
});

app.MapGet("/api/packages", async (IDbContextFactory<PackForgeDbContext> dbf) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    return await db.PackageBuilds.OrderByDescending(b => b.CreatedAt).Take(100).ToListAsync();
});

// Release gate: a package can only be published if it built successfully AND its
// stored checksum still matches the bytes in blob storage (no drift, no tampering).
app.MapPost("/api/packages/{id:guid}/publish", async (Guid id, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var build = await db.PackageBuilds.FindAsync(id);
    if (build is null)
        return Results.NotFound();
    if (build.Status != BuildStatus.Ready || build.BlobName is null || build.PackageSha256 is null)
        return Results.BadRequest("Only successfully built packages can be published.");

    var actual = await blobs.ComputeSha256Async(BlobStorageService.PackagesContainer, build.BlobName);
    if (actual != build.PackageSha256)
        return Results.BadRequest($"Release gate failed: checksum mismatch (recorded {build.PackageSha256[..12]}…, blob {actual[..12]}…).");

    build.Published = true;
    build.PublishedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { build.Id, build.Published, build.PublishedAt });
});

app.MapGet("/api/packages/{id:guid}/download", async (Guid id, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var build = await db.PackageBuilds.FindAsync(id);
    return build is null or { Status: not BuildStatus.Ready } || build.BlobName is null
        ? Results.NotFound()
        : Results.Redirect(blobs.GetDownloadSasUri(BlobStorageService.PackagesContainer, build.BlobName).ToString());
});

// ---- Migration engine --------------------------------------------------------

app.MapPost("/api/migration/run", (MigrationService migration) =>
    migration.TryStart() ? Results.Accepted() : Results.Conflict("A migration run is already in progress."));

app.MapGet("/api/migration/report", async (MigrationService migration, IDbContextFactory<PackForgeDbContext> dbf) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var byStatus = await db.MigrationItems
        .GroupBy(i => new { i.SourceSystem, i.Status })
        .Select(g => new { g.Key.SourceSystem, Status = g.Key.Status.ToString(), Count = g.Count(), Bytes = g.Sum(i => i.SizeBytes) })
        .ToListAsync();
    var failures = await db.MigrationItems
        .Where(i => i.Status == MigrationStatus.Failed)
        .Select(i => new { i.SourceSystem, i.SourcePath, i.Error })
        .Take(50)
        .ToListAsync();
    var elapsed = migration.StartedAt is null ? 0
        : ((migration.FinishedAt ?? DateTimeOffset.UtcNow) - migration.StartedAt.Value).TotalSeconds;
    return Results.Ok(new
    {
        running = migration.Running,
        startedAt = migration.StartedAt,
        finishedAt = migration.FinishedAt,
        bytesCopied = migration.BytesCopied,
        throughputMBps = elapsed > 0 ? migration.BytesCopied / elapsed / (1 << 20) : 0,
        byStatus,
        failures,
    });
});

// Dual-read: verified items serve from blob (SAS redirect); everything else falls
// back to the legacy source, so nothing goes dark during the migration.
app.MapGet("/api/legacy/{system}/{**path}", async (string system, string path, IDbContextFactory<PackForgeDbContext> dbf, BlobStorageService blobs, MigrationService migration) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var item = await db.MigrationItems
        .FirstOrDefaultAsync(i => i.SourceSystem == system && i.SourcePath == path);

    if (item is { Status: MigrationStatus.Verified, BlobName: not null })
        return Results.Redirect(blobs.GetDownloadSasUri(BlobStorageService.MigratedContainer, item.BlobName).ToString());

    var source = migration.GetSource(system);
    if (source is null)
        return Results.NotFound();
    try
    {
        return Results.Stream(await source.OpenReadAsync(path), "application/octet-stream");
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

// ---- Startup: schema + dev storage bootstrap --------------------------------

using (var scope = app.Services.CreateScope())
{
    var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<PackForgeDbContext>>().CreateDbContextAsync();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
        await scope.ServiceProvider.GetRequiredService<BlobStorageService>().InitializeDevAsync();
}

app.Logger.LogInformation("Math kernel: {Kernel}",
    PackForge.Core.Expressions.NativeMathKernel.IsAvailable ? "native C++ (packforge_eval)" : "managed fallback");

app.Run();

public record BeginUploadRequest(string FileName, long SizeBytes);

public record BuildRequest(Guid UploadId);
