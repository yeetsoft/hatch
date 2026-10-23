using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-1445: a control on a Runners row that is clipped fails a browser case.
/// The bug HA-1444 fixed was a layout one - every button was in the document
/// the whole time, squeezed out of the card by a flex row that let them shrink
/// - so only a measurement of a real layout can see it.
///
/// <para>Two rows are drawn from a stubbed <c>/api/hatch/runners</c>: one with
/// every long field set and no order, so the ticket input, Work this ticket and
/// Run one pass are drawn, and one holding an order, so Withdraw is. Everything
/// else, and the bundle, is real.</para>
///
/// <para>At desktop every control must lie inside the card. At the phone width
/// <c>Table.css</c> makes the table's wrapper scroll sideways on purpose, so a
/// control past the card's edge is reachable and is no bug there; the phone is
/// held to the table's box instead. Both are held to the control's own cell,
/// to its own <c>scrollWidth</c>, and to a document that does not scroll
/// sideways.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class RunnersFitTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    /// <summary>The smallest target a coarse pointer should be asked to hit, in CSS pixels.</summary>
    private const double MinimumTarget = 44;

    /// <summary>Sub-pixel layout is fractional; two boxes that touch are not overlapping.</summary>
    private const double Tolerance = 0.5;

    /// <summary>Row A draws six controls and row B four; fewer than this means the selector found something else.</summary>
    private const int MinimumControls = 8;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnersRow_FitsTheCardAtDesktop()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        await RunAsync(null, "RunnersFit");
    }

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public async Task TheRunnersRow_FitsAtThePhoneProfile()
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);

        await RunAsync(DeviceProfile.IPhone14Pro, "RunnersFitPhone");
    }

    private async Task RunAsync(DeviceProfile? profile, string name)
    {
        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            // The runners read is stubbed, but the host still serves the bundle.
            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/apps/hatch/runners", StubRunners);
            await harness.WaitForVisibleAsync(deadline, ".hatch-reorder--wrap");

            var measurement = await harness.MeasureRunnersAsync(deadline);
            var where = profile is null ? "runners, desktop" : $"runners, {profile.Device}";
            output.WriteLine($"--- {where}{Environment.NewLine}{measurement}");

            var violations = Check(where, profile is not null, measurement);

            Assert.True(
                violations.Count == 0,
                $"A Runners row does not fit ({violations.Count} violation(s)):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

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

    /// <summary>Every way the measurement shows a control clipped. Names the route, the control, its box and the box it escaped.</summary>
    private static List<string> Check(string where, bool phone, RunnersMeasurement m)
    {
        var found = new List<string>();

        if (m.Controls.Count < MinimumControls)
            found.Add($"{where}: only {m.Controls.Count} control(s) measured, expected at least {MinimumControls}");

        if (m.ScrollWidth != m.ClientWidth)
            found.Add($"{where}: the document scrolls sideways (scrollWidth {m.ScrollWidth} != clientWidth {m.ClientWidth})");

        if (phone && !m.CoarsePointerMatches)
            found.Add($"{where}: (pointer: coarse) does not match under the device descriptor, so the 44px rule cannot be asserted in the browser");

        foreach (var control in m.Controls)
        {
            var box = control.Box;

            if (phone)
            {
                // The table's wrapper scrolls on a phone by design; what it must not do is let a control out of the table.
                if (box.X < m.Table.X - Tolerance || box.Right > m.Table.Right + Tolerance)
                    found.Add($"{where}: {control.Name} ({box}) lies outside the table ({m.Table})");
            }
            else if (box.X < m.Card.X - Tolerance || box.Right > m.Card.Right + Tolerance)
            {
                found.Add($"{where}: {control.Name} ({box}) lies outside the card ({m.Card})");
            }

            if (box.X < control.Cell.X - Tolerance || box.Right > control.Cell.Right + Tolerance)
                found.Add($"{where}: {control.Name} ({box}) lies outside its cell ({control.Cell})");

            if (control.ScrollWidth > control.ClientWidth + 1)
                found.Add($"{where}: {control.Name} ({box}) is clipped inside itself (scrollWidth {control.ScrollWidth} > clientWidth {control.ClientWidth})");

            if (!phone)
                continue;

            // Every control, button or input, is held to 44px both ways: .hatch-btn carries it, and the ticket input is raised to it under a coarse pointer.
            var reaches = box.Height >= MinimumTarget - Tolerance && box.Width >= MinimumTarget - Tolerance;
            if (!reaches)
                found.Add($"{where}: {control.Name} ({box}) is under {MinimumTarget}px");
        }

        return found;
    }

    /// <summary>
    /// Two runners that both draw controls. The first sets every long field and
    /// holds no order, so the ticket input, Work this ticket and Run one pass are
    /// drawn; the second holds one, so the order sentence and Withdraw are.
    /// Nothing names a real host, path, repository or person.
    /// </summary>
    private static async Task StubRunners(IBrowserContext context)
    {
        var now = DateTimeOffset.UtcNow;

        var wide = new RunnerDto(
            Name: "example-host.invalid:/srv/checkouts/an-invented-organisation/an-invented-repository-with-a-long-name",
            Kind: "loop",
            FirstSeenAt: now.AddHours(-3),
            LastSeenAt: now,
            ClaimKey: "E2E-1",
            Line: string.Join(" && ", Enumerable.Repeat("make test-e2e --filter=SomeVeryLongInventedTestClassName.SomeVeryLongInventedTestMethodName_WithAnExtremelyLongSuffixThatCannotBreak", 3)),
            LineAt: now,
            State: RunnerStates.Running,
            Under: "an-invented-playbook-with-a-long-name",
            MaxRuns: 12,
            MaxSpend: 123.45m,
            UntilAt: now.AddHours(8),
            GoneAfterSeconds: 300,
            Repositories: ["example-org/an-invented-repository", "example-org/another-invented-repository", "example-org/a-third-invented-repository"],
            Clones: true,
            Mine: false,
            Standing: true,
            Where: "example-host.invalid:/srv/checkouts/an-invented-organisation/an-invented-repository-with-a-long-name/nested/deeper/still",
            Agent: "an-invented-agent",
            Drives: ["an-invented-drive", "another-invented-drive"],
            Tags: ["an-invented-tag", "another-invented-tag", "a-third-invented-tag"]);

        var ordered = new RunnerDto(
            Name: "example-host.invalid:/e2e",
            Kind: "loop",
            FirstSeenAt: now.AddHours(-1),
            LastSeenAt: now,
            ClaimKey: null,
            Line: null,
            LineAt: null,
            State: RunnerStates.Running,
            Under: null,
            MaxRuns: null,
            MaxSpend: null,
            UntilAt: null,
            GoneAfterSeconds: 300,
            Repositories: [],
            Clones: null,
            Mine: null,
            Standing: null,
            Where: null,
            Agent: null,
            Drives: [],
            Order: new RunnerOrderDto(RunnerOrderKinds.Work, "E2E-2", "someone", now));

        await FulfilAsync(context, "**/api/hatch/runners", new[] { wide, ordered });
    }

    private static Task FulfilAsync<T>(IBrowserContext context, string pattern, T body) =>
        context.RouteAsync(pattern, route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, Web),
        }));
}
