using System;
using System.IO;

namespace NextXLR.Services;

/// <summary>
/// Provides Git commit metadata embedded at build time or resolved dynamically from the repository.
/// </summary>
public static class GitBuildInfo
{
    private static string _commitHash = "a4095a1";
    private static string _fullCommitHash = "a4095a1b3b8b6756ce1a510ed5c88f1dfe352691";

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
