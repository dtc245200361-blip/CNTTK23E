using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "PingApi",
    status = "running"
}));

app.MapHub<PingHub>("/hubs/ping");
app.Run();

sealed class PingHub : Hub
{
    public async Task Echo(string message)
    {
        await Clients.Caller.SendAsync("EchoResponse", $"Echo: {message}");
    }

    public async Task Broadcast(string message)
    {
        await Clients.All.SendAsync("BroadcastMessage", message);
    }

    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        await Clients.Group(groupName).SendAsync(
            "GroupMessage",
            $"Client {ShortId(Context.ConnectionId)} da tham gia nhom {groupName}");
    }

    public async Task SendToGroup(string groupName, string message)
    {
        await Clients.Group(groupName).SendAsync("GroupMessage", message);
    }

    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"[Hub] + {ShortId(Context.ConnectionId)} ket noi");
        await Clients.All.SendAsync(
            "SystemMessage",
            $"Client {ShortId(Context.ConnectionId)} da ket noi.");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Console.WriteLine($"[Hub] - {ShortId(Context.ConnectionId)} ngat");
        await Clients.All.SendAsync(
            "SystemMessage",
            $"Client {ShortId(Context.ConnectionId)} da ngat ket noi.");
        await base.OnDisconnectedAsync(exception);
    }

    private static string ShortId(string id) =>
        id.Length > 8 ? id[..8] : id;
}
