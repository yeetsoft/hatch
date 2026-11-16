using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="AddProjectPriority"/>'s <c>Up()</c>/<c>Down()</c>, run against a
/// bare <see cref="MigrationBuilder"/> rather than a database - the same probe
/// shape <see cref="AddDreamRemandsMigrationTests"/> uses.
/// </summary>
public class AddProjectPriorityMigrationTests
{
    [Fact]
    public void Up_AddsThreeColumnsToProjects()
    {
        var columns = Up.OfType<AddColumnOperation>().ToList();

        Assert.Equal(3, Up.Count);
        Assert.All(columns, c => Assert.Equal("Projects", c.Table));
        Assert.Equal(["Priority", "PriorityAt", "PriorityBy"], columns.Select(c => c.Name).Order());
    }

    [Fact]
    public void Up_DefaultsTheLevelToNormalAndNeverNull()
    {
        var level = Up.OfType<AddColumnOperation>().Single(c => c.Name == "Priority");

        Assert.Equal("normal", level.DefaultValue);
        Assert.False(level.IsNullable);
    }

    [Fact]
    public void Up_LeavesWhoAndWhenNullable_AndCapsTheName()
    {
        var by = Up.OfType<AddColumnOperation>().Single(c => c.Name == "PriorityBy");
        var at = Up.OfType<AddColumnOperation>().Single(c => c.Name == "PriorityAt");

        Assert.True(by.IsNullable);
        Assert.Equal(240, by.MaxLength);
        Assert.True(at.IsNullable);
    }

    [Fact]
    public void Down_DropsTheThreeColumns()
    {
        var drops = Down.OfType<DropColumnOperation>().ToList();

        Assert.Equal(3, Down.Count);
        Assert.All(drops, d => Assert.Equal("Projects", d.Table));
        Assert.Equal(["Priority", "PriorityAt", "PriorityBy"], drops.Select(d => d.Name).Order());
    }

    private static IReadOnlyList<MigrationOperation> Up => new AddProjectPriorityProbe().Forward;
    private static IReadOnlyList<MigrationOperation> Down => new AddProjectPriorityProbe().Backward;

    private sealed class AddProjectPriorityProbe : AddProjectPriority
    {
        public IReadOnlyList<MigrationOperation> Forward
        {
            get
            {
                var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
                Up(builder);
                return builder.Operations;
            }
        }

        public IReadOnlyList<MigrationOperation> Backward
        {
            get
            {
                var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
                Down(builder);
                return builder.Operations;
            }
        }
    }
}
