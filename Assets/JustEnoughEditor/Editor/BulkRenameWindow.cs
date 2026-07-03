using System;
using System.Collections.Generic;
using System.IO;
using JustEnoughEditor.BulkRename;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    /// <summary>
    /// Hierarchy の GameObject と Project の Asset を対象に、複数モードで一括リネームを行うウィンドウ。
    /// </summary>
    public class BulkRenameWindow : EditorWindow
    {
        private static readonly string[] ModeLabels =
        {
            "Find/Replace", "Add Prefix/Suffix", "Sequential Number", "Replace All"
        };

        private readonly List<Object> targets = new List<Object>();
        private readonly BulkRenameSettings settings = new BulkRenameSettings();
        private Vector2 scrollPosition;
        private string lastResultMessage = "";

        public static void ShowWindow()
        {
            var window = GetWindow<BulkRenameWindow>("Bulk Rename");
            window.RefreshFromSelection();
        }

        private void RefreshFromSelection()
        {
            targets.Clear();
            targets.AddRange(Selection.objects);
            lastResultMessage = "";
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"Targets: {targets.Count}", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh from Selection"))
                RefreshFromSelection();

            EditorGUILayout.Space();
            settings.Mode = (RenameMode)GUILayout.Toolbar((int)settings.Mode, ModeLabels);
            EditorGUILayout.Space();

            DrawModeFields();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            DrawPreview();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Renaming assets cannot be undone.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button("Apply"))
                    ApplyRename();
            }

            if (!string.IsNullOrEmpty(lastResultMessage))
                EditorGUILayout.LabelField(lastResultMessage);
        }

        private void DrawModeFields()
        {
            switch (settings.Mode)
            {
                case RenameMode.FindReplace:
                    settings.FindText = EditorGUILayout.TextField("Find", settings.FindText);
                    settings.ReplaceText = EditorGUILayout.TextField("Replace", settings.ReplaceText);
                    settings.CaseSensitive = EditorGUILayout.Toggle("Case Sensitive", settings.CaseSensitive);
                    break;
                case RenameMode.AddPrefixSuffix:
                    settings.AddPrefix = EditorGUILayout.TextField("Add Prefix", settings.AddPrefix);
                    settings.AddSuffix = EditorGUILayout.TextField("Add Suffix", settings.AddSuffix);
                    settings.RemoveFromStart = EditorGUILayout.IntField("Remove From Start", settings.RemoveFromStart);
                    settings.RemoveFromEnd = EditorGUILayout.IntField("Remove From End", settings.RemoveFromEnd);
                    break;
                case RenameMode.SequentialNumber:
                    DrawNumberFields();
                    break;
                case RenameMode.ReplaceAll:
                    settings.NewBaseName = EditorGUILayout.TextField("New Base Name", settings.NewBaseName);
                    settings.AppendNumber = EditorGUILayout.Toggle("Append Number", settings.AppendNumber);
                    if (settings.AppendNumber)
                        DrawNumberFields();
                    break;
            }
        }

        private void DrawNumberFields()
        {
            settings.StartNumber = EditorGUILayout.IntField("Start Number", settings.StartNumber);
            settings.Digits = EditorGUILayout.IntField("Digits", settings.Digits);
            settings.NumberPosition = (NumberPosition)EditorGUILayout.EnumPopup("Position", settings.NumberPosition);
            settings.NumberSeparator = EditorGUILayout.TextField("Separator", settings.NumberSeparator);
        }

        private void DrawPreview()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

            for (var i = 0; i < targets.Count; i++)
            {
                var obj = targets[i];
                if (obj == null) continue;

                var (newName, skip, skipReason) = ComputeNewName(obj, i);

                EditorGUILayout.BeginHorizontal();
                if (skip)
                    EditorGUILayout.LabelField(new GUIContent("!", skipReason), GUILayout.Width(20));
                else
                    GUILayout.Space(20);

                EditorGUILayout.LabelField(obj.name, GUILayout.Width(150));
                EditorGUILayout.LabelField("->", GUILayout.Width(20));
                EditorGUILayout.LabelField(skip ? "(skipped)" : newName);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private static bool IsAsset(Object obj)
        {
            return !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj));
        }

        private (string newName, bool skip, string skipReason) ComputeNewName(Object obj, int index)
        {
            if (IsAsset(obj))
            {
                var path = AssetDatabase.GetAssetPath(obj);
                var extension = Path.GetExtension(path);
                var baseName = Path.GetFileNameWithoutExtension(path);
                var newBase = BulkRenameOperations.GenerateNewName(baseName, index, settings);
                var newFullName = newBase + extension;

                if (string.IsNullOrEmpty(newBase))
                    return (newFullName, true, "Name is empty");
                if (!BulkRenameOperations.IsValidFileName(newBase))
                    return (newFullName, true, "Contains invalid characters");

                var directory = Path.GetDirectoryName(path) ?? "";
                var newPath = (directory.Length > 0 ? directory + "/" : "") + newFullName;
                newPath = newPath.Replace('\\', '/');

                if (!string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
                    return (newFullName, true, "A file with this name already exists");

                return (newFullName, false, null);
            }

            var newName = BulkRenameOperations.GenerateNewName(obj.name, index, settings);
            if (string.IsNullOrEmpty(newName))
                return (newName, true, "Name is empty");

            return (newName, false, null);
        }

        private void ApplyRename()
        {
            var successCount = 0;
            var skipCount = 0;

            for (var i = 0; i < targets.Count; i++)
            {
                var obj = targets[i];
                if (obj == null)
                {
                    skipCount++;
                    continue;
                }

                var (newName, skip, _) = ComputeNewName(obj, i);
                if (skip)
                {
                    skipCount++;
                    continue;
                }

                if (IsAsset(obj))
                {
                    var path = AssetDatabase.GetAssetPath(obj);
                    var newBaseName = Path.GetFileNameWithoutExtension(newName);
                    var error = AssetDatabase.RenameAsset(path, newBaseName);
                    if (!string.IsNullOrEmpty(error))
                    {
                        skipCount++;
                        continue;
                    }
                }
                else
                {
                    Undo.RecordObject(obj, "Bulk Rename");
                    obj.name = newName;
                }

                successCount++;
            }

            AssetDatabase.Refresh();
            lastResultMessage = $"{successCount} renamed, {skipCount} skipped";
            RefreshFromSelection();
        }
    }
}
