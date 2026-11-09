using System;
using System.IO;
using System.Reflection;

namespace NextXLR.Services;

/// <summary>
/// Provides Git commit metadata embedded automatically at build time.
/// </summary>
public static class GitBuildInfo
{
    private static string _commitHash = "release";
    private static string _fullCommitHash = "release";

    public static string CommitHash => _commitHash;
    public static string FullCommitHash => _fullCommitHash;

    static GitBuildInfo()
    {
        try
        {
            var info = typeof(GitBuildInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrEmpty(info) && info.Contains('+'))
            {
                var sha = info.Split('+')[1].Trim();
                if (sha.Length >= 7)
                {
                    _fullCommitHash = sha;
                    _commitHash = sha.Substring(0, 7);
                    return;
                }
            }
        }
        catch { }

        // Dynamic repository fallback if running from IDE in dev repo
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var head = Path.Combine(dir.FullName, ".git", "HEAD");
                if (File.Exists(head))
                {
                    var c = File.ReadAllText(head).Trim();
                    if (c.StartsWith("ref: "))
                    {
                        var refFile = Path.Combine(dir.FullName, ".git", c[5..].Trim().Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(refFile))
                        {
                            var s = File.ReadAllText(refFile).Trim();
                            if (s.Length >= 7) { _fullCommitHash = s; _commitHash = s[..7]; return; }
                        }
                    }
                    else if (c.Length >= 7)
                    {
                        _fullCommitHash = c; _commitHash = c[..7]; return;
                    }
                }
                dir = dir.Parent;
            }
        }
        catch { }
    }
}
