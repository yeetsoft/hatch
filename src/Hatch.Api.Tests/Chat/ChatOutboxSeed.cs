using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Chat;

/// <summary>Projects, workspaces, bindings and rooms for the queue tests, in memory or on Postgres.</summary>
internal sealed class ChatOutboxSeed(HatchContext db, DateTimeOffset now)
{
    public async Task<int> ProjectAsync(string key)
    {
        var project = new EfHatchProject { Key = key, Name = key, CreatedAt = now };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    public async Task<int> WorkspaceAsync(string externalId, params int[] boundProjects)
    {
        var workspace = new EfHatchChatWorkspace
        {
            Provider = "fake", ExternalId = externalId, Name = externalId, Token = "sealed", CreatedAt = now, UpdatedAt = now,
        };
        db.ChatWorkspaces.Add(workspace);
        await db.SaveChangesAsync();

        foreach (var project in boundProjects)
            db.ChatWorkspaceProjects.Add(new EfHatchChatWorkspaceProject { WorkspaceId = workspace.Id, ProjectId = project, CreatedAt = now });
        await db.SaveChangesAsync();
        return workspace.Id;
    }

    public async Task<int> RoomAsync(
        int workspace, string channel,
        bool changelog = false, bool questions = false, bool problems = false, bool unownedProblems = false,
        bool allProjects = true, int[]? chosen = null)
    {
        var room = new EfHatchChatRoom
        {
            WorkspaceId = workspace, ExternalId = channel, Name = channel,
            Changelog = changelog, Questions = questions, Problems = problems, UnownedProblems = unownedProblems,
            AllProjects = allProjects, CreatedAt = now, UpdatedAt = now,
        };
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync();

        foreach (var project in chosen ?? [])
            db.ChatRoomProjects.Add(new EfHatchChatRoomProject { RoomId = room.Id, WorkspaceId = workspace, ProjectId = project });
        await db.SaveChangesAsync();
        return room.Id;
    }
}
