using System.Text.Json;
using Hatch.Api.Jobs;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Files a reflection in every project whose cadence
/// (<see cref="EfHatchProject.ReflectionCadenceDays"/>,
/// <see cref="EfHatchProject.ReflectionLastPeriodStart"/>) is due.
///
/// Hatch's own job, reading Hatch's own context, so it sits in
/// <c>Modules/Hatch</c> rather than <c>Jobs</c> (Modules/README.md) - unlike
/// the house jobs registered in <c>Program.cs</c>, which aren't a module's.
///
/// Never <see cref="IssuesController.CreateIssueAsync"/>: that path resolves
/// its actor through <c>ICallerIdentity.ActorNameAsync</c>, which falls back
/// to "operator" the moment there is no HTTP request to read - true on every
/// Quartz firing. The epic's decision is that a reflection is filed by the
/// board as actor, so this job writes the issue and its <c>created</c> event
/// directly, copying <c>CreateIssueAsync</c>'s mint shape with
/// <see cref="Actor"/> standing in for the resolved name.
/// </summary>
public class ReflectionCadenceJob(HatchContext db, RankService ranks, TimeProvider time, ILogger<ReflectionCadenceJob> logger) : IAppJob
{
    public const string Actor = "the board";

    /// <summary>Same bound as <c>IssuesController.MintAttempts</c>, for the same reason.</summary>
    private const int MintAttempts = 5;

    public string Name => "ReflectionCadence";

    public string Group => "Hatch.Api";

    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public bool ServesTheHouse => false;

    public Task Execute(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();

        var projects = await db.Projects.Where(p => p.ReflectionCadenceDays != null).ToListAsync(ct);
        foreach (var project in projects)
        {
            if (!ReflectionCadence.IsDue(project.ReflectionCadenceDays, project.ReflectionLastPeriodStart, now)) continue;

            var playbooks = await db.Playbooks.Where(p => p.ProjectId == null || p.ProjectId == project.Id).ToListAsync(ct);
            var row = Dispatch.MatchReflection(playbooks, project.Id);
            if (row is null)
            {
                logger.LogWarning("Project {ProjectKey} has a reflection cadence due but no playbook row covers it", project.Key);
                continue;
            }

            var windowEnd = ReflectionCadence.NextDue(project.ReflectionCadenceDays, project.ReflectionLastPeriodStart, now)!.Value;
            var windowStart = windowEnd.AddDays(-project.ReflectionCadenceDays!.Value);
            var title = $"Reflection: {windowStart:yyyy-MM-dd} to {windowEnd:yyyy-MM-dd}";
            var description = $"The reflection for {project.Key} covering {windowStart:yyyy-MM-dd} to {windowEnd:yyyy-MM-dd}.";

            var filed = false;
            for (var attempt = 1; !filed; attempt++)
            {
                var current = await db.Projects.FirstAsync(p => p.Id == project.Id, ct);

                var issue = new EfHatchIssue
                {
                    ProjectId = current.Id,
                    Number = current.NextIssueNumber,
                    Type = "reflection",
                    Title = title,
                    Description = description,
                    StatusId = row.FromStatusId,
                    ParentId = null,
                    Rank = await ranks.BottomAsync(row.FromStatusId, ct),
                    CreatedBy = Actor,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                issue.Events.Add(Event(Actor, EfHatchIssueEvent.Created, new { type = issue.Type, title }, now));

                current.NextIssueNumber++;
                current.ReflectionLastPeriodStart = windowEnd;
                db.Issues.Add(issue);

                try
                {
                    await db.SaveChangesAsync(ct);
                    filed = true;
                }
                catch (DbUpdateException) when (attempt < MintAttempts)
                {
                    db.ChangeTracker.Clear();
                }
                catch (DbUpdateException)
                {
                    logger.LogWarning("Project {ProjectKey} could not mint a reflection issue after {Attempts} attempts - left for the next firing", project.Key, MintAttempts);
                    break;
                }
            }
        }
    }

    private static EfHatchIssueEvent Event(string actor, string kind, object? payload, DateTimeOffset at) => new()
    {
        Actor = actor,
        Kind = kind,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload),
        At = at,
    };
}
