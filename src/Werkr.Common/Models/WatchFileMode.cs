namespace Werkr.Common.Models;

/// <summary>
/// Controls how the WatchFile action behaves when its timeout expires
/// without detecting a matching file.
/// </summary>
public enum WatchFileMode {
    /// <summary>The action fails when the timeout expires without a match.</summary>
    FailOnTimeout = 0,

    /// <summary>The action succeeds with a "not found" output when the timeout expires.</summary>
    ExitQuietly = 1,
}
