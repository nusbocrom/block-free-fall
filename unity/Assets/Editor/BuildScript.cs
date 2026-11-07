using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BlockFreeFall.EditorTools
{
    /// <summary>
    /// コマンドラインからのビルド。
    ///   -executeMethod BlockFreeFall.EditorTools.BuildScript.BuildWebGL     → unity/Builds/WebGL(GitHub Pages 用。圧縮なし)
    ///   -executeMethod BlockFreeFall.EditorTools.BuildScript.BuildUnityroom → unity/Builds/unityroom(unityroom 用。Gzip で圧縮)
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("BlockFreeFall/Setup Project")]
        public static void Setup()
        {
            PlayerSettings.companyName = "KusogeDev";
            PlayerSettings.productName = "ブロック自由落下";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.runInBackground = true;
            // WebGL: 圧縮なし(サーバーに特別なヘッダー設定を要らなくするため)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template = "PROJECT:BFF";
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BlockFreeFall/Build WebGL")]
        public static void BuildWebGL() => Build("WebGL", WebGLCompressionFormat.Disabled, "");

        /// <summary>
        /// unityroom 用。unityroom の指定どおり Gzip で圧縮し、Decompression Fallback は使わない。
        /// UNITYROOM の印を付けて、シェアの URL を unityroom のページにする。アップロードするのは中の Build フォルダと StreamingAssets。
        /// </summary>
        [MenuItem("BlockFreeFall/Build WebGL (unityroom)")]
        public static void BuildUnityroom() => Build("unityroom", WebGLCompressionFormat.Gzip, "UNITYROOM");

        static void Build(string folder, WebGLCompressionFormat compression, string defines)
        {
            Setup();
            PlayerSettings.WebGL.compressionFormat = compression;
            string savedDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, defines);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds", folder));
            if (Directory.Exists(output)) Directory.Delete(output, true);
            var scenes = new[] { "Assets/Scenes/SampleScene.unity" };
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            // プロジェクトの設定は、いつもの圧縮なし・印なしに戻しておく
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, savedDefines);
            Setup();
            Debug.Log("BlockFreeFall: build " + report.summary.result + " -> " + output);
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
