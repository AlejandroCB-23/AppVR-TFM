// Assets/Editor/WaveSDKPostProcessor.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;

public class WaveSDKPostProcessor : AssetPostprocessor
{
    private const string Symbol = "WAVE_SDK_IMPORTED";

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // Verificar si hubo cambios en archivos del SDK
        bool sdkModified = importedAssets.Concat(deletedAssets).Concat(movedFromAssetPaths)
            .Any(p => p.Contains("Wave.Essence") || p.Contains("WaveVR"));

        if (sdkModified)
        {
            CheckSDKPresence();
        }
    }

    private static void CheckSDKPresence()
    {
        // Buscar el SDK en todo el proyecto
        bool sdkExists = AssetDatabase.FindAssets("")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Any(p => p.Contains("Wave.Essence") || p.Contains("WaveVR"));

        if (sdkExists)
        {
            AddSymbol();
        }
        else
        {
            RemoveSymbol();
        }
    }

    private static void AddSymbol()
    {
        BuildTargetGroup buildTarget = EditorUserBuildSettings.selectedBuildTargetGroup;
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTarget);

        if (!defines.Contains(Symbol))
        {
            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                buildTarget,
                string.IsNullOrEmpty(defines) ? Symbol : $"{defines};{Symbol}");

            Debug.Log($"[Wave SDK] Símbolo {Symbol} agregado.");
        }
    }

    private static void RemoveSymbol()
    {
        BuildTargetGroup buildTarget = EditorUserBuildSettings.selectedBuildTargetGroup;
        string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTarget);

        if (defines.Contains(Symbol))
        {
            var newDefines = defines.Split(';')
                .Where(s => s != Symbol)
                .ToArray();

            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                buildTarget,
                string.Join(";", newDefines));

            Debug.LogWarning($"[Wave SDK] Símbolo {Symbol} eliminado.");
        }
    }
}
#endif
