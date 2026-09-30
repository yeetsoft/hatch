using Hatch.Api.Ef;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hatch.Api.Tests.Modules;

/// <summary>
/// Every context's model agrees with its migrations' snapshot - the check EF
/// runs at the head of <c>MigrateAsync</c>, asserted here because nothing else
/// in the suite ever calls it. The test databases are built with
/// <c>EnsureCreated</c> straight from the model, so a model change with no
/// migration behind it passes every other test and then fails the migrate Job
/// with <c>PendingModelChangesWarning</c>, leaving the new pods serving a
/// schema nobody applied. That is what HA-150's <c>HasDefaultValue</c> on
/// <c>Playbooks.Shape</c> did.
///
/// <para>Needs no database: the comparison is between two in-memory models, and
/// the connection string the design-time factories carry is never opened.</para>
/// </summary>
public class ModelSnapshotTests
{
    /// <summary>
    /// Every context <c>dotnet ef</c> can build, found the way <c>dotnet ef</c>
    /// finds them, so a new module is covered by adding its design-time factory
    /// and nothing here.
    /// </summary>
    public static TheoryData<string> Factories()
    {
        var data = new TheoryData<string>();
        foreach (var type in FactoryTypes())
            data.Add(type.FullName!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TheModelHasNoChangesItsSnapshotDoesNotKnowAbout(string factory)
    {
        var type = FactoryTypes().Single(t => t.FullName == factory);
        var create = type.GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>))
            .GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;

        using var context = (DbContext)create.Invoke(Activator.CreateInstance(type), [Array.Empty<string>()])!;

        Assert.False(
            context.Database.HasPendingModelChanges(),
            $"{context.GetType().Name} has changes its model snapshot does not: add a migration for them.");
    }

    /// <summary>The reflection above finding nothing would pass the theory by running no case at all.</summary>
    [Fact]
    public void TheSearchFindsTheCoreContextAndEveryModule()
    {
        var found = FactoryTypes().ToList();
        Assert.Contains(typeof(DesignTimeDbContextFactory), found);
        Assert.Contains(typeof(Api.Modules.Hatch.HatchDesignTimeFactory), found);
    }

    private static IEnumerable<Type> FactoryTypes() =>
        typeof(DesignTimeDbContextFactory).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)));
}
