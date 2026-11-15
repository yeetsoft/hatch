using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="AddDreamRemands"/>'s <c>Up()</c>/<c>Down()</c>, run against a
/// bare <see cref="MigrationBuilder"/> rather than a database - the same probe
/// shape <see cref="AddDreamsMigrationTests"/> uses.
/// </summary>
public class AddDreamRemandsMigrationTests
{
    [Fact]
    public void Up_CreatesDreamRemandsKeyedOnTheDreamAndTheIssue()
    {
        var table = Up.OfType<CreateTableOperation>().Single(t => t.Name == "DreamRemands");

        Assert.Equal(["DreamId", "IssueId"], table.PrimaryKey!.Columns);
        Assert.Contains(table.Columns, c => c.Name == "Root" && c.ClrType == typeof(bool) && !c.IsNullable);
    }

    [Fact]
    public void Up_CascadesBothForeignKeys()
    {
        var table = Up.OfType<CreateTableOperation>().Single(t => t.Name == "DreamRemands");

        Assert.Equal(2, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, f => Assert.Equal(ReferentialAction.Cascade, f.OnDelete));
    }

    [Fact]
    public void Up_IndexesTheIssueSide()
    {
        var index = Assert.Single(Up.OfType<CreateIndexOperation>());

        Assert.Equal("DreamRemands", index.Table);
        Assert.Equal(["IssueId"], index.Columns);
    }

    [Fact]
    public void Down_DropsTheWholeTable()
    {
        var drop = Assert.Single(Down);

        var table = Assert.IsType<DropTableOperation>(drop);
        Assert.Equal("DreamRemands", table.Name);
    }

    private static IReadOnlyList<MigrationOperation> Up => new AddDreamRemandsProbe().Forward;
    private static IReadOnlyList<MigrationOperation> Down => new AddDreamRemandsProbe().Backward;

    private sealed class AddDreamRemandsProbe : AddDreamRemands
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
