using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BlockFreeFall.EditorTools
{
    /// <summary>
    /// コマンドラインからのビルド。
    ///   -executeMethod BlockFreeFall.EditorTools.BuildScript.BuildWebGL → unity/Builds/WebGL
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
            PlayerSettings.WebGL.template = "PROJECT:BFF";
            AssetDatabase.SaveAssets();
        }

        [MenuItem("BlockFreeFall/Build WebGL")]
        public static void BuildWebGL()
        {
            Setup();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/WebGL"));
            if (Directory.Exists(output)) Directory.Delete(output, true);
            var scenes = new[] { "Assets/Scenes/SampleScene.unity" };
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });
            Debug.Log("BlockFreeFall: build " + report.summary.result + " -> " + output);
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
