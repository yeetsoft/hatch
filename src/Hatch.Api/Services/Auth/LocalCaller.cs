using Hatch.Api.Common;

namespace Hatch.Api.Services.Auth;

/// <summary>
/// Who is calling when nothing authenticated them - the identity local mode
/// has instead of a wall.
///
/// <c>Auth:Enabled=false</c> already admits every request and wakes no gate
/// (docs/auth-architecture.md). What it did not have was a name: every event a
/// browser wrote against a local install read <c>operator</c>, and "Assign to
/// me" had nobody to assign to. This is the policy that gives that mode a
/// person.
/// </summary>
/// <remarks>
/// Kept out of <see cref="CallerIdentity"/> deliberately: every decision
/// here is a pure function of two strings, and a pure function is testable
/// without an <see cref="Microsoft.AspNetCore.Http.HttpContext"/>, a stub auth
/// service and a settings table. CallerIdentity is left holding the plumbing
/// and none of the judgement.
/// </remarks>
public static class LocalCaller
{
    /// <summary>
    /// The name a local install uses when nobody has told it one - the same
    /// word the compose file falls back to, so there is one answer rather than
    /// two that drift.
    /// </summary>
    public const string DefaultName = "friend";

    /// <summary>
    /// The local person's id, fixed rather than derived from their name.
    ///
    /// That is the whole of the design decision. An id computed from the name
    /// would change the day somebody corrected a typo in it, and every issue
    /// assigned to them would silently read as unassigned - the liveness rule
    /// in <see cref="IActorDirectory"/> answers "nobody" for an id it does not
    /// recognise, which is correct behaviour applied to a fact that quietly
    /// became false. The id is who you are; the name is only what is drawn.
    /// </summary>
    public static readonly Guid PersonId = new("10ca1000-0000-4000-8000-000000000001");

    /// <summary>
    /// What to call the person sitting at this machine: the site setting they
    /// typed, else the value the app was started with, else
    /// <see cref="DefaultName"/>.
    /// </summary>
    /// <remarks>
    /// A name that <see cref="PersonName.TryNormalize"/> refuses falls through
    /// to the next candidate rather than throwing. The caller is on the path of
    /// every request in local mode, and a misconfigured name that returned a
    /// 500 would take the whole install down over a typo in an environment
    /// variable.
    /// </remarks>
    public static string PersonNameOf(string? fromSetting, string? fromConfig)
    {
        if (PersonName.TryNormalize(fromSetting, out var setting, out _)) return setting;
        if (PersonName.TryNormalize(fromConfig, out var config, out _)) return config;

        return DefaultName;
    }

    /// <summary>
    /// Whether either candidate actually named the person, or whether
    /// <see cref="DefaultName"/> is what they are being called for want of
    /// anything better.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="PersonNameOf"/> rather than derived by comparing its
    /// answer to the default, because an operator who genuinely types
    /// <c>friend</c> has configured their name and should not be told
    /// otherwise forever.
    /// </remarks>
    public static bool IsNamed(string? fromSetting, string? fromConfig) =>
        PersonName.TryNormalize(fromSetting, out _, out _) ||
        PersonName.TryNormalize(fromConfig, out _, out _);

    /// <summary>
    /// Whether the local person may answer for a caller who presented nothing:
    /// only on an install that cannot sign anybody in.
    /// </summary>
    /// <remarks>
    /// The name is the install's, not the caller's. Once a sign-in authority is
    /// configured or a person is enrolled, an unidentified request is a
    /// stranger rather than whoever started the app, and naming it as the local
    /// person would attribute its writes to somebody who did not make them.
    /// </remarks>
    public static bool PersonSpeaks(bool signInConfigured, bool anyPersonEnrolled) =>
        !signInConfigured && !anyPersonEnrolled;
}
