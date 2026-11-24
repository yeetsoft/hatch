using System.Linq.Expressions;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The scan reads its candidates without the description and the search vector
/// (HA-2795), by listing every other column in <see cref="ScanCandidates.Columns"/>.
/// A column added to <see cref="EfHatchIssue"/> later would read as its default
/// in every fold if nobody added it to that list, and nothing would say so. This
/// is what says so.
/// </summary>
public class ScanCandidatesColumnsTests
{
    /// <summary>The only two the scan's read leaves unset: the text, and the vector built from it.</summary>
    private static readonly string[] LeftOut = [nameof(EfHatchIssue.Description), nameof(EfHatchIssue.SearchVector)];

    [Fact]
    public void EveryMappedPropertyOfAnIssue_IsSetByTheScansRead_ExceptTheDescriptionAndItsSearchVector()
    {
        using var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var mapped = db.Model.FindEntityType(typeof(EfHatchIssue))!
            .GetProperties()
            .Where(p => !p.IsShadowProperty())
            .Select(p => p.Name)
            .ToList();

        var body = Assert.IsType<MemberInitExpression>(ScanCandidates.Columns.Body);
        var bound = body.Bindings.Select(b => b.Member.Name).ToHashSet();

        var missing = mapped.Where(name => !bound.Contains(name) && !LeftOut.Contains(name)).ToList();
        Assert.True(missing.Count == 0,
            $"ScanCandidates.Columns leaves {string.Join(", ", missing)} unset, so a scan's fold would read the default. " +
            "Add each to the member initialiser, beside the others.");

        // The two left out stay the only two: a name here that is also bound has
        // been put back, and the scan reads the text again.
        Assert.All(LeftOut, name => Assert.DoesNotContain(name, bound));
        // The vector is mapped by the Npgsql provider only, so the in-memory model
        // this test reads does not hold it; the text is on both.
        Assert.Contains(nameof(EfHatchIssue.Description), mapped);
    }
}
