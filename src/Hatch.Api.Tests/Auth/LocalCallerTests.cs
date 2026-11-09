using Hatch.Api.Common;
using Hatch.Api.Services.Auth;

namespace Hatch.Api.Tests.Auth;

/// <summary>
/// The policy behind local mode's identity, tested where it is a pure function
/// of two strings rather than through a request. That separation is the reason
/// LocalCaller is its own class: what follows needs no HttpContext, no stub
/// auth service and no settings table.
/// </summary>
public class LocalCallerTests
{
    [Fact]
    public void TheSiteSetting_WinsOverWhatTheAppWasStartedWith()
    {
        Assert.Equal("Ada", LocalCaller.PersonNameOf("Ada", "Grace"));
    }

    [Fact]
    public void WithNoSetting_TheConfiguredNameIsUsed()
    {
        Assert.Equal("Grace", LocalCaller.PersonNameOf(null, "Grace"));
        Assert.Equal("Grace", LocalCaller.PersonNameOf("   ", "Grace"));
    }

    [Fact]
    public void WithNothingAnywhere_ThereIsStillAName()
    {
        Assert.Equal(LocalCaller.DefaultName, LocalCaller.PersonNameOf(null, null));
        Assert.Equal(LocalCaller.DefaultName, LocalCaller.PersonNameOf("", ""));
    }

    /// <summary>
    /// A misconfigured name must not 500 every request in the mode it exists
    /// for. It falls through to the next candidate, which is the same thing
    /// that would have happened had nobody typed anything.
    /// </summary>
    [Fact]
    public void ANameTheHouseRuleRefuses_FallsThroughRatherThanThrowing()
    {
        var tooLong = new string('a', PersonName.MaxGraphemes + 1);

        Assert.Equal("Grace", LocalCaller.PersonNameOf(tooLong, "Grace"));
        Assert.Equal(LocalCaller.DefaultName, LocalCaller.PersonNameOf(tooLong, tooLong));
    }

    /// <summary>The trojan-source strip comes along, because this name lands in an audit column.</summary>
    [Fact]
    public void APersonsName_IsNormalizedOnTheWayThrough()
    {
        Assert.Equal("Ada Lovelace", LocalCaller.PersonNameOf("  Ada‮   Lovelace ", null));
    }

    [Fact]
    public void Configured_IsWhetherEitherSourceSaidSo()
    {
        Assert.True(LocalCaller.IsNamed("Ada", null));
        Assert.True(LocalCaller.IsNamed(null, "Grace"));
        Assert.False(LocalCaller.IsNamed(null, null));
        Assert.False(LocalCaller.IsNamed("  ", ""));
    }

    /// <summary>
    /// Typing the default is still having said something. Inferring this by
    /// comparing the resolved name to DefaultName would nag an operator called
    /// "friend" for the life of the install.
    /// </summary>
    [Fact]
    public void SomebodyWhoTypedTheDefaultName_HasConfiguredIt()
    {
        Assert.True(LocalCaller.IsNamed(LocalCaller.DefaultName, null));
    }

    /// <summary>The local person speaks only for an install that cannot sign anybody in.</summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void PersonSpeaks_OnlyWhenNothingCanSignAnybodyIn(bool signInConfigured, bool anyPersonEnrolled, bool expected)
    {
        Assert.Equal(expected, LocalCaller.PersonSpeaks(signInConfigured, anyPersonEnrolled));
    }

    /// <summary>
    /// The one thing about the local person that is not allowed to move.
    /// Renaming yourself must not orphan every issue you were assigned, so the
    /// id is a literal rather than anything derived - and this is the test that
    /// notices if somebody later makes it a hash of the name.
    /// </summary>
    [Fact]
    public void TheLocalPersonsId_IsFixedAndNotDerivedFromTheirName()
    {
        Assert.Equal(new Guid("10ca1000-0000-4000-8000-000000000001"), LocalCaller.PersonId);
    }
}
