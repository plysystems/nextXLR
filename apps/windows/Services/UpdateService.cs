using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NextXLR.Services;

public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public bool IsNewVersion { get; init; }
    public bool IsDifferentBuild { get; init; }
    public string LatestTag { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public string? LatestName { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? DownloadUrl { get; init; }
    public string LocalCommit { get; init; } = "";
    public string RemoteCommit { get; init; } = "";
    public string StatusMessage { get; init; } = "";
}

public static class UpdateService
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        ConnectTimeout = TimeSpan.FromSeconds(10)
    })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    static UpdateService()
    {
        try
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NextXLR-App/" + AppSettings.AppVersion);
        }
        catch { }
    }

    /// <summary>
    /// Checks GitHub Releases API for updates comparing version tags and commit hashes.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        var currentVer = AppSettings.AppVersion.TrimStart('v', 'V');
        var localHash = AppSettings.BuildCommitHash;

        string? latestReleaseTag = null;
        string? latestReleaseName = null;
        string? latestReleaseBody = null;
        string? targetCommitish = null;
        string? exeDownloadUrl = null;

        try
        {
            var releasesUrl = "https://api.github.com/repos/plysystems/nextXLR/releases";
            var resp = await HttpClient.GetAsync(releasesUrl);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var newestRel = doc.RootElement[0];
                    latestReleaseTag = newestRel.TryGetProperty("tag_name", out var tElem) ? tElem.GetString() : null;
                    latestReleaseName = newestRel.TryGetProperty("name", out var nElem) ? nElem.GetString() : null;
                    latestReleaseBody = newestRel.TryGetProperty("body", out var bElem) ? bElem.GetString() : null;
                    targetCommitish = newestRel.TryGetProperty("target_commitish", out var tcElem) ? tcElem.GetString() : null;

                    if (newestRel.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assetsElem.EnumerateArray())
                        {
                            var aName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                            if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                exeDownloadUrl = asset.TryGetProperty("browser_download_url", out var bu) ? bu.GetString() : null;
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] Error fetching releases: {ex.Message}");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = currentVer,
                LocalCommit = localHash,
                StatusMessage = $"Fehler beim Abrufen der Updates: {ex.Message}"
            };
        }

        if (string.IsNullOrEmpty(latestReleaseTag))
        {
            latestReleaseTag = "v" + currentVer;
        }

        var cleanTag = latestReleaseTag.TrimStart('v', 'V');
        if (string.IsNullOrEmpty(exeDownloadUrl))
        {
            exeDownloadUrl = $"https://github.com/plysystems/nextXLR/releases/download/{latestReleaseTag}/NextXLR-Setup-{cleanTag}.exe";
        }

        bool isNewerVer = IsNewerVersion(latestReleaseTag, currentVer);

        // If targetCommitish is a 40-char git commit SHA, check if it's different from local commit.
        // Branch names like "main" are NOT commits and mean the release points to the main branch HEAD.
        bool isRemoteSha = !string.IsNullOrEmpty(targetCommitish) &&
                           targetCommitish.Length >= 7 &&
                           Regex.IsMatch(targetCommitish, "^[0-9a-fA-F]+$");

        bool isDifferentCommit = isRemoteSha &&
                                 !string.Equals(targetCommitish, localHash, StringComparison.OrdinalIgnoreCase) &&
                                 !targetCommitish!.StartsWith(localHash, StringComparison.OrdinalIgnoreCase) &&
                                 !localHash.StartsWith(targetCommitish, StringComparison.OrdinalIgnoreCase);

        if (isNewerVer)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                IsNewVersion = true,
                LatestTag = latestReleaseTag,
                CurrentVersion = currentVer,
                LatestName = latestReleaseName,
                ReleaseNotes = latestReleaseBody,
                DownloadUrl = exeDownloadUrl,
                LocalCommit = localHash,
                RemoteCommit = targetCommitish ?? "",
                StatusMessage = $"Neue Version {latestReleaseTag} verfügbar!"
            };
        }
        else if (isDifferentCommit)
        {
            var shortRemote = targetCommitish!.Length > 7 ? targetCommitish[..7] : targetCommitish;
            return new UpdateCheckResult
            {
                IsUpdateAvailable = true,
                IsDifferentBuild = true,
                LatestTag = latestReleaseTag,
                CurrentVersion = currentVer,
                LatestName = latestReleaseName,
                ReleaseNotes = latestReleaseBody,
                DownloadUrl = exeDownloadUrl,
                LocalCommit = localHash,
                RemoteCommit = shortRemote,
                StatusMessage = $"Neuerer Build für {latestReleaseTag} verfügbar ({shortRemote})."
            };
        }
        else
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                LatestTag = latestReleaseTag,
                CurrentVersion = currentVer,
                LatestName = latestReleaseName,
                ReleaseNotes = latestReleaseBody,
                DownloadUrl = exeDownloadUrl,
                LocalCommit = localHash,
                RemoteCommit = targetCommitish ?? "",
                StatusMessage = $"NEXT : XLR ist auf dem neuesten Stand (v{currentVer})."
            };
        }
    }

    /// <summary>
    /// Executes a background update check when the application starts, if enabled in settings.
    /// Shows a Windows tray notification if an update is found.
    /// </summary>
    public static void StartStartupUpdateCheck(TrayIconService? trayService, Action<UpdateCheckResult>? onUpdateFound = null)
    {
        var settings = AppSettings.Load();
        if (!settings.CheckForUpdatesOnStart) return;

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait 4 seconds after start to let daemon and UI load smoothly
                await Task.Delay(4000);

                var result = await CheckForUpdatesAsync();
                if (result.IsUpdateAvailable)
                {
                    Debug.WriteLine($"[UpdateService] Background check found update: {result.LatestTag}");

                    try
                    {
                        var title = result.IsNewVersion
                            ? $"NEXT : XLR Update verfügbar ({result.LatestTag})"
                            : $"NEXT : XLR Build aktualisiert ({result.LatestTag})";

                        var msg = result.IsNewVersion
                            ? $"Version {result.LatestTag} steht bereit. Öffne Einstellungen > Updates zum Installieren."
                            : $"Ein neuerer Build ({result.RemoteCommit}) für {result.LatestTag} steht bereit.";

                        trayService?.ShowNotification(title, msg);
                    }
                    catch { }

                    onUpdateFound?.Invoke(result);
                }
                else
                {
                    Debug.WriteLine("[UpdateService] Background check: Software is up to date.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Startup update check failed: {ex.Message}");
            }
        });
    }

    public static bool IsNewerVersion(string remoteTag, string currentVer)
    {
        var r = remoteTag.TrimStart('v', 'V').Trim();
        var c = currentVer.TrimStart('v', 'V').Trim();
        if (r.Equals(c, StringComparison.OrdinalIgnoreCase)) return false;

        var rParts = r.Split(new[] { '-', '+' }, 2);
        var cParts = c.Split(new[] { '-', '+' }, 2);

        if (Version.TryParse(rParts[0], out var rVer) && Version.TryParse(cParts[0], out var cVer))
        {
            if (rVer > cVer) return true;
            if (rVer < cVer) return false;

            // Base versions equal (e.g. 0.2.0 vs 0.2.0)
            bool rIsFinal = rParts.Length == 1;
            bool cIsFinal = cParts.Length == 1;
            if (rIsFinal && !cIsFinal) return true;  // Remote is final release, local is prerelease
            if (!rIsFinal && cIsFinal) return false; // Remote is prerelease, local is final release
            if (rIsFinal && cIsFinal) return false;  // Identical final versions

            // Both are prereleases (e.g. 0.2.0-rc vs 0.2.0-beta)
            int rRank = RankPrerelease(rParts[1]);
            int cRank = RankPrerelease(cParts[1]);
            if (rRank != cRank) return rRank > cRank;

            // Same prerelease tier: compare suffix alphanumerically (e.g. beta.2 vs beta.1)
            return string.Compare(rParts[1], cParts[1], StringComparison.OrdinalIgnoreCase) > 0;
        }

        return false;
    }

    private static int RankPrerelease(string pre)
    {
        var p = pre.ToLowerInvariant();
        if (p.StartsWith("alpha")) return 10;
        if (p.StartsWith("preview")) return 15;
        if (p.StartsWith("beta")) return 20;
        if (p.StartsWith("rc")) return 30;
        return 5;
    }
}
