// COMPILE-CHECK STUB (never shipped): scenes, build settings, player settings and the
// build pipeline as LifeAsDuburi/Editor uses them. Signatures from UnityCsReference
// 6000.3.27f2; '// file:line' = where the member is declared.
using System;
using UnityEngine.SceneManagement;

namespace UnityEditor.SceneManagement
{
    public enum OpenSceneMode { Single, Additive, AdditiveWithoutLoading }   // Editor/Mono/EditorSceneManager.cs:14
    public enum NewSceneMode { Single, Additive }                            // Editor/Mono/EditorSceneManager.cs:15
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }             // Editor/Mono/EditorSceneManager.cs:16

    // Editor/Mono/EditorSceneManager.bindings.cs:31
    public sealed partial class EditorSceneManager : SceneManager
    {
        public static Scene OpenScene(string scenePath, OpenSceneMode mode) => default;          // Editor/Mono/EditorSceneManager.bindings.cs:79
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;         // Editor/Mono/EditorSceneManager.bindings.cs:94
        public static bool MarkSceneDirty(Scene scene) => false;                                 // Editor/Mono/EditorSceneManager.bindings.cs:228
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => false;                    // Editor/Mono/EditorSceneManager.cs:60
        public static Scene OpenScene(string scenePath) => default;                              // Editor/Mono/EditorSceneManager.cs:63
        public static Scene NewScene(NewSceneSetup setup) => default;                            // Editor/Mono/EditorSceneManager.cs:71
        public static bool SaveScene(Scene scene, string dstScenePath) => false;                 // Editor/Mono/EditorSceneManager.cs:79
        public static bool SaveScene(Scene scene) => false;                                      // Editor/Mono/EditorSceneManager.cs:86
    }
}

namespace UnityEditor.Build.Reporting
{
    // Modules/BuildReportingEditor/Managed/BuildResult.cs:10
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

    // Modules/BuildReportingEditor/Managed/BuildSummary.cs:12
    public partial struct BuildSummary
    {
        public string outputPath => null;          // Modules/BuildReportingEditor/Managed/BuildSummary.cs:33
        public ulong totalSize => 0;                // Modules/BuildReportingEditor/Managed/BuildSummary.cs:36
        public TimeSpan totalTime => default;       // Modules/BuildReportingEditor/Managed/BuildSummary.cs:40
        public int totalErrors => 0;                // Modules/BuildReportingEditor/Managed/BuildSummary.cs:45
        public int totalWarnings => 0;              // Modules/BuildReportingEditor/Managed/BuildSummary.cs:47
        public BuildResult result => default;       // Modules/BuildReportingEditor/Managed/BuildSummary.cs:52
    }

    // Modules/BuildReportingEditor/Managed/BuildReport.bindings.cs:122
    public sealed partial class BuildReport : UnityEngine.Object
    {
        public BuildSummary summary => default;     // Modules/BuildReportingEditor/Managed/BuildReport.bindings.cs:141
    }
}

namespace UnityEditor
{
    using UnityEditor.Build.Reporting;

    // Editor/Mono/BuildTarget.cs:13 (only the members used here)
    public enum BuildTarget { StandaloneWindows = 5, WebGL = 20, StandaloneWindows64 = 19, NoTarget = -2 }

    // Editor/Mono/BuildTargetGroup.cs:13 (only the members used here)
    public enum BuildTargetGroup { Unknown = 0, Standalone = 1, WebGL = 13 }

    // Editor/Mono/BuildPipeline.bindings.cs:22 (only the members used here)
    [Flags] public enum BuildOptions { None = 0, Development = 1 << 0 }

