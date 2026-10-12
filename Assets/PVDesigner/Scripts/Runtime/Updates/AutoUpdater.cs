using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PVDesigner.App;
using PVDesigner.Core.Updates;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace PVDesigner.Updates
{
    /// <summary>
    /// Where updates are published. Releases are created by .github/workflows/release.yml
    /// when a tag "vX.Y.Z" is pushed. The repository must be public (or a public mirror used
    /// for releases) so clients can query the GitHub API without credentials.
    /// </summary>
    public static class UpdateConfig
    {
        public const string Owner = "asfantrading-create";
        public const string Repository = "PVsyst-Drawing";

        public const string WindowsAsset = "PVDesigner-Windows-x64.zip";
        public const string QuestAsset = "PVDesigner-Quest.apk";
        public const string ChecksumSuffix = ".sha256";
        public const string UserAgent = "PVDesigner-Updater";
    }

    /// <summary>Checks GitHub Releases, downloads, verifies (SHA-256) and installs new versions.</summary>
    public class AutoUpdater : MonoBehaviour
    {
        public ReleaseInfo Available { get; private set; }
        public string StatusText { get; private set; } = "Not checked";
        public bool Busy { get; private set; }

        private PVDesignerApp _app;

        public void Initialize(PVDesignerApp app)
        {
            _app = app;
        }

        public static string PlatformAssetName
        {
            get
            {
#if UNITY_ANDROID
                return UpdateConfig.QuestAsset;
#else
                return UpdateConfig.WindowsAsset;
#endif
            }
        }

        public void CheckForUpdates(bool userInitiated)
        {
            if (Busy) return;
            StartCoroutine(CheckRoutine(userInitiated));
        }

        private static UnityWebRequest ApiRequest(string url)
        {
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", UpdateConfig.UserAgent);
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.timeout = 30;
            return req;
        }

        private IEnumerator CheckRoutine(bool userInitiated)
        {
            Busy = true;
            StatusText = "Checking ...";
            bool pre = AppSettings.IncludePreReleases;
            string url = pre
                ? GitHubReleases.ReleasesUrl(UpdateConfig.Owner, UpdateConfig.Repository)
                : GitHubReleases.LatestReleaseUrl(UpdateConfig.Owner, UpdateConfig.Repository);
            using (var req = ApiRequest(url))
            {
                yield return req.SendWebRequest();
                Busy = false;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    StatusText = req.responseCode == 404
                        ? "No published release found"
                        : "Check failed: " + req.error;
                    if (userInitiated) _app.Window.Dialog.Show("Updates", StatusText + "\n\nRepository: " + UpdateConfig.Owner + "/" + UpdateConfig.Repository);
                    yield break;
                }

                ReleaseInfo release;
                try
                {
                    release = pre ? GitHubReleases.PickNewest(req.downloadHandler.text, true) : GitHubReleases.ParseLatest(req.downloadHandler.text);
                }
                catch (Exception e)
                {
                    StatusText = "Invalid response: " + e.Message;
                    yield break;
                }

                if (release == null || !GitHubReleases.IsNewer(release, Application.version))
                {
                    Available = null;
                    StatusText = "Up to date (" + Application.version + ")";
                    _app.Window.SetUpdateAvailable(null);
                    if (userInitiated) _app.Window.Dialog.Show("Updates", "You are using the latest version (" + Application.version + ").");
                    yield break;
                }

                Available = release;
                StatusText = "Version " + release.Version + " available";
                _app.Window.SetUpdateAvailable(release.Version.ToString());
                if (userInitiated || AppSettings.SkippedVersion != release.TagName) ShowUpdateDialog();
            }
        }

        public void ShowUpdateDialog()
        {
            var r = Available;
            if (r == null)
            {
                CheckForUpdates(true);
                return;
            }
            string notes = string.IsNullOrWhiteSpace(r.Body) ? "(no release notes)" : r.Body;
            if (notes.Length > 1500) notes = notes.Substring(0, 1500) + " ...";
            string msg = $"A new version is available: {r.Version} (installed: {Application.version}).\n\n{notes}";
            var asset = r.FindAsset(PlatformAssetName);
            if (asset == null || !CanSelfInstall)
            {
                _app.Window.Dialog.Show("Update available", msg + "\n\nDownload it from the release page.",
                    ("Open download page", () => Application.OpenURL(r.HtmlUrl)),
                    ("Skip this version", () => AppSettings.SkippedVersion = r.TagName),
                    ("Later", null));
                return;
            }
            _app.Window.Dialog.Show("Update available", msg,
                ("Update now", () => StartCoroutine(InstallRoutine(r, asset))),
                ("Skip this version", () => AppSettings.SkippedVersion = r.TagName),
                ("Later", null));
        }

        /// <summary>Automatic installation is supported for Windows player builds.</summary>
        public static bool CanSelfInstall =>
            Application.platform == RuntimePlatform.WindowsPlayer;

        private IEnumerator InstallRoutine(ReleaseInfo release, ReleaseAsset asset)
        {
            if (Busy) yield break;
            if (_app.IsDirty) _app.SaveProject();
            Busy = true;
            string dir = Path.Combine(AppPaths.Updates, release.TagName);
            string zipPath = Path.Combine(dir, asset.Name);
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                Busy = false;
                Error("Cannot prepare the update folder: " + e.Message);
                yield break;
            }

            // Expected checksum (published next to the asset by the release workflow).
            string expected = null;
            var shaAsset = release.FindAsset(asset.Name + UpdateConfig.ChecksumSuffix);
            if (shaAsset != null)
            {
                using (var req = UnityWebRequest.Get(shaAsset.DownloadUrl))
                {
                    req.SetRequestHeader("User-Agent", UpdateConfig.UserAgent);
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) expected = GitHubReleases.ParseSha256File(req.downloadHandler.text);
                }
            }
            if (expected == null)
            {
                Busy = false;
                Error("The release has no valid checksum file (" + asset.Name + UpdateConfig.ChecksumSuffix + "). Update aborted for safety.");
                yield break;
            }

            using (var req = new UnityWebRequest(asset.DownloadUrl, UnityWebRequest.kHttpVerbGET))
            {
                req.SetRequestHeader("User-Agent", UpdateConfig.UserAgent);
                req.downloadHandler = new DownloadHandlerFile(zipPath) { removeFileOnAbort = true };
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    StatusText = $"Downloading {req.downloadProgress * 100:0} % ({asset.Size / 1048576.0:0} MB)";
                    _app.Status(StatusText);
                    yield return null;
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Busy = false;
                    Error("Download failed: " + req.error);
                    yield break;
                }
            }

            StatusText = "Verifying ...";
            string actual;
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(zipPath))
                    actual = GitHubReleases.ToHex(sha.ComputeHash(fs));
            }
            catch (Exception e)
            {
                Busy = false;
                Error("Cannot read the downloaded file: " + e.Message);
                yield break;
            }
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                Busy = false;
                Error("Checksum mismatch: the download is corrupted or was tampered with. Update aborted.");
                yield break;
            }

            string staging = Path.Combine(dir, "files");
            string marker = Path.Combine(dir, "installer-started");
            try
            {
                ZipFile.ExtractToDirectory(zipPath, staging);
                LaunchInstaller(staging, dir, marker);
            }
            catch (Exception e)
            {
                Busy = false;
                Error("Installation failed: " + e.Message);
                yield break;
            }

            // Only quit once the installer script is confirmed running (it can be blocked by
            // execution policy, antivirus or a cancelled elevation prompt).
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!File.Exists(marker) && Time.realtimeSinceStartup < deadline) yield return new WaitForSecondsRealtime(0.25f);
            if (!File.Exists(marker))
            {
                Busy = false;
                Error("The update installer did not start (blocked by security settings or the administrator prompt was cancelled). " +
                      "Install the update manually from the release page.");
                Application.OpenURL(release.HtmlUrl);
                yield break;
            }
            StatusText = "Restarting ...";
            _app.Status("Installing update " + release.Version + ", the application restarts ...");
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit();
        }

        private void Error(string message)
        {
            StatusText = message;
            _app.Status(message, true);
            _app.Window.Dialog.Show("Update", message);
        }

        /// <summary>
        /// Writes and starts a PowerShell script that waits for this process to exit, backs up the installation,
        /// copies the new files and restarts the application. On failure the backup is restored.
        /// Elevation is requested only when the installation folder is protected.
        /// </summary>
        private static void LaunchInstaller(string staging, string workDir, string marker)
        {
            string installDir = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(installDir)) throw new InvalidOperationException("Unknown installation folder.");
            string exe;
            try
            {
                exe = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch (Exception)
            {
                exe = null;
            }
            if (string.IsNullOrEmpty(exe)) exe = Path.Combine(installDir, Application.productName + ".exe");

            // The zip may contain the build at its root or inside a single folder.
            string source = staging;
            if (!File.Exists(Path.Combine(source, Path.GetFileName(exe))))
            {
                var dirs = Directory.GetDirectories(staging);
                if (dirs.Length == 1) source = dirs[0];
            }
            if (!File.Exists(Path.Combine(source, Path.GetFileName(exe))))
                throw new InvalidOperationException("The update package does not contain " + Path.GetFileName(exe));

            string backup = Path.Combine(workDir, "backup");
            string log = Path.Combine(workDir, "install.log");
            int pid = Process.GetCurrentProcess().Id;
            // PowerShell with a UTF-8 BOM handles non-ASCII paths (e.g. Arabic user names) correctly.
            string script = Path.Combine(workDir, "install-update.ps1");
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Continue'");
            sb.AppendLine($"Set-Content -LiteralPath {Ps(marker)} -Value 'started'");
            sb.AppendLine($"$procId = {pid}");
            sb.AppendLine("while (Get-Process -Id $procId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }");
            sb.AppendLine("Start-Sleep -Milliseconds 500");
            sb.AppendLine($"robocopy {Ps(installDir)} {Ps(backup)} /MIR /R:3 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null");
            sb.AppendLine("if ($LASTEXITCODE -ge 8) { $ok = $false } else {");
            sb.AppendLine($"  robocopy {Ps(source)} {Ps(installDir)} /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP /LOG:{Ps(log)} | Out-Null");
            sb.AppendLine("  $ok = $LASTEXITCODE -lt 8");
            sb.AppendLine("  if (-not $ok) {");
            sb.AppendLine("    # Roll back to the previous version so the application keeps working.");
            sb.AppendLine($"    robocopy {Ps(backup)} {Ps(installDir)} /MIR /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("if (-not $ok) {");
            sb.AppendLine("  Add-Type -AssemblyName PresentationFramework");
            sb.AppendLine("  [System.Windows.MessageBox]::Show('The update could not be installed; the previous version was kept. Close PV Designer and try again.', 'PV Designer update') | Out-Null");
            sb.AppendLine("}");
            sb.AppendLine("# Restart through Explorer so the application never runs elevated or as another account.");
            sb.AppendLine($"Start-Process -FilePath 'explorer.exe' -ArgumentList ('\"' + {Ps(exe)} + '\"')");
            sb.AppendLine($"if ($ok) {{ Remove-Item -Recurse -Force -LiteralPath {Ps(staging)}, {Ps(backup)} -ErrorAction SilentlyContinue }}");
            File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true));

            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workDir
            };
            if (!CanWrite(installDir)) psi.Verb = "runas";
            Process.Start(psi);
        }

        /// <summary>PowerShell single quoted string literal.</summary>
        private static string Ps(string s) => "'" + s.Replace("'", "''") + "'";

        private static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
