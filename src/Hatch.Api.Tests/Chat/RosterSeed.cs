using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hatch.Api.Tests.Chat;

/// <summary>A <see cref="ChatLinkRoster"/> over a test's own databases, with the wall and enrolment a test names.</summary>
internal static class RosterSeed
{
    public static AppDbContext NewAppDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public static ChatLinkRoster For(
        HatchContext db, AppDbContext appDb, bool wallOn = false, bool anyPerson = false, string? localName = null) =>
        new(db, appDb, Options.Create(new AuthOptions { Enabled = wallOn }), new StubAuthService { AnyPerson = anyPerson },
            new StubSiteSettings(localPersonName: localName), NullLogger<ChatLinkRoster>.Instance);
}
