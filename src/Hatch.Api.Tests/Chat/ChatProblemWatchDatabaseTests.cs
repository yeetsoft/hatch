using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Chat;

/// <summary>
/// The half of the problem watch the database owns: the unique index on the
/// problems that have not ended, which keeps two instances from announcing one
/// stoppage twice. Skips without a Postgres; run it with <c>make test-api-db</c>.
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class ChatProblemWatchDatabaseTests
{
    private static readonly DateTimeOffset Now = ChatOutboxTests.Now;

    [SkippableFact]
    public async Task TheIndex_RefusesASecondOpenProblemOfAKind_AndAllowsOneOnceTheFirstEnds()
    {
        Skip.IfNot(HatchDatabase.Available, "HATCH_TEST_DATABASE_URL is unset - run `make test-api-db`.");
        var connection = await HatchDatabase.PrepareAsync();

        await using var first = Open(connection);
        var open = Problem("goose", "goose:one");
        first.ChatProblems.Add(open);
        await first.SaveChangesAsync();

        // Another instance, which read nothing yet, announces the same stoppage.
        await using var second = Open(connection);
        second.ChatProblems.Add(Problem("goose", "goose:two"));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // A different kind is its own problem.
        await using var third = Open(connection);
        third.ChatProblems.Add(Problem("backups", "backups:one"));
        await third.SaveChangesAsync();

        open.EndedAt = Now.AddMinutes(10);
        await first.SaveChangesAsync();

        await using var fourth = Open(connection);
        fourth.ChatProblems.Add(Problem("goose", "goose:three"));
        await fourth.SaveChangesAsync();

        await using var read = Open(connection);
        var goose = await read.ChatProblems.Where(p => p.Kind == "goose").OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(["goose:one", "goose:three"], goose.Select(p => p.Subject));
        Assert.Equal([false, true], goose.Select(p => p.EndedAt is null));
    }

    private static EfHatchChatProblem Problem(string kind, string subject) =>
        new() { Kind = kind, Subject = subject, BeganAt = Now, Sentence = "the sentence" };

    private static HatchContext Open(string connection) =>
        new(new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connection).Options);
}
