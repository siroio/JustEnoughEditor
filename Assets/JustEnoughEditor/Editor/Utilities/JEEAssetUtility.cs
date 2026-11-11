using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    public static class JEEAssetUtility
    {
        public static string GetSharedDataDirectory(string scriptName)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
                throw new ArgumentException("A script name is required.", nameof(scriptName));

            var guids = AssetDatabase.FindAssets(scriptName + " t:Script");
            if (guids.Length == 0) return "Assets/JustEnoughEditor/Data";

            var scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var packageDir = Path.GetDirectoryName(Path.GetDirectoryName(scriptPath));
            return packageDir.Replace('\\', '/') + "/Data";
        }

        public static bool IsAsset(Object target)
        {
            return target != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target));
        }

        public static bool IsValidAssetPath(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) &&
                   !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath));
        }

        public static bool TryOpenAsset(string assetPath)
        {
            var asset = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null || !AssetDatabase.OpenAsset(asset)) return false;

            Selection.activeObject = asset;
            return true;
        }

        public static T LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            assetPath = ValidateCreationPath(assetPath);

            var existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing != null)
            {
                if (existing is T typed) return typed;
                throw new InvalidOperationException($"An asset of type {existing.GetType().FullName} already exists at {assetPath}.");
            }

            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static string ValidateCreationPath(string assetPath)
        {
            var normalized = assetPath?.Replace('\\', '/');
            var traversesParent = normalized != null &&
                                  (normalized.Contains("/../") ||
                                   normalized.EndsWith("/..", StringComparison.Ordinal) ||
                                   normalized.Contains("/./") ||
                                   normalized.EndsWith("/.", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(normalized) ||
                Path.IsPathRooted(normalized) ||
                !normalized.StartsWith("Assets/", StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(normalized), ".asset", StringComparison.OrdinalIgnoreCase) ||
                traversesParent)
                throw new ArgumentException("Asset path must be a .asset path under Assets/.", nameof(assetPath));

            return normalized;
        }
    }
}
