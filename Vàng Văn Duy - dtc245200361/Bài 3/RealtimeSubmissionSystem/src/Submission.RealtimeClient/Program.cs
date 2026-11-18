using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

var apiBaseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://localhost:5000";
var studentCode = args.Length > 1 ? args[1] : "SV001";

using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
var gradedNotification = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);

Guid? watchedSubmissionId = null;

await using var connection = new HubConnectionBuilder()
    .WithUrl($"{apiBaseUrl}/hubs/submissions")
    .WithAutomaticReconnect()
    .Build();

connection.On<SubmissionStatusMessage>("SubmissionStatusChanged", message =>
{
    Console.WriteLine(
        $"Thong bao SignalR: {ShortId(message.SubmissionId)} -> {message.Status} ({message.UpdatedAt:O})");

    if (message.Status == "graded")
        gradedNotification.TrySetResult();
});

connection.Reconnecting += error =>
{
    Console.WriteLine("SignalR dang ket noi lai...");
    return Task.CompletedTask;
};

connection.Reconnected += async newConnectionId =>
{
    Console.WriteLine($"SignalR da ket noi lai: {newConnectionId}");
    if (watchedSubmissionId is Guid id)
        await connection.InvokeAsync("WatchSubmission", id.ToString());
};

await connection.StartAsync();
Console.WriteLine("Da ket noi SignalR.");

var assignments = await http.GetFromJsonAsync<List<AssignmentDto>>("/api/assignments");
var selected = assignments?.FirstOrDefault();

if (selected is null)
{
    Console.WriteLine("Khong co bai tap de nop.");
    return;
}

var createResponse = await http.PostAsJsonAsync(
    $"/api/assignments/{selected.Id}/submissions",
    new CreateSubmissionRequest(
        studentCode,
        $"https://example.com/{Uri.EscapeDataString(studentCode)}/signalr"));

if (createResponse.StatusCode != HttpStatusCode.Created)
{
    var error = await createResponse.Content.ReadAsStringAsync();
    Console.WriteLine($"Tao luot nop that bai: {(int)createResponse.StatusCode} {error}");
    return;
}

var created = await createResponse.Content.ReadFromJsonAsync<SubmissionDto>();
if (created is null)
{
    Console.WriteLine("Khong doc duoc phan hoi tao luot nop.");
    return;
}

watchedSubmissionId = created.Id;
Console.WriteLine($"Da tao luot nop: {created.Id}");
Console.WriteLine($"Trang thai REST ban dau: {created.Status}");

await connection.InvokeAsync("WatchSubmission", created.Id.ToString());

var completed = await Task.WhenAny(
    gradedNotification.Task,
    Task.Delay(TimeSpan.FromSeconds(10)));

if (completed != gradedNotification.Task)
    Console.WriteLine("Qua thoi gian cho thong bao graded.");

var latest = await http.GetFromJsonAsync<SubmissionDto>(
    $"/api/submissions/{created.Id}");

Console.WriteLine($"Trang thai doc lai bang REST: {latest?.Status}");

static string ShortId(Guid id) => id.ToString()[..8];

record AssignmentDto(int Id, string Title, bool IsOpen);
record CreateSubmissionRequest(string StudentCode, string RepositoryUrl);
record SubmissionDto(
    Guid Id, int AssignmentId, string StudentCode, string RepositoryUrl,
    string Status, DateTimeOffset ReceivedAt, DateTimeOffset UpdatedAt);
record SubmissionStatusMessage(Guid SubmissionId, string Status, DateTimeOffset UpdatedAt);
