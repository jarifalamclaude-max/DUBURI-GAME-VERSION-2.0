// Menu "Life as Duburi / Build WebGL": checks the project (Set Up Project if
// something is missing), switches the target to WebGL when needed (asks first),
// builds to <project>/Builds/WebGL and says how big it is and how to test and
// share it. "Build WebGL (Development)" makes a Development Build in Builds/WebGL-Dev
// for testers: it also shows the red error box on screen (release builds only log
// errors to the browser console).

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Duburi.EditorTools
{
    public static class WebGLBuild
    {
        [MenuItem("Life as Duburi/Build WebGL", false, 20)]
        public static void BuildMenu() => Build(false);

        /// a Development Build: the on-screen error box (ErrorPanel) works in it
        [MenuItem("Life as Duburi/Build WebGL (Development)", false, 21)]
        public static void BuildDevMenu() => Build(true);

        static void Build(bool development)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Build WebGL", "Leave Play mode first.", "OK");
                return;
            }

            // 1. the set-up checks
            var report = new ProjectSetup.Report();
            bool ok = ProjectSetup.Check(report);
            if (!ok)
            {
                report.Show("Build WebGL: the project is not ready", "Fix the items marked ✗, then build again.");
                return;
            }
            if (!IsSetUp())
            {
                if (!EditorUtility.DisplayDialog("Build WebGL", "The project is not fully set up yet (scene, materials or post-processing profiles missing — after an update of the game files Set Up Project must run again). Run Set Up Project now?", "Set Up and continue", "Cancel"))
                    return;
                report = new ProjectSetup.Report();
                if (!ProjectSetup.Run(report, interactive: true))
                {
                    report.Show("Build WebGL: set-up needs attention", "Fix the items marked ✗, then build again.");
                    return;
                }
            }
            else if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // 2. the WebGL target
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                EditorUtility.DisplayDialog("Build WebGL",
                    "The Web Build Support module is not installed.\n\nUnity Hub > Installs > (this Unity version) > ⚙ > Add modules > Web Build Support, then restart Unity.",
                    "OK");
                return;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                if (!EditorUtility.DisplayDialog("Build WebGL",
                        "Switch the project to the WebGL platform? Unity re-imports the assets for WebGL (this can take a few minutes the first time).",
                        "Switch to WebGL", "Cancel"))
                    return;
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                {
                    EditorUtility.DisplayDialog("Build WebGL", "Unity could not switch to WebGL. Try File > Build Profiles > Web > Switch Platform.", "OK");
                    return;
                }
            }

            // 3. build
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s != null && s.enabled) scenes.Add(s.path);
            if (scenes.Count == 0) scenes.Add(ProjectSetup.ScenePath);
            string project = Path.GetDirectoryName(Application.dataPath);
            string outDir = Path.Combine(project, "Builds", development ? "WebGL-Dev" : "WebGL");
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport result = BuildPipeline.BuildPlayer(options);
            var sum = result != null ? result.summary : default;
            if (result == null || sum.result != BuildResult.Succeeded)
            {
                string why = result == null ? "no report" : sum.result + ", " + sum.totalErrors + " error(s)";
                Debug.LogError("Life as Duburi: WebGL build failed (" + why + "). See the Console for the errors.");
                EditorUtility.DisplayDialog("Build WebGL failed", "The build failed (" + why + ").\nThe Console lists the errors.", "OK");
                return;
            }

            // 4. report
            long bytes = FolderSize(outDir);
            string size = (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            string msg =
                (development ? "Development Build (shows the red error box). " : "") +
                "Built in " + Math.Round(sum.totalTime.TotalMinutes, 1) + " min: " + size + " in\n" + outDir + "\n\n" +
                "Test it: the page must come from a web server, not from a file. In that folder run\n" +
                "    python -m http.server 8000\n" +
                "and open http://localhost:8000 (or use File > Build And Run).\n\n" +
                "Share it: zip the CONTENTS of the folder (index.html at the top of the zip) and upload it to itch.io " +
                "as an HTML5 game, or drop the folder on Netlify Drop (app.netlify.com/drop), or push it to a GitHub Pages branch. " +
                "Gzip with the decompression fallback works on any static host.";
            Debug.Log("Life as Duburi: WebGL build done.\n" + msg);
            if (EditorUtility.DisplayDialog("Build WebGL done", msg, "Show folder", "Close"))
                EditorUtility.RevealInFinder(Path.Combine(outDir, "index.html"));
        }

        static bool IsSetUp()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ProjectSetup.ScenePath) == null) return false;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ProjectSetup.PostProfilePath) == null) return false;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ProjectSetup.PostProfileUltraPath) == null) return false; // added with Ultra graphics
            if (AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.MaterialsDir + "/Lit.mat") == null) return false;
            var list = EditorBuildSettings.scenes;
            return list.Length > 0 && list[0].path == ProjectSetup.ScenePath && list[0].enabled;
        }

        static long FolderSize(string dir)
        {
            long n = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) n += new FileInfo(f).Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return n;
        }
    }
}