    // Editor/Mono/BuildPipeline.bindings.cs:218
    public partial struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }                  // Editor/Mono/BuildPipeline.bindings.cs:220
        public string locationPathName { get; set; }          // Editor/Mono/BuildPipeline.bindings.cs:221
        public BuildTargetGroup targetGroup { get; set; }     // Editor/Mono/BuildPipeline.bindings.cs:223
        public BuildTarget target { get; set; }               // Editor/Mono/BuildPipeline.bindings.cs:224
        public BuildOptions options { get; set; }             // Editor/Mono/BuildPipeline.bindings.cs:226
    }

    // Editor/Mono/BuildPipeline.bindings.cs:271
    public partial class BuildPipeline
    {
        public static BuildReport BuildPlayer(BuildPlayerOptions buildPlayerOptions) => null;                     // Editor/Mono/BuildPipeline.bindings.cs:355
        public static bool IsBuildTargetSupported(BuildTargetGroup buildTargetGroup, BuildTarget target) => false;  // Editor/Mono/BuildPipeline.bindings.cs:615
    }

    // Editor/Mono/EditorBuildSettings.bindings.cs:22
    public partial class EditorBuildSettingsScene : IComparable
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }   // Editor/Mono/EditorBuildSettings.bindings.cs:31
        public bool enabled { get; set; }                                // Editor/Mono/EditorBuildSettings.bindings.cs:45
        public string path { get; set; }                                 // Editor/Mono/EditorBuildSettings.bindings.cs:46
        public int CompareTo(object obj) => 0;                           // Editor/Mono/EditorBuildSettings.bindings.cs:53
    }

    // Editor/Mono/EditorBuildSettings.bindings.cs:74
    public partial class EditorBuildSettings : UnityEngine.Object
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }    // Editor/Mono/EditorBuildSettings.bindings.cs:85
    }

    // Editor/Mono/EditorUserBuildSettings.bindings.cs:358
    public partial class EditorUserBuildSettings : UnityEngine.Object
    {
        public static BuildTarget activeBuildTarget => default;                                          // Editor/Mono/EditorUserBuildSettings.bindings.cs:784
        public static bool SwitchActiveBuildTarget(BuildTargetGroup targetGroup, BuildTarget target) => false; // Editor/Mono/EditorUserBuildSettings.bindings.cs:860
    }

    // Editor/Mono/PlayerSettings.bindings.cs:130
    public enum UIOrientation { Portrait = 0, PortraitUpsideDown = 1, LandscapeRight = 2, LandscapeLeft = 3, AutoRotation = 4 }

    // Editor/Mono/PlayerSettingsWebGL.bindings.cs:11
    public enum WebGLExceptionSupport { None = 0, ExplicitlyThrownExceptionsOnly = 1, FullWithoutStacktrace = 2, FullWithStacktrace = 3 }

    // Editor/Mono/PlayerSettingsWebGL.bindings.cs:19
    public enum WebGLCompressionFormat { Brotli, Gzip, Disabled }

    // Editor/Mono/PlayerSettings.bindings.cs:388
    public sealed partial class PlayerSettings : UnityEngine.Object
    {
        public static string companyName { get; set; }                                   // Editor/Mono/PlayerSettings.bindings.cs:545
        public static string productName { get; set; }                                   // Editor/Mono/PlayerSettings.bindings.cs:548
        public static bool runInBackground { get; set; }                                 // Editor/Mono/PlayerSettings.bindings.cs:612
        public static UnityEngine.Rendering.GraphicsDeviceType[] GetGraphicsAPIs(BuildTarget platform) => null;     // Editor/Mono/PlayerSettings.bindings.cs:874
        public static void SetGraphicsAPIs(BuildTarget platform, UnityEngine.Rendering.GraphicsDeviceType[] apis) { } // Editor/Mono/PlayerSettings.bindings.cs:876
        public static void SetUseDefaultGraphicsAPIs(BuildTarget platform, bool automatic) { }                      // Editor/Mono/PlayerSettings.bindings.cs:889
        public static UIOrientation defaultInterfaceOrientation { get; set; }            // Editor/Mono/PlayerSettings.bindings.cs:1618
        public static bool allowedAutorotateToPortrait { get; set; }                     // Editor/Mono/PlayerSettings.bindings.cs:1622
        public static bool allowedAutorotateToPortraitUpsideDown { get; set; }           // Editor/Mono/PlayerSettings.bindings.cs:1626
        public static bool allowedAutorotateToLandscapeRight { get; set; }               // Editor/Mono/PlayerSettings.bindings.cs:1630
        public static bool allowedAutorotateToLandscapeLeft { get; set; }                // Editor/Mono/PlayerSettings.bindings.cs:1634

        // Editor/Mono/PlayerSettingsSplashScreen.bindings.cs:63
        public partial class SplashScreen
        {
            public static bool show { get; set; }                                        // Editor/Mono/PlayerSettingsSplashScreen.bindings.cs:111
            public static bool showUnityLogo { get; set; }                               // Editor/Mono/PlayerSettingsSplashScreen.bindings.cs:114
        }

        // Editor/Mono/PlayerSettingsWebGL.bindings.cs:65
        public sealed class WebGL
        {
            public static WebGLExceptionSupport exceptionSupport { get; set; }           // Editor/Mono/PlayerSettingsWebGL.bindings.cs:75
            public static bool dataCaching { get; set; }                                 // Editor/Mono/PlayerSettingsWebGL.bindings.cs:82
            public static WebGLCompressionFormat compressionFormat { get; set; }         // Editor/Mono/PlayerSettingsWebGL.bindings.cs:145
            public static bool decompressionFallback { get; set; }                       // Editor/Mono/PlayerSettingsWebGL.bindings.cs:188
        }
    }
}
