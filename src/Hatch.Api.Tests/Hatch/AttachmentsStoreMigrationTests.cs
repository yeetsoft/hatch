using Hatch.Api.Modules.Hatch.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="AttachmentsStore"/>'s <c>Up()</c>/<c>Down()</c>, lifted out and run
/// against a bare <see cref="MigrationBuilder"/> rather than a database - the
/// same probe shape <see cref="AddSearchVectorsMigrationTests"/> uses.
/// </summary>
public class AttachmentsStoreMigrationTests
{
    [Fact]
    public void Up_RenamesTheTableAndGrowsAndFillsItsNewColumns()
    {
        Assert.Contains(
            Up.OfType<RenameTableOperation>(),
            r => r.Name == "Images" && r.NewName == "Attachments");

        var alter = Up.OfType<AlterColumnOperation>().Single(a => a.Name == "ContentType");
        Assert.Equal(255, alter.MaxLength);

        var added = Up.OfType<AddColumnOperation>().Select(a => a.Name).ToList();
        Assert.Equal(["FileName", "Size", "CreatedById"], added);

        var fill = Up.OfType<SqlOperation>().Single(s => s.Sql.Contains("image.") && s.Sql.Contains("octet_length"));
        Assert.Contains("'image/webp' THEN 'webp'", fill.Sql);
    }

    [Fact]
    public void Down_DeletesNonImageRowsBeforeShrinkingTheColumn()
    {
        var operations = Down.ToList();
        var deleteIndex = operations.FindIndex(o => o is SqlOperation s && s.Sql.Contains("DELETE"));
        var shrinkIndex = operations.FindIndex(o => o is AlterColumnOperation { Name: "ContentType" });

        Assert.True(deleteIndex >= 0);
        Assert.True(shrinkIndex >= 0);
        Assert.True(deleteIndex < shrinkIndex);

        var shrink = (AlterColumnOperation)operations[shrinkIndex];
        Assert.Equal(64, shrink.MaxLength);
    }

    private static IReadOnlyList<MigrationOperation> Up => new AttachmentsStoreProbe().Forward;
    private static IReadOnlyList<MigrationOperation> Down => new AttachmentsStoreProbe().Backward;

    private sealed class AttachmentsStoreProbe : AttachmentsStore
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
