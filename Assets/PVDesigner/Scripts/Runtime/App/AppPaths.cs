using System;
using System.IO;
using UnityEngine;

namespace PVDesigner.App
{
    /// <summary>User data folders (Documents/PV Designer on desktop, persistent data path on mobile / Quest).</summary>
    public static class AppPaths
    {
        public static string Root
        {
            get
            {
#if UNITY_ANDROID || UNITY_IOS
                return Application.persistentDataPath;
#else
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = Application.persistentDataPath;
                return Path.Combine(docs, "PV Designer");
#endif
            }
        }

        public static string Projects => Path.Combine(Root, "Projects");
        public static string Reports => Path.Combine(Root, "Reports");
        /// <summary>Drop PVsyst .PAN / .OND files here to import them.</summary>
        public static string Components => Path.Combine(Root, "Components");
        public static string Updates => Path.Combine(Application.temporaryCachePath, "Updates");

        public static void EnsureCreated()
        {
            foreach (var dir in new[] { Root, Projects, Reports, Components })
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Cannot create folder " + dir + ": " + e.Message);
                }
            }
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "Project";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        public static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Application.OpenURL(new Uri(path).AbsoluteUri);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Cannot open folder: " + e.Message);
            }
        }
    }

    /// <summary>Per user preferences stored in PlayerPrefs.</summary>
    public static class AppSettings
    {
        public static bool CheckUpdatesOnStartup
        {
            get => PlayerPrefs.GetInt("pvd.updates.check", 1) == 1;
            set => PlayerPrefs.SetInt("pvd.updates.check", value ? 1 : 0);
        }

        public static bool IncludePreReleases
        {
            get => PlayerPrefs.GetInt("pvd.updates.pre", 0) == 1;
            set => PlayerPrefs.SetInt("pvd.updates.pre", value ? 1 : 0);
        }

        public static string SkippedVersion
        {
            get => PlayerPrefs.GetString("pvd.updates.skip", "");
            set => PlayerPrefs.SetString("pvd.updates.skip", value ?? "");
        }

        public static string LastProjectPath
        {
            get => PlayerPrefs.GetString("pvd.lastProject", "");
            set => PlayerPrefs.SetString("pvd.lastProject", value ?? "");
        }

        public static int GroundStyle
        {
            get => PlayerPrefs.GetInt("pvd.view.ground", 0);
            set => PlayerPrefs.SetInt("pvd.view.ground", value);
        }

        public static bool SoftShadows
        {
            get => PlayerPrefs.GetInt("pvd.view.softShadows", 1) == 1;
            set => PlayerPrefs.SetInt("pvd.view.softShadows", value ? 1 : 0);
        }

        public static float UiScale
        {
            get => PlayerPrefs.GetFloat("pvd.view.uiScale", 1f);
            set => PlayerPrefs.SetFloat("pvd.view.uiScale", value);
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
