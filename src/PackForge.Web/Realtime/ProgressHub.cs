using Microsoft.AspNetCore.SignalR;

namespace PackForge.Web.Realtime;

/// <summary>
/// SignalR hub for pushing build and migration progress to connected clients —
/// replaces UI polling. Server-side producers (build worker, migration service)
/// broadcast through IHubContext; the Blazor pages subscribe with a HubConnection.
/// </summary>
public class ProgressHub : Hub
{
    public const string BuildsChanged = "BuildsChanged";
    public const string MigrationChanged = "MigrationChanged";
}

/// <summary>Thin wrapper so producers don't take a hard dependency on SignalR types.</summary>
public class ProgressNotifier(IHubContext<ProgressHub> hub)
{
    public Task BuildsChangedAsync() => hub.Clients.All.SendAsync(ProgressHub.BuildsChanged);
    public Task MigrationChangedAsync() => hub.Clients.All.SendAsync(ProgressHub.MigrationChanged);
}
