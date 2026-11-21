using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="AddIssuePreviousTitles"/>'s <c>Up()</c>/<c>Down()</c>, lifted out and
/// run against a bare <see cref="MigrationBuilder"/> rather than a database - the
/// same probe shape <see cref="AddSearchVectorsMigrationTests"/> uses. The
/// backfill statement itself is <see cref="IssuePreviousTitlesBackfillTests"/>'s
/// business.
/// </summary>
public class AddIssuePreviousTitlesMigrationTests
{
    [Fact]
    public void Up_AddsThePreviousTitlesColumn()
    {
        var column = Up.OfType<AddColumnOperation>().Single();

        Assert.Equal("hatch", column.Schema);
        Assert.Equal("Issues", column.Table);
        Assert.Equal("PreviousTitles", column.Name);
        Assert.False(column.IsNullable);
        Assert.Equal("", column.DefaultValue);
    }

    [Fact]
    public void Up_CarriesOneBackfillStatement_OverTheIssuesAndIssueEventsTables()
    {
        var sql = Up.OfType<SqlOperation>().Single().Sql;

        Assert.Contains("hatch.\"Issues\"", sql);
        Assert.Contains("hatch.\"IssueEvents\"", sql);
        Assert.Contains("'retitled'", sql);
    }

    [Fact]
    public void Up_RebuildsSearchVector_CarryingAllThreeWeights()
    {
        var expression = Up.OfType<AlterColumnOperation>().Single(c => c.Name == "SearchVector").ComputedColumnSql;

        Assert.Contains("""setweight(to_tsvector('english', "Title"), 'A')""", expression);
        Assert.Contains("""setweight(to_tsvector('english', "Description"), 'B')""", expression);
        Assert.Contains("""setweight(to_tsvector('english', "PreviousTitles"), 'B')""", expression);
    }

    [Fact]
    public void Up_RunsTheBackfillBeforeTheColumnIsRebuilt()
    {
        var backfill = Up.ToList().FindIndex(o => o is SqlOperation);
        var rebuild = Up.ToList().FindIndex(o => o is AlterColumnOperation a && a.Name == "SearchVector");

        Assert.True(backfill < rebuild);
    }

    [Fact]
    public void Down_DropsThePreviousTitlesColumn_AndRestoresTheOldTwoWeightExpression()
    {
        Assert.Contains(Down, o => o is DropColumnOperation d && d.Name == "PreviousTitles");

        var expression = Down.OfType<AlterColumnOperation>().Single(c => c.Name == "SearchVector").ComputedColumnSql;

        Assert.Contains("""setweight(to_tsvector('english', "Title"), 'A')""", expression);
        Assert.Contains("""setweight(to_tsvector('english', "Description"), 'B')""", expression);
        Assert.DoesNotContain("PreviousTitles", expression);
    }

    private static IReadOnlyList<MigrationOperation> Up => new AddIssuePreviousTitlesProbe().Forward;
    private static IReadOnlyList<MigrationOperation> Down => new AddIssuePreviousTitlesProbe().Backward;

    private sealed class AddIssuePreviousTitlesProbe : AddIssuePreviousTitles
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
