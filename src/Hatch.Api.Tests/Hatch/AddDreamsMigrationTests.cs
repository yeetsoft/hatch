using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="AddDreams"/>'s <c>Up()</c>/<c>Down()</c>, lifted out and run
/// against a bare <see cref="MigrationBuilder"/> rather than a database - the
/// same probe shape <c>AddSearchVectorsMigrationTests</c> uses.
/// </summary>
public class AddDreamsMigrationTests
{
    [Fact]
    public void Up_CreatesDreamsWithAStoredSearchVectorColumn()
    {
        var table = Up.OfType<CreateTableOperation>().Single(t => t.Name == "Dreams");
        var searchVector = table.Columns.Single(c => c.Name == "SearchVector");

        Assert.True(searchVector.IsStored);
        Assert.Contains("""setweight(to_tsvector('english', "Body"), 'A')""", searchVector.ComputedColumnSql);
    }

    [Fact]
    public void Up_IndexesTheSearchVectorColumnWithGin()
    {
        var index = Up.OfType<CreateIndexOperation>().Single(i => i.Table == "Dreams" && i.Columns is ["SearchVector"]);

        Assert.Equal("GIN", index.FindAnnotation("Npgsql:IndexMethod")?.Value);
    }

    [Fact]
    public void Down_DropsTheWholeTable()
    {
        var drop = Assert.Single(Down);

        var table = Assert.IsType<DropTableOperation>(drop);
        Assert.Equal("Dreams", table.Name);
    }

    private static IReadOnlyList<MigrationOperation> Up => new AddDreamsProbe().Forward;
    private static IReadOnlyList<MigrationOperation> Down => new AddDreamsProbe().Backward;

    private sealed class AddDreamsProbe : AddDreams
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
