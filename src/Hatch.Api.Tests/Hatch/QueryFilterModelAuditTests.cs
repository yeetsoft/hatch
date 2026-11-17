using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The model, audited the way <c>AdminSurfaceTests</c> audits the routes: every
/// table that reaches a project says so with a query filter, or is named here
/// with the reason it does not. A table added next month that reaches a project
/// and carries nothing fails this until somebody decides.
/// </summary>
/// <remarks>
/// "Reaches a project" is a foreign-key path to <c>Projects</c>, of any length,
/// or a <c>Canonical</c> column - a repository, which a project binds. The
/// filters themselves are <see cref="HatchContext"/>'s; what they let through
/// is <see cref="ScopeEnforcementTests"/>'s.
/// </remarks>
public class QueryFilterModelAuditTests
{
    /// <summary>
    /// Tables that reach a project and carry no filter, each with the reason.
    /// The first test asserts every one is still unfiltered and still real, so
    /// adding a filter to one of them is a deliberate edit to this list.
    /// </summary>
    private static readonly Dictionary<string, string> Unfiltered = new()
    {
        ["EfHatchPersonScopeProject"] =
            "The projects a person's scope names. The resolver reads it to learn what a caller may see, so a "
            + "filter on it would ask the answer to find itself; it holds ids and no content, and only the "
            + "people-and-scopes routes, which are an admin's alone, return it.",
    };

    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new HatchContext(options);
        return db.Model;
    }

    [Fact]
    public void EveryTableThatReachesAProject_CarriesAFilter_OrIsNamedWithAReason()
    {
        var model = Model();
        var projects = model.FindEntityType(typeof(EfHatchProject))!;

        var bare = model.GetEntityTypes()
            .Where(e => Reaches(e, projects, []) || e.FindProperty("Canonical") is not null)
            .Where(e => !HasFilter(e))
            .Select(e => e.ClrType.Name)
            .Where(name => !Unfiltered.ContainsKey(name))
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            bare.Count == 0,
            "These tables reach a project, or name a repository, and carry no query filter: "
            + string.Join(", ", bare)
            + ". Add one in HatchContext.OnModelCreating, or list the table in QueryFilterModelAuditTests.Unfiltered with the reason.");
    }

    [Fact]
    public void EveryNamedException_IsStillARealTable_StillUnfiltered_AndStillReachesAProject()
    {
        var model = Model();
        var projects = model.FindEntityType(typeof(EfHatchProject))!;

        foreach (var name in Unfiltered.Keys)
        {
            var entity = model.GetEntityTypes().SingleOrDefault(e => e.ClrType.Name == name);

            Assert.True(entity is not null, $"{name} is on the unfiltered list and is not a table any more");
            Assert.False(HasFilter(entity!), $"{name} is on the unfiltered list and now carries a filter - take it off the list");
            Assert.True(
                Reaches(entity!, projects, []) || entity!.FindProperty("Canonical") is not null,
                $"{name} is on the unfiltered list and no longer reaches a project - take it off the list");
        }
    }

    /// <summary>
    /// The tables the brief names as unfiltered, held to it: they reach no
    /// project, so the audit above never asks about them, and this is what
    /// notices if one starts to.
    /// </summary>
    [Theory]
    [InlineData(typeof(EfHatchStatus))]
    [InlineData(typeof(EfHatchWipLimit))]
    [InlineData(typeof(EfHatchOvernightSeen))]
    [InlineData(typeof(EfHatchPersonScope))]
    [InlineData(typeof(EfHatchRunner))]
    [InlineData(typeof(EfHatchRunnerConfig))]
    [InlineData(typeof(EfHatchRunnerSession))]
    public void TheTablesTheBriefLeavesOpen_CarryNoFilter_AndReachNoProject(Type clr)
    {
        var model = Model();
        var projects = model.FindEntityType(typeof(EfHatchProject))!;
        var entity = model.FindEntityType(clr)!;

        Assert.False(HasFilter(entity), $"{clr.Name} carries a filter and is meant to be open");
        Assert.False(Reaches(entity, projects, []), $"{clr.Name} now reaches a project, so it needs a filter or a reason");
        Assert.Null(entity.FindProperty("Canonical"));
    }

    private static bool HasFilter(IEntityType entity) => entity.GetDeclaredQueryFilters().Count > 0;

    /// <summary>A foreign-key path from <paramref name="from"/> to <paramref name="target"/>, of any length.</summary>
    private static bool Reaches(IEntityType from, IEntityType target, HashSet<IEntityType> seen)
    {
        if (!seen.Add(from)) return false;

        foreach (var key in from.GetForeignKeys())
        {
            var principal = key.PrincipalEntityType;
            if (principal == target || Reaches(principal, target, seen)) return true;
        }

        return false;
    }
}
