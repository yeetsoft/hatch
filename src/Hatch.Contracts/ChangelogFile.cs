namespace Hatch.Contracts;

/// <summary>
/// What a repository's trunk can hold at <see cref="Path"/>, as the board
/// judges it against its own projection of the file - the names a stored row
/// and a client agree on.
/// </summary>
public static class ChangelogFile
{
    /// <summary>Where this file lives in a repository's checkout.</summary>
    public const string Path = ".hatch/changelog.yml";

    /// <summary>The file is the whole history, so its cap is wider than the dependency file's 512 KB.</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>The file is exactly the bytes the board would write.</summary>
    public const string CurrentState = "current";

    /// <summary>A file Hatch wrote, lacking or differing from some entries the board holds.</summary>
    public const string BehindState = "behind";

    /// <summary>The trunk has nothing at the path.</summary>
    public const string AbsentState = "absent";

    /// <summary>Bytes exist but are not a file Hatch wrote: another header, or YAML not of the shape.</summary>
    public const string ForeignState = "foreign";

    public static readonly string[] States = [CurrentState, BehindState, AbsentState, ForeignState];
}
