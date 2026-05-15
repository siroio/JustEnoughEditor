using System.IO;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    public static class JEESettingsIO
    {
        public static void ExportSettings()
        {
            var path = EditorUtility.SaveFilePanel("Export JEE Settings", "", "JustEnoughEditorSettings.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            var json = JsonUtility.ToJson(JEEPrefs.Capture(), true);
            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
        }

        public static void ImportSettings()
        {
            var path = EditorUtility.OpenFilePanel("Import JEE Settings", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            var json = File.ReadAllText(path);
            JEEPrefs.Apply(JsonUtility.FromJson<JEEExportedSettings>(json));
        }
    }
}
