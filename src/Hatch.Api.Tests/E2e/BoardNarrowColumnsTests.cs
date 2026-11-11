using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// AR-66's acceptance, measured: a board column that shows no card is drawn at
/// half the width of one that does, and the width follows what is shown. Two
/// cards are filed and one moved to another column, so exactly two columns
/// show a card and every other is narrow; a search that matches only one card
/// empties the other's column (its card is hidden by the filter, not absent)
/// and clearing the search brings it back.
///
/// <para>Opened with reduced motion, so the width transition is off and the
/// drawn widths are final the moment the class changes - the transition itself
/// is pinned as text by <c>boardNarrowCss.test.ts</c>. A screenshot of each of
/// the three states is left in the case's artifact directory.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class BoardNarrowColumnsTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel rounding in the grid's track sizing.</summary>
    private const double Tolerance = 1;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task AColumnShowingNoCard_IsHalfAsWide_AndWidensWhenACardShows()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness("BoardNarrowColumns");
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var (columnCount, alphaColumn, betaColumn) = await deadline.BoundAsync("filing two issues", () => SeedAsync(baseUrl));

            Assert.True(columnCount >= 3, $"The board has {columnCount} columns; the case needs at least three to leave one narrow.");
            Assert.NotEqual(alphaColumn, betaColumn);

            await harness.OpenAsync(deadline, baseUrl, "/", reducedMotion: true);
            await harness.WaitForTopBarAsync(deadline);
            await harness.WaitForFirstColumnAsync(deadline);

            var everyColumn = Enumerable.Range(0, columnCount).ToList();
            var emptyAfterTwo = everyColumn.Where(i => i != alphaColumn && i != betaColumn).ToList();

            // 1. Two columns show a card: the rest are narrow.
            var before = await harness.MeasureColumnWidthsAsync(deadline, emptyAfterTwo);
            AssertHalf(before, emptyAfterTwo, "before the search");
            output.WriteLine($"before: {string.Join(", ", before.Select(w => w.ToString("0.#")))}");
            await harness.ScreenshotAsync(deadline, "1-before-search");

            // 2. A search that only Beta matches: Alpha's column holds a card the filter hides.
            await harness.SearchBoardAsync(deadline, "Beta");
            var filteredNarrow = everyColumn.Where(i => i != betaColumn).ToList();
            var filtered = await harness.MeasureColumnWidthsAsync(deadline, filteredNarrow);
            AssertHalf(filtered, filteredNarrow, "with the search on");
            Assert.True(await harness.ColumnSaysHiddenAsync(deadline, alphaColumn), "The emptied column does not say how many cards the filter hides.");
            output.WriteLine($"filtered: {string.Join(", ", filtered.Select(w => w.ToString("0.#")))}");
            await harness.ScreenshotAsync(deadline, "2-search-empties-a-column");

            // 3. Clearing the search puts it back.
            await harness.SearchBoardAsync(deadline, "");
            var cleared = await harness.MeasureColumnWidthsAsync(deadline, emptyAfterTwo);
            AssertHalf(cleared, emptyAfterTwo, "after the search is cleared");
            output.WriteLine($"cleared: {string.Join(", ", cleared.Select(w => w.ToString("0.#")))}");
            await harness.ScreenshotAsync(deadline, "3-search-cleared");

            harness.AssertNoPageErrors();
        }
        catch (Exception e) when (e is not Xunit.SkipException)
        {
            harness.Fail();
            throw;
        }
        finally
        {
            await harness.DisposeAsync();
        }
    }

    /// <summary>Every narrow column is half of every full one, and a full one is wider than a narrow one.</summary>
    private static void AssertHalf(IReadOnlyList<double> widths, IReadOnlyList<int> narrowIndices, string when)
    {
        var narrow = narrowIndices.Select(i => widths[i]).ToList();
        var full = widths.Where((_, i) => !narrowIndices.Contains(i)).ToList();
        var state = $"{when}: widths {string.Join(", ", widths.Select(w => w.ToString("0.#")))}, narrow at [{string.Join(", ", narrowIndices)}]";

        Assert.NotEmpty(narrow);
        Assert.NotEmpty(full);

        foreach (var n in narrow)
        foreach (var f in full)
            Assert.True(Math.Abs(n * 2 - f) <= Tolerance * 2, $"A narrow column is not half a full one - {state}");
    }

    /// <summary>
    /// A project and two tasks - "Alpha" stays where a new task lands and "Beta" moves to the first
    /// board column, or the second if that is where Alpha is - then the number of columns the board
    /// draws and the zero-based index of the column each card is in. Filed the way any caller files
    /// them with the wall off: the local person may file everything this test files.
    /// </summary>
    private static async Task<(int Columns, int Alpha, int Beta)> SeedAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("E2E", "E2E"));
        var alpha = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/hatch/issues", new(project.Id, "task", "Alpha", null, null, null, null));
        var beta = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/hatch/issues", new(project.Id, "task", "Beta", null, null, null, null));

        var board = await http.GetFromJsonAsync<BoardDto>("/api/hatch/board", Web);
        var columns = board!.Statuses.Where(s => !s.IsDeferred).OrderBy(s => s.SortOrder).ToList();
        Assert.True(columns.Count >= 2, "The board has fewer than two columns to move a card between.");

        var alphaStatus = board.Issues.Single(i => i.Key == alpha.Key).StatusId;
        var alphaColumn = columns.FindIndex(c => c.Id == alphaStatus);
        var betaColumn = alphaColumn == 0 ? 1 : 0;

        using var response = await http.PostAsJsonAsync($"/api/hatch/issues/{beta.Key}/move", new IssueMoveRequest(columns[betaColumn].Id, null, null), Web);
        Assert.True(response.IsSuccessStatusCode, $"Moving {beta.Key} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return (columns.Count, alphaColumn, betaColumn);
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}
