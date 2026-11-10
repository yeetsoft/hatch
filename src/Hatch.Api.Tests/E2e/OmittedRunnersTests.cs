using Hatch.Api.Modules.Hatch;
using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1785: the Omitted runners section of the issue page, rendered and driven
/// against the real host and the built bundle. HA-1754 asks that a change under
/// <c>src/Hatch.Web</c> be seen on a desk and a phone profile; HA-1760 closed
/// without that pass.
///
/// <para>A parent carries one omission and its child inherits it; the child
/// carries one of its own and one the runner wrote against itself (a recusal,
/// <c>by</c> equal to the runner). Nothing is stubbed: Lift and Omit make the
/// real DELETE and PUT. The section is drawn only in the Details card's edit
/// mode, so the case asserts the one-line fact outside it, presses Edit, then
/// asserts the rows, drives Lift and Omit, and walks the refusals.</para>
///
/// <para>Every <c>.hatch-omission</c> descendant and every element of the add
/// form must lie inside the Details card, and the document must not scroll
/// sideways, on both profiles - see <see cref="PhoneOverflowReference"/> for
/// a layout bug this once stood in for and does not reproduce here. A
/// screenshot of the view, the edit mode with its rows and a refusal is left
/// in the case's artifact directory.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class OmittedRunnersTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 120;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    private const string AncestorRunner = "Ancestor Runner One";
    private const string OwnRunner = "Own Runner Two";
    private const string RecusedRunner = "Recused Runner Three With A Rather Long Name Of Over Forty Characters";
    private const string AddedRunner = "Added Runner Four";
    private const string ReasonlessRunner = "Reasonless Runner Five";
    private const string SlashRunner = "slash/runner six";

    private const string AncestorReason = "keeps timing out on the long builds";
    private const string OwnReason = "too flaky lately";
    private const string AddedReason = "added by the driver pass";

    /// <summary>
    /// HA-2172 is the section overflowing the Details card at the iPhone 14 Pro width - first seen
    /// on a Windows dev machine (scrollWidth 502 against clientWidth 393). It does not reproduce
    /// under this case's own reference environment, the Linux WebKit the CI e2e job runs: there the
    /// rows measure inside the card with room to spare, most likely a font-fallback difference
    /// between the two platforms rather than a layout bug a browser everywhere hits. The CI e2e gate
    /// also fails any case that skips, so a permanent <c>Skip.If(true, ...)</c> here was never going
    /// to go green. The phone case now runs whole and is held to the exact bar the desktop case is -
    /// see <see cref="AssertFits"/> - rather than skipping past a reproduction this environment does
    /// not have.
    /// </summary>
    private const string PhoneOverflowReference =
        "HA-2172: at the iPhone 14 Pro width the Omitted runners rows and Reason input overflow the Details card on some platforms.";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheSection_ShowsRowsLiftsAndAdds_AtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "OmittedRunners");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheSection_ShowsRowsLiftsAndAdds_AtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "OmittedRunnersPhone");
    }

    private async Task RunAsync(DeviceProfile? profile, string name)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        var where = profile is null ? "desktop" : profile.Device;
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);
            var (parent, child) = await deadline.BoundAsync("filing the issues", () => SeedAsync(baseUrl, harness.Services));

            var page = $"/apps/hatch/issues/{child}";
            await harness.OpenAsync(deadline, baseUrl, page);
            var p = harness.Page!;

            // 1. Outside edit mode the section is the one-line fact, and no row is drawn.
            var fact = p.Locator(".hatch-card", new() { Has = p.Locator("h2.hatch-section-title", new() { HasTextString = "Details" }) }).First;
            await deadline.BoundAsync("waiting for the Details card", () => fact.WaitForAsync());
            await deadline.BoundAsync("waiting for the omitted-runners fact", () =>
                fact.GetByText($"{AncestorRunner} ({AncestorReason}) · from {parent}").WaitForAsync());
            var factText = await deadline.BoundAsync("reading the Details card", () => fact.InnerTextAsync());
            Assert.Contains($"{OwnRunner} ({OwnReason})", factText);
            Assert.Contains(RecusedRunner, factText);
            Assert.Contains($"{AncestorRunner} ({AncestorReason}) · from {parent}", factText);
            Assert.Equal(0, await deadline.BoundAsync("counting rows", () => p.Locator(".hatch-omission").CountAsync()));
            await harness.ScreenshotAsync(deadline, "1-view-mode");

            // 2. Edit draws the three rows.
            await deadline.BoundAsync("pressing Edit", () => fact.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync());
            await deadline.BoundAsync("waiting for the rows", () => p.Locator(".hatch-omission").Nth(2).WaitForAsync());
            Assert.Equal(3, await deadline.BoundAsync("counting rows", () => p.Locator(".hatch-omission").CountAsync()));

            var own = Row(p, OwnRunner);
            var recused = Row(p, RecusedRunner);
            var inherited = Row(p, AncestorRunner);

            var ownText = await deadline.BoundAsync("reading the own row", () => own.InnerTextAsync());
            Assert.Contains(OwnReason, ownText);
            Assert.Contains("omitted by ", ownText);
            Assert.Contains(" on ", ownText);
            Assert.DoesNotContain("recused itself", ownText);
            Assert.DoesNotContain("Lifted on", ownText);

            var recusedText = await deadline.BoundAsync("reading the recused row", () => recused.InnerTextAsync());
            Assert.Contains("no reason given", recusedText);
            Assert.Contains("recused itself", recusedText);
            Assert.Contains(" on ", recusedText);
            Assert.DoesNotContain("omitted by ", recusedText);

            var inheritedText = await deadline.BoundAsync("reading the inherited row", () => inherited.InnerTextAsync());
            Assert.Contains(AncestorReason, inheritedText);
            Assert.Contains("omitted by ", inheritedText);
            Assert.Contains(" on ", inheritedText);
            Assert.Contains($"from {parent}", inheritedText);
            Assert.Contains($"Lifted on {parent}.", inheritedText);

            // The inherited row is lifted on its ancestor: no button at all, and a link to it.
            Assert.Equal(1, await deadline.BoundAsync("counting the own Lift", () => own.GetByRole(AriaRole.Button, new() { Name = "Lift", Exact = true }).CountAsync()));
            Assert.Equal(1, await deadline.BoundAsync("counting the recused Lift", () => recused.GetByRole(AriaRole.Button, new() { Name = "Lift", Exact = true }).CountAsync()));
            Assert.Equal(0, await deadline.BoundAsync("counting the inherited buttons", () => inherited.Locator("button").CountAsync()));
            Assert.Equal(1, await deadline.BoundAsync("counting the ancestor link", () => inherited.Locator($"a[href$='/issues/{parent}']").CountAsync()));
            Assert.Equal(2, await deadline.BoundAsync("counting every Lift", () => p.GetByRole(AriaRole.Button, new() { Name = "Lift", Exact = true }).CountAsync()));
            Assert.True(
                await deadline.BoundAsync("looking for the badge", () => p.Locator(".hatch-omission .hatch-badge", new() { HasTextString = "not a runner on this board" }).CountAsync()) >= 3,
                "no runner is registered on this board, so each of the three rows should carry the 'not a runner on this board' badge");

            var rowsDrawn = await harness.MeasureOmittedRunnersAsync(deadline);
            output.WriteLine($"--- rows drawn, {where}{Environment.NewLine}{rowsDrawn}");
            await harness.ScreenshotAsync(deadline, "2-edit-mode-rows");
            AssertFits(where, "with the rows drawn", rowsDrawn);

            // 3. Lift on the own row removes it - and the write reached the database.
            await deadline.BoundAsync("pressing Lift on the own row", () => own.GetByRole(AriaRole.Button, new() { Name = "Lift", Exact = true }).ClickAsync());
            await deadline.BoundAsync("waiting for the own row to leave", () => own.WaitForAsync(new() { State = WaitForSelectorState.Detached }));
            Assert.Equal(2, await deadline.BoundAsync("counting rows", () => p.Locator(".hatch-omission").CountAsync()));
            var afterLift = await deadline.BoundAsync("reading the child", () => ReadRunnersAsync(baseUrl, child));
            Assert.DoesNotContain(OwnRunner, afterLift);
            Assert.Contains(AncestorRunner, afterLift);

            // 4. A blank runner name leaves Omit disabled and draws no sentence.
            var runnerInput = p.GetByLabel("Runner to omit", new() { Exact = true });
            var reasonInput = p.GetByLabel("Reason", new() { Exact = true });
            var omit = p.Locator(".hatch-omission-add").GetByRole(AriaRole.Button, new() { Name = "Omit", Exact = true });
            Assert.True(await deadline.BoundAsync("checking Omit", () => omit.IsDisabledAsync()), "Omit is enabled with no runner name");
            await deadline.BoundAsync("typing a blank name", () => runnerInput.FillAsync("   "));
            await deadline.BoundAsync("typing a reason", () => reasonInput.FillAsync("a reason with no runner"));
            Assert.True(await deadline.BoundAsync("checking Omit", () => omit.IsDisabledAsync()), "Omit is enabled with a blank runner name");
            Assert.Equal(0, await deadline.BoundAsync("looking for a sentence", () => p.Locator("p.hatch-omission-refusal").CountAsync()));
            await deadline.BoundAsync("clearing the reason", () => reasonInput.FillAsync(""));

            // 5. Adding a runner by name and reason adds a row and clears both inputs.
            await deadline.BoundAsync("typing the runner", () => runnerInput.FillAsync(AddedRunner));
            await deadline.BoundAsync("typing the reason", () => reasonInput.FillAsync(AddedReason));
            Assert.Equal("24/200", await deadline.BoundAsync("reading the counter", () => p.Locator(".hatch-omission-add .text-muted").InnerTextAsync()));
            await deadline.BoundAsync("pressing Omit", () => omit.ClickAsync());
            var added = Row(p, AddedRunner);
            await deadline.BoundAsync("waiting for the added row", () => added.WaitForAsync());
            Assert.Contains(AddedReason, await deadline.BoundAsync("reading the added row", () => added.InnerTextAsync()));
            Assert.Equal(1, await deadline.BoundAsync("counting the added Lift", () => added.GetByRole(AriaRole.Button, new() { Name = "Lift", Exact = true }).CountAsync()));
            Assert.Equal("", await deadline.BoundAsync("reading the runner input", () => runnerInput.InputValueAsync()));
            Assert.Equal("", await deadline.BoundAsync("reading the reason input", () => reasonInput.InputValueAsync()));
            Assert.Equal("0/200", await deadline.BoundAsync("reading the counter", () => p.Locator(".hatch-omission-add .text-muted").InnerTextAsync()));

            // 6. A blank reason is accepted, and reads as none.
            await deadline.BoundAsync("typing the runner", () => runnerInput.FillAsync(ReasonlessRunner));
            await deadline.BoundAsync("pressing Omit", () => omit.ClickAsync());
            var reasonless = Row(p, ReasonlessRunner);
            await deadline.BoundAsync("waiting for the reasonless row", () => reasonless.WaitForAsync());
            var reasonlessText = await deadline.BoundAsync("reading the reasonless row", () => reasonless.InnerTextAsync());
            Assert.Contains("no reason given · omitted by ", reasonlessText);

            // 7. A name with a slash: what the route does is recorded, not asserted either way.
            await deadline.BoundAsync("typing the slash name", () => runnerInput.FillAsync(SlashRunner));
            await deadline.BoundAsync("pressing Omit", () => omit.ClickAsync());
            var slashRow = Row(p, SlashRunner);
            var anySentence = p.Locator("p.hatch-omission-refusal[role=alert]");
            await deadline.BoundAsync("waiting for the slash name's answer", () =>
                Task.WhenAny(slashRow.WaitForAsync(), anySentence.WaitForAsync()));
            var slashRows = await deadline.BoundAsync("counting the slash row", () => slashRow.CountAsync());
            var slashSentence = slashRows == 0 ? await deadline.BoundAsync("reading the sentence", () => anySentence.InnerTextAsync()) : null;
            output.WriteLine($"a runner name with a slash, through the page: {(slashRows == 1 ? "accepted, a row was drawn" : $"refused: \"{slashSentence}\"")}");
            output.WriteLine($"a runner name with a slash, straight to the route: {await deadline.BoundAsync("asking the route", () => PutAsync(baseUrl, child, "slash/direct runner", "from the route"))}");

            // 8. Refusals, each one sentence in a role=alert paragraph. Playwright's fill is held to maxlength
            //    by the input, so the 201-character reason is set through the DOM's own value setter.
            await deadline.BoundAsync("typing the runner", () => runnerInput.FillAsync("Refused Runner"));
            await harness.SetValuePastMaxLengthAsync(deadline, "input[aria-label='Reason']", new string('r', OmitRunnerRequest.MaxLength + 1));
            Assert.Equal(OmitRunnerRequest.MaxLength + 1, (await deadline.BoundAsync("reading the reason", () => reasonInput.InputValueAsync())).Length);
            await deadline.BoundAsync("pressing Omit", () => omit.ClickAsync());
            var reasonSentence = $"an omission reason is at most {OmitRunnerRequest.MaxLength} characters";
            await deadline.BoundAsync("waiting for the reason refusal", () =>
                p.Locator("p.hatch-omission-refusal[role=alert]", new() { HasTextString = reasonSentence }).WaitForAsync());
            Assert.Equal(reasonSentence, await deadline.BoundAsync("reading the refusal", () => anySentence.InnerTextAsync()));
            Assert.Equal(0, await deadline.BoundAsync("counting the refused row", () => Row(p, "Refused Runner").CountAsync()));
            output.WriteLine($"refusal, reason over {OmitRunnerRequest.MaxLength}: {reasonSentence}");

            var tooLongName = new string('n', EfHatchRunner.MaxNameLength + 1);
            await deadline.BoundAsync("typing a long runner name", () => runnerInput.FillAsync(tooLongName));
            await deadline.BoundAsync("clearing the reason", () => reasonInput.FillAsync(""));
            await deadline.BoundAsync("pressing Omit", () => omit.ClickAsync());
            var nameSentence = $"a runner name is at most {EfHatchRunner.MaxNameLength} characters";
            await deadline.BoundAsync("waiting for the name refusal", () =>
                p.Locator("p.hatch-omission-refusal[role=alert]", new() { HasTextString = nameSentence }).WaitForAsync());
            Assert.Equal(nameSentence, await deadline.BoundAsync("reading the refusal", () => anySentence.InnerTextAsync()));
            output.WriteLine($"refusal, name over {EfHatchRunner.MaxNameLength}: {nameSentence}");

            // The tallest state of the section: every row, both inputs holding text, a sentence under the form.
            await deadline.BoundAsync("typing the reason", () => reasonInput.FillAsync(new string('r', OmitRunnerRequest.MaxLength)));
            var refused = await harness.MeasureOmittedRunnersAsync(deadline);
            output.WriteLine($"--- after a refusal, {where}{Environment.NewLine}{refused}");
            await harness.ScreenshotAsync(deadline, "3-after-a-refusal");
            AssertFits(where, "after a refusal", refused);

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

    /// <summary>The row whose bold runner name is exactly <paramref name="runner"/>.</summary>
    private static ILocator Row(IPage page, string runner) =>
        page.Locator("li.hatch-omission", new() { Has = page.Locator("strong", new() { HasTextString = runner }) }).First;

    /// <summary>
    /// Every element of every row and of the add form lies inside the Details card
    /// (naming the element and both boxes), the document does not scroll sideways,
    /// and no button is squeezed past its own <c>scrollWidth</c>. Held to the same
    /// bar on both profiles: see <see cref="PhoneOverflowReference"/> for why the
    /// phone profile is not special-cased here any more.
    /// </summary>
    private void AssertFits(string where, string when, OmittedRunnersMeasurement m)
    {
        var violations = new List<string>();

        if (m.Elements.Count(e => e.Name.StartsWith("li.hatch-omission ", StringComparison.Ordinal)) < 3)
            violations.Add($"{where}, {when}: fewer than three rows were measured");
        if (!m.Elements.Any(e => e.Control && e.Name.StartsWith("input \"Reason\"", StringComparison.Ordinal)))
            violations.Add($"{where}, {when}: the Reason input was not measured");

        if (m.ScrollWidth != m.ClientWidth)
            violations.Add($"{where}, {when}: the document scrolls sideways (scrollWidth {m.ScrollWidth} != clientWidth {m.ClientWidth})");

        foreach (var e in m.Elements)
        {
            if (e.Box.X < m.Card.X - Tolerance || e.Box.Right > m.Card.Right + Tolerance)
                violations.Add($"{where}, {when}: {e.Name} ({e.Box}) lies outside the card ({m.Card})");

            // A text input holding more than it shows scrolls its own text by design; a button that does is clipped.
            if (e.Control && !e.Name.StartsWith("input ", StringComparison.Ordinal) && e.ScrollWidth > e.ClientWidth + 1)
                violations.Add($"{where}, {when}: {e.Name} ({e.Box}) is clipped inside itself (scrollWidth {e.ScrollWidth} > clientWidth {e.ClientWidth})");
        }

        Assert.True(
            violations.Count == 0,
            $"The Omitted runners section does not fit ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    /// <summary>
    /// A project, a story and a task under it. The story omits one runner, so the
    /// task inherits it; the task omits another (the local person is the actor)
    /// and holds a third whose <c>by</c> is the runner itself, the shape a recusal
    /// leaves. That last row is written through the context: a recusal is made by
    /// a runner's own key, which a scratch host with no keys does not have.
    /// </summary>
    private static async Task<(string Parent, string Child)> SeedAsync(string baseUrl, IServiceProvider services)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

        var project = await PostAsync<ProjectCreateRequest, ProjectDto>(http, "/api/hatch/projects", new("E2E", "E2E"));
        var parent = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/hatch/issues", new(project.Id, "story", "Parent", null, null, null, null));
        var child = await PostAsync<IssueCreateRequest, IssueDto>(
            http, "/api/hatch/issues", new(project.Id, "task", "Child", null, parent.Key, null, null));

        await PutOk(http, parent.Key, AncestorRunner, AncestorReason);
        await PutOk(http, child.Key, OwnRunner, OwnReason);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HatchContext>();
        var childRow = db.Issues.Single(i => i.Project!.Key == "E2E" && i.Title == "Child");
        db.RunnerOmissions.Add(new EfHatchIssueRunnerOmission
        {
            IssueId = childRow.Id,
            RunnerName = RecusedRunner,
            Reason = null,
            By = RecusedRunner,
            At = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return (parent.Key, child.Key);
    }

    private static async Task PutOk(HttpClient http, string key, string runner, string reason)
    {
        var answer = await PutAsync(http, key, runner, reason);
        Assert.True(answer.StartsWith("200", StringComparison.Ordinal), $"Omitting {runner} from {key} answered {answer}");
    }

    private static async Task<string> PutAsync(string baseUrl, string key, string runner, string reason)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        return await PutAsync(http, key, runner, reason);
    }

    private static async Task<string> PutAsync(HttpClient http, string key, string runner, string reason)
    {
        using var response = await http.PutAsJsonAsync($"/api/hatch/issues/{key}/omitted-runners/{Uri.EscapeDataString(runner)}", new OmitRunnerRequest(reason), Web);
        var answer = $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}".Trim();
        return answer.Length <= 200 ? answer : answer[..200];
    }

    /// <summary>The runner names the issue's own and inherited omissions name, as the API reads them.</summary>
    private static async Task<IReadOnlyList<string>> ReadRunnersAsync(string baseUrl, string key)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var issue = await http.GetFromJsonAsync<IssueDto>($"/api/hatch/issues/{key}", Web);
        return issue!.OmittedRunners.Select(o => o.Runner).ToList();
    }

    private static async Task<TResponse> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body)
    {
        using var response = await http.PostAsJsonAsync(path, body, Web);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path} answered {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<TResponse>(text, Web)!;
    }
}
