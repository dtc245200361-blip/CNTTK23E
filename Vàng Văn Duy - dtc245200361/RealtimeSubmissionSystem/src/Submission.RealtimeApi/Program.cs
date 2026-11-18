using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();

var assignments = new List<AssignmentDto>
{
    new(1, "Bai tap SignalR", true)
};

var submissions = new List<SubmissionDto>();
var submissionsLock = new object();

app.MapGet("/", () => Results.Ok(new
{
    service = "EduSubmit Realtime API",
    status = "running"
}));

app.MapGet("/api/assignments", () => Results.Ok(assignments));

app.MapPost("/api/assignments/{assignmentId:int}/submissions",
    (int assignmentId,
     CreateSubmissionRequest? request,
     IHubContext<SubmissionHub> hubContext) =>
{
    var assignment = assignments.FirstOrDefault(a => a.Id == assignmentId);
    if (assignment is null)
        return Results.NotFound(new ApiError("ASSIGNMENT_NOT_FOUND", "Khong tim thay bai tap."));

    if (!assignment.IsOpen)
        return Results.BadRequest(new ApiError("ASSIGNMENT_CLOSED", "Bai tap da dong."));

    if (request is null ||
        string.IsNullOrWhiteSpace(request.StudentCode) ||
        string.IsNullOrWhiteSpace(request.RepositoryUrl) ||
        !Uri.TryCreate(request.RepositoryUrl, UriKind.Absolute, out var repoUri) ||
        (repoUri.Scheme != Uri.UriSchemeHttp && repoUri.Scheme != Uri.UriSchemeHttps))
    {
        return Results.BadRequest(new ApiError(
            "INVALID_SUBMISSION",
            "studentCode va repositoryUrl la bat buoc; repositoryUrl phai la URL http/https."));
    }

    var now = DateTimeOffset.UtcNow;
    var submission = new SubmissionDto(
        Guid.NewGuid(),
        assignmentId,
        request.StudentCode.Trim(),
        request.RepositoryUrl.Trim(),
        "received",
        now,
        now);

    lock (submissionsLock)
    {
        submissions.Add(submission);
    }

    // Mo phong tac vu cham nen; POST tra 201 ngay, khong cho cham xong.
    _ = SimulateGradingAsync(submission.Id, submissions, submissionsLock, hubContext);

    return Results.Created($"/api/submissions/{submission.Id}", submission);
});

app.MapGet("/api/submissions/{submissionId:guid}", (Guid submissionId) =>
{
    lock (submissionsLock)
    {
        var submission = submissions.FirstOrDefault(s => s.Id == submissionId);
        return submission is null
            ? Results.NotFound(new ApiError("SUBMISSION_NOT_FOUND", "Khong tim thay luot nop."))
            : Results.Ok(submission);
    }
});

app.MapHub<SubmissionHub>("/hubs/submissions");
app.Run();

static async Task SimulateGradingAsync(
    Guid submissionId,
    List<SubmissionDto> submissions,
    object submissionsLock,
    IHubContext<SubmissionHub> hubContext)
{
    await Task.Delay(TimeSpan.FromSeconds(1));
    await UpdateAndNotifyAsync("processing");

    await Task.Delay(TimeSpan.FromSeconds(2));
    await UpdateAndNotifyAsync("graded");

    async Task UpdateAndNotifyAsync(string status)
    {
        SubmissionDto? updated;
        lock (submissionsLock)
        {
            var index = submissions.FindIndex(s => s.Id == submissionId);
            if (index < 0) return;

            updated = submissions[index] with
            {
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            submissions[index] = updated;
        }

        await hubContext.Clients
            .Group(SubmissionGroups.ForSubmission(submissionId))
            .SendAsync("SubmissionStatusChanged",
                new SubmissionStatusMessage(updated.Id, updated.Status, updated.UpdatedAt));
    }
}

sealed class SubmissionHub : Hub
{
    public Task WatchSubmission(string submissionId)
    {
        if (!Guid.TryParse(submissionId, out var id))
            throw new HubException("submissionId khong hop le.");

        return Groups.AddToGroupAsync(
            Context.ConnectionId,
            SubmissionGroups.ForSubmission(id));
    }
}

static class SubmissionGroups
{
    public static string ForSubmission(Guid id) => ForSubmission(id.ToString());
    public static string ForSubmission(string id) => $"submission:{id}";
}

record AssignmentDto(int Id, string Title, bool IsOpen);
record CreateSubmissionRequest(string StudentCode, string RepositoryUrl);
record SubmissionDto(
    Guid Id,
    int AssignmentId,
    string StudentCode,
    string RepositoryUrl,
    string Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt);
record SubmissionStatusMessage(Guid SubmissionId, string Status, DateTimeOffset UpdatedAt);
record ApiError(string Code, string Message);
