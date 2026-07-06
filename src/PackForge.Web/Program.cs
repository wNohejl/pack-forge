using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using PackForge.Core;
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

    var props = await blobs.GetBlobPropertiesAsync(upload.BlobName);
    if (props is null || props.ContentLength != upload.SizeBytes)
    {
        upload.Status = UploadStatus.Failed;
        await db.SaveChangesAsync();
        return Results.BadRequest($"Blob missing or size mismatch (expected {upload.SizeBytes}, got {props?.ContentLength ?? 0}).");
    }

    upload.Sha256 = await blobs.ComputeSha256Async(upload.BlobName);
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
        : Results.Redirect(blobs.GetDownloadSasUri(upload.BlobName).ToString());
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
