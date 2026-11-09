using System;
using System.IO;

namespace NextXLR.Services;

/// <summary>
/// Provides Git commit metadata embedded at build time or resolved dynamically from the repository.
/// </summary>
public static class GitBuildInfo
{
    private static string _commitHash = "1f0b4fe";
    private static string _fullCommitHash = "1f0b4fe38b16f7bab25288c38b6f74b000283a21";

    public static string CommitHash => _commitHash;
    public static string FullCommitHash => _fullCommitHash;

    static GitBuildInfo()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            DirectoryInfo? repoDir = null;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    repoDir = dir;
                    break;
                }
                dir = dir.Parent;
            }

            if (repoDir != null)
            {
                var headFile = Path.Combine(repoDir.FullName, ".git", "HEAD");
                if (File.Exists(headFile))
                {
                    var headContent = File.ReadAllText(headFile).Trim();
                    if (headContent.StartsWith("ref: ", StringComparison.OrdinalIgnoreCase))
                    {
                        var refPath = headContent.Substring(5).Trim();
                        var refFile = Path.Combine(repoDir.FullName, ".git", refPath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(refFile))
                        {
                            var sha = File.ReadAllText(refFile).Trim();
                            if (sha.Length >= 7)
                            {
                                _fullCommitHash = sha;
                                _commitHash = sha.Substring(0, 7);
                            }
                        }
                    }
                    else if (headContent.Length >= 7)
                    {
                        _fullCommitHash = headContent;
                        _commitHash = headContent.Substring(0, 7);
                    }
                }
            }
        }
        catch
        {
        }
    }
}
