using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace PVDesigner.Core.Updates
{
    /// <summary>Minimal semantic version (major.minor.patch[-prerelease]) with ordering.</summary>
    public readonly struct SemVer : IComparable<SemVer>
    {
        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;
        public readonly string PreRelease;

        public SemVer(int major, int minor, int patch, string preRelease = "")
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? "";
        }

        public bool IsPreRelease => PreRelease.Length > 0;

        public static bool TryParse(string s, out SemVer v)
        {
            v = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            string pre = "";
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1);
                s = s.Substring(0, dash);
            }
            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return false;
            var nums = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (i >= parts.Length) break;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out nums[i])) return false;
            }
            v = new SemVer(nums[0], nums[1], nums[2], pre);
            return true;
        }

        public int CompareTo(SemVer other)
        {
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            // A release ranks above its pre-releases.
            if (IsPreRelease && !other.IsPreRelease) return -1;
            if (!IsPreRelease && other.IsPreRelease) return 1;
            return string.CompareOrdinal(PreRelease, other.PreRelease);
        }

        public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
        public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;

        public override string ToString() => IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
    }

    public class ReleaseAsset
    {
        public string Name;
        public string DownloadUrl;
        public long Size;
    }

    public class ReleaseInfo
    {
        public string TagName;
        public string Name;
        public string Body;
        public bool PreRelease;
        public bool Draft;
        public string HtmlUrl;
        public DateTime PublishedAt;
        public readonly List<ReleaseAsset> Assets = new List<ReleaseAsset>();

        public SemVer Version => SemVer.TryParse(TagName, out var v) ? v : new SemVer(0, 0, 0);

        public ReleaseAsset FindAsset(string name)
        {
            foreach (var a in Assets)
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }
    }

    /// <summary>Parsing helpers for the GitHub Releases REST API.</summary>
    public static class GitHubReleases
    {
        public static string LatestReleaseUrl(string owner, string repo) =>
            $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        public static string ReleasesUrl(string owner, string repo) =>
            $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=10";

        public static ReleaseInfo ParseRelease(JToken j)
        {
            var r = new ReleaseInfo
            {
                TagName = j.Value<string>("tag_name") ?? "",
                Name = j.Value<string>("name") ?? "",
                Body = j.Value<string>("body") ?? "",
                PreRelease = j.Value<bool?>("prerelease") ?? false,
                Draft = j.Value<bool?>("draft") ?? false,
                HtmlUrl = j.Value<string>("html_url") ?? "",
                PublishedAt = j.Value<DateTime?>("published_at") ?? DateTime.MinValue
            };
            if (j["assets"] is JArray assets)
            {
                foreach (var a in assets)
                {
                    r.Assets.Add(new ReleaseAsset
                    {
                        Name = a.Value<string>("name") ?? "",
                        DownloadUrl = a.Value<string>("browser_download_url") ?? "",
                        Size = a.Value<long?>("size") ?? 0
                    });
                }
            }
            return r;
        }

        public static ReleaseInfo ParseLatest(string json) => ParseRelease(JToken.Parse(json));

        /// <summary>Picks the newest non-draft release, optionally including pre-releases.</summary>
        public static ReleaseInfo PickNewest(string releasesArrayJson, bool includePreReleases)
        {
            ReleaseInfo best = null;
            foreach (var j in JArray.Parse(releasesArrayJson))
            {
                var r = ParseRelease(j);
                if (r.Draft || (r.PreRelease && !includePreReleases)) continue;
                if (best == null || r.Version > best.Version) best = r;
            }
            return best;
        }

        public static bool IsNewer(ReleaseInfo release, string currentVersion)
        {
            if (release == null) return false;
            if (!SemVer.TryParse(currentVersion, out var cur)) return true;
            return release.Version > cur;
        }

        /// <summary>Parses "&lt;sha256&gt;  &lt;file name&gt;" (sha256sum format) or a bare hash.</summary>
        public static string ParseSha256File(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var token = text.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (token.Length != 64) return null;
            foreach (char c in token)
                if (!Uri.IsHexDigit(c)) return null;
            return token.ToLowerInvariant();
        }

        public static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
