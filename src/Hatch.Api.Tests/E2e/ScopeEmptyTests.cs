using Hatch.Api.Tests.Hatch;
using Hatch.Contracts;
using Microsoft.Playwright;
using System.Text.Json;
using Xunit.Abstractions;

namespace Hatch.Api.Tests.E2e;

/// <summary>
/// HA-2386's acceptance, rendered: a person whose scope reaches no project
/// gets one sentence in place of the board, Plan, Projects and Dreams pages,
/// and nothing of the page behind it - no column, no project list. A person
/// with every project in scope still gets the board.
///
/// <para>The wall is off, so no caller is bounded; <c>/api/me</c> is fulfilled
/// with the real <see cref="MeDto"/> serialised, saying <c>none</c>. What this
/// case reads is the bundle's own answer to that; the server's side of it is
/// <c>MeScopeTests</c>.</para>
/// </summary>
[Collection(Jobs.QuartzSchedulerCollection.Name)]
public class ScopeEmptyTests(ITestOutputHelper output)
{
    private const int CaseBudgetSeconds = 90;

    /// <summary>A little past <see cref="CaseBudgetSeconds"/>, so it only fires if <see cref="Deadline"/> itself fails to bound something.</summary>
    private const int CaseTimeoutMs = (CaseBudgetSeconds + 10) * 1000;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string Sentence = "No project is in your scope; an admin sets it on Manage → Users.";

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task APersonWithNoProject_SeesOneSentenceAtTheDesktopProfile() => RunAsync("ScopeEmptyDesktop", "desktop", null);

    [SkippableFact(Timeout = CaseTimeoutMs)]
    public Task APersonWithNoProject_SeesOneSentenceAtThePhoneProfile()
    {
        Skip.If(FrozenMacOs14WebKit.Here, FrozenMacOs14WebKit.SkipReason);
        return RunAsync("ScopeEmptyPhone", "phone", DeviceProfile.IPhone14Pro);
    }

    private async Task RunAsync(string name, string profileName, DeviceProfile? profile)
    {
        Skip.IfNot(HatchDatabase.Available, $"{HatchDatabase.Variable} is unset");

        var harness = new E2eHarness(name);
        var deadline = new Deadline(TimeSpan.FromSeconds(CaseBudgetSeconds), harness.LogsSoFar);
        try
        {
            await harness.LaunchBrowserAsync(deadline, profile);

            Assert.True(
                File.Exists(E2eHarness.BundleIndex()),
                $"{E2eHarness.BundleIndex()} is missing: build the SPA first (`make test-e2e`, or `npm run build -w apps/hatch` in src/Hatch.Web).");

            await harness.CreateDatabasesAsync(deadline);
            var baseUrl = await harness.StartHostAsync(deadline);

            await harness.OpenAsync(deadline, baseUrl, "/", context => StubMe(context, "none"));

            foreach (var path in new[] { "/", "/projects", "/plan", "/dreams" })
            {
                if (path != "/") await harness.GoToAsync(deadline, path.TrimStart('/'));

                await harness.WaitForVisibleAsync(deadline, $".hatch-page:has-text(\"{Sentence}\")");
                var read = await harness.EvaluateAsync<JsonElement>(deadline, $"reading {path}", """
                    () => ({
                      page: document.querySelector('.hatch-page')?.textContent?.trim(),
                      pages: document.querySelectorAll('.hatch-page').length,
                      columns: document.querySelectorAll('.hatch-column').length,
                      scrollWidth: document.documentElement.scrollWidth,
                      innerWidth: window.innerWidth,
                    })
                    """);
                output.WriteLine($"{profileName} {path}: {read}");
                output.WriteLine($"saved {await harness.ScreenshotAsync(deadline, $"{(path == "/" ? "board" : path.TrimStart('/'))}-{profileName}")}");

                Assert.Equal(Sentence, read.GetProperty("page").GetString());
                Assert.Equal(1, read.GetProperty("pages").GetInt32());
                Assert.Equal(0, read.GetProperty("columns").GetInt32());
                Assert.True(
                    read.GetProperty("scrollWidth").GetDouble() <= read.GetProperty("innerWidth").GetDouble() + 0.5,
                    $"the page scrolls sideways: {read}");
            }

            // The same bundle, told the scope is all: the board as it always was.
            await harness.UnrouteAllAsync(deadline);
            await deadline.BoundAsync("answering all", () => StubMe(harness.Page!.Context, "all"));
            await harness.GoToAsync(deadline, "");
            await harness.WaitForFirstColumnAsync(deadline);
            Assert.False(await harness.IsVisibleAsync(deadline, $".hatch-page:has-text(\"{Sentence}\")"));

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

    /// <summary>Who is here, as the real record serialises it, with the scope the case names.</summary>
    private static Task StubMe(IBrowserContext context, string mode)
    {
        var me = new MeDto("person", "Ada", "user", true, true, false, Scope: new MeScopeDto(mode, []));

        return context.RouteAsync("**/api/me", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(me, Web),
        }));
    }
}
