using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if PVD_XR_MANAGEMENT
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;
#endif
#if PVD_OPENXR
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;
#endif

namespace PVDesigner.EditorTools
{
    /// <summary>
    /// One-click project configuration (also executed automatically by the CI build):
    /// scene, player settings, always included shaders and OpenXR for desktop and Meta Quest.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string CompanyName = "PV Designer";
        public const string ProductName = "PVDesigner";
        public const string ApplicationId = "com.pvdesigner.app";

        /// <summary>Shaders created at runtime with Shader.Find must be part of the build.</summary>
        public static readonly string[] RuntimeShaders =
        {
            "Standard", "Sprites/Default", "Skybox/Procedural", "UI/Default", "Legacy Shaders/Diffuse", "GUI/Text Shader"
        };

        /// <summary>OpenXR controller profiles enabled for every platform.</summary>
        private static readonly HashSet<string> InteractionProfiles = new HashSet<string>
        {
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
            "ValveIndexControllerProfile",
            "HTCViveControllerProfile",
            "MicrosoftMotionControllerProfile",
            "KHRSimpleControllerProfile",
            "PICO4ControllerProfile"
        };

        [MenuItem("PV Designer/Setup/Configure project (scene, player, XR)", priority = 0)]
        public static void ConfigureAll()
        {
            EnsureScene();
            ConfigurePlayer();
            EnsureAlwaysIncludedShaders();
            ConfigureXR();
            AssetDatabase.SaveAssets();
            Debug.Log("[PV Designer] Project configured.");
        }

        [MenuItem("PV Designer/Open main scene", priority = 20)]
        public static void OpenMainScene()
        {
            EnsureScene();
            EditorSceneManager.OpenScene(ScenePath);
        }

        public static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
                var previous = SceneManager.GetActiveScene().path;
                // The application builds everything from code (PVDesignerApp bootstraps itself), so the scene stays empty.
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
                if (!string.IsNullOrEmpty(previous) && File.Exists(previous)) EditorSceneManager.OpenScene(previous);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, ApplicationId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            // Meta Quest (Android, OpenXR).
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Meta Horizon Store requirement: Android 12L (API level 32) or newer.
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);

            // Accept both the legacy Input Manager and the Input System package (InputAdapter supports both).
            SetActiveInputHandling(2);
        }

        /// <summary>0 = Input Manager, 1 = Input System, 2 = Both.</summary>
        public static void SetActiveInputHandling(int value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null || prop.intValue == value) return;
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureAlwaysIncludedShaders()
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs == null || gs.Length == 0) return;
            var so = new SerializedObject(gs[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;
            bool changed = false;
            foreach (var name in RuntimeShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("PV Designer/Setup/Configure OpenXR only", priority = 1)]
        public static void ConfigureXR()
        {
#if PVD_XR_MANAGEMENT
            var perTarget = GetOrCreateXrSettings();
            ConfigureXrFor(perTarget, BuildTargetGroup.Standalone, initOnStart: false);
            ConfigureXrFor(perTarget, BuildTargetGroup.Android, initOnStart: true);
            EditorUtility.SetDirty(perTarget);
#else
            Debug.LogWarning("[PV Designer] com.unity.xr.management is not installed: VR is disabled.");
#endif
        }

#if PVD_XR_MANAGEMENT
        private static XRGeneralSettingsPerBuildTarget GetOrCreateXrSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings) && settings != null)
                return settings;
            const string folder = "Assets/XR";
            const string path = folder + "/XRGeneralSettingsPerBuildTarget.asset";
            settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (settings == null)
            {
                Directory.CreateDirectory(folder);
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, path);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            return settings;
        }

        private static void ConfigureXrFor(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group, bool initOnStart)
        {
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = initOnStart;
            var manager = perTarget.ManagerSettingsForBuildTarget(group);
            XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
#if PVD_OPENXR
            FeatureHelpers.RefreshFeatures(group);
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openXr == null) return;
            foreach (var feature in openXr.GetFeatures())
            {
                string type = feature.GetType().Name;
                bool enable = InteractionProfiles.Contains(type)
                              || (group == BuildTargetGroup.Android && (type == "MetaQuestFeature" || type == "OculusQuestFeature"));
                if (enable && !feature.enabled)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                }
            }
            EditorUtility.SetDirty(openXr);
#endif
        }
#endif
    }
}
