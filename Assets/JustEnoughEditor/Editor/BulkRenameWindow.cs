using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JustEnoughEditor.BulkRename;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    /// <summary>
    /// ステップチェーン方式の一括リネームウィンドウ。
    /// 選択中の GameObject / Asset に対して、複数のリネームステップを上から順に適用する。
    /// </summary>
    public class BulkRenameWindow : EditorWindow
    {
        private static readonly string[] StepTypeLabels =
        {
            "Find & Replace", "Add Prefix/Suffix", "Remove Characters",
            "Set Name", "Change Case", "Trim/Normalize"
        };

        /// <summary>プレビュー 1 行分の計算結果。</summary>
        private struct PreviewEntry
        {
            public Object Target;
            public string OldName;      // 表示用 (アセットは拡張子付き)
            public string NewName;      // 表示用 (アセットは拡張子付き)
            public string NewBaseName;  // 適用用 (拡張子なし)
            public bool Skip;
            public string SkipReason;
            public bool WillChange;
        }

        private readonly List<Object> targets = new List<Object>();
        private RenamePipeline pipeline = new RenamePipeline();
        private ReorderableList stepList;
        private Vector2 previewScroll;
        private bool lockTargets;
        private string lastResultMessage = "";
        private MessageType lastResultType = MessageType.Info;

        private GUIStyle changedStyle;
        private GUIStyle skippedStyle;
        private GUIStyle unchangedStyle;

        public static void ShowWindow()
        {
            var window = GetWindow<BulkRenameWindow>("Bulk Rename");
            window.RefreshFromSelection();
        }

        private void OnEnable()
        {
            if (pipeline.Steps.Count == 0)
                pipeline.Steps.Add(new RenameStep { Type = RenameStepType.FindReplace });

            BuildStepList();
            RefreshFromSelection();
        }

        private void OnSelectionChange()
        {
            if (!lockTargets) RefreshFromSelection();
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
            EnsureStyles();

            DrawTargetBar();
            EditorGUILayout.Space();

            stepList.DoLayoutList();

            if (UsesNumberToken())
                DrawNumberingSection();

            var validationOk = BulkRenameOperations.TryValidate(pipeline, out var validationError);
            if (!validationOk)
                EditorGUILayout.HelpBox(validationError, MessageType.Error);

            EditorGUILayout.Space();
            var previews = validationOk ? ComputePreviews() : new List<PreviewEntry>();
            DrawPreview(previews);

            EditorGUILayout.Space();
            if (targets.Any(t => t != null && IsAsset(t)))
                EditorGUILayout.HelpBox("Renaming assets cannot be undone.", MessageType.Warning);

            var changeCount = previews.Count(p => !p.Skip && p.WillChange);
            using (new EditorGUI.DisabledScope(!validationOk || changeCount == 0))
            {
                if (GUILayout.Button($"Apply ({changeCount})", GUILayout.Height(28f)))
                    ApplyRename(previews);
            }

            if (!string.IsNullOrEmpty(lastResultMessage))
                EditorGUILayout.HelpBox(lastResultMessage, lastResultType);
        }

        private void EnsureStyles()
        {
            if (changedStyle != null) return;

            changedStyle = new GUIStyle(EditorStyles.label);
            changedStyle.normal.textColor = new Color(0.4f, 0.8f, 0.4f);
            skippedStyle = new GUIStyle(EditorStyles.label);
            skippedStyle.normal.textColor = new Color(0.95f, 0.45f, 0.45f);
            unchangedStyle = new GUIStyle(EditorStyles.label);
            unchangedStyle.normal.textColor = Color.gray;
        }

        // ---- ターゲットバー ----

        private void DrawTargetBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Targets: {targets.Count}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            var newLock = GUILayout.Toggle(lockTargets, "Lock Targets", "Button", GUILayout.Width(100f));
            if (newLock != lockTargets)
            {
                lockTargets = newLock;
                if (!lockTargets) RefreshFromSelection();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ---- ステップリスト ----

        private void BuildStepList()
        {
            stepList = new ReorderableList(pipeline.Steps, typeof(RenameStep), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Rename Steps (applied top to bottom)"),
                drawElementCallback = DrawStepElement,
                elementHeightCallback = GetStepHeight,
                onAddDropdownCallback = ShowAddStepMenu
            };
        }

        private static float Rows(int count)
        {
            return count * (EditorGUIUtility.singleLineHeight + 2f) + 6f;
        }

        private float GetStepHeight(int index)
        {
            switch (pipeline.Steps[index].Type)
            {
                case RenameStepType.FindReplace: return Rows(4);
                case RenameStepType.AddPrefixSuffix: return Rows(3);
                case RenameStepType.RemoveCharacters: return Rows(3);
                case RenameStepType.SetName: return Rows(2);
                case RenameStepType.ChangeCase: return Rows(2);
                case RenameStepType.TrimNormalize: return Rows(4);
                default: return Rows(1);
            }
        }

        private void ShowAddStepMenu(Rect buttonRect, ReorderableList list)
        {
            var menu = new GenericMenu();
            for (var i = 0; i < StepTypeLabels.Length; i++)
            {
                var type = (RenameStepType)i;
                menu.AddItem(new GUIContent(StepTypeLabels[i]), false, () =>
                {
                    pipeline.Steps.Add(new RenameStep { Type = type });
                    Repaint();
                });
            }
            menu.DropDown(buttonRect);
        }

        private static Rect NextRow(Rect row)
        {
            return new Rect(row.x, row.y + EditorGUIUtility.singleLineHeight + 2f, row.width, EditorGUIUtility.singleLineHeight);
        }

        private void DrawStepElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var step = pipeline.Steps[index];
            var line = EditorGUIUtility.singleLineHeight;
            var row = new Rect(rect.x, rect.y + 3f, rect.width, line);

            var toggleRect = new Rect(row.x, row.y, 18f, line);
            step.Enabled = EditorGUI.Toggle(toggleRect, step.Enabled);

            var typeRect = new Rect(row.x + 22f, row.y, row.width - 22f, line);
            step.Type = (RenameStepType)EditorGUI.Popup(typeRect, (int)step.Type, StepTypeLabels);

            using (new EditorGUI.DisabledScope(!step.Enabled))
            {
                switch (step.Type)
                {
                    case RenameStepType.FindReplace:
                        row = NextRow(row);
                        step.FindText = EditorGUI.TextField(row, "Find", step.FindText);
                        row = NextRow(row);
                        step.ReplaceText = EditorGUI.TextField(row, "Replace", step.ReplaceText);
                        row = NextRow(row);
                        var half = new Rect(row.x, row.y, row.width / 2f, line);
                        step.UseRegex = EditorGUI.ToggleLeft(half, "Regex", step.UseRegex);
                        half.x += row.width / 2f;
                        step.CaseSensitive = EditorGUI.ToggleLeft(half, "Case Sensitive", step.CaseSensitive);
                        break;

                    case RenameStepType.AddPrefixSuffix:
                        row = NextRow(row);
                        step.AddPrefix = EditorGUI.TextField(row, "Prefix", step.AddPrefix);
                        row = NextRow(row);
                        step.AddSuffix = EditorGUI.TextField(row, "Suffix", step.AddSuffix);
                        break;

                    case RenameStepType.RemoveCharacters:
                        row = NextRow(row);
                        step.RemoveFromStart = Mathf.Max(0, EditorGUI.IntField(row, "From Start", step.RemoveFromStart));
                        row = NextRow(row);
                        step.RemoveFromEnd = Mathf.Max(0, EditorGUI.IntField(row, "From End", step.RemoveFromEnd));
                        break;

                    case RenameStepType.SetName:
                        row = NextRow(row);
                        step.NamePattern = EditorGUI.TextField(row, "Name Pattern", step.NamePattern);
                        break;

                    case RenameStepType.ChangeCase:
                        row = NextRow(row);
                        step.CaseConversion = (CaseConversion)EditorGUI.EnumPopup(row, "Convert To", step.CaseConversion);
                        break;

                    case RenameStepType.TrimNormalize:
                        row = NextRow(row);
                        step.TrimWhitespace = EditorGUI.ToggleLeft(row, "Trim Whitespace", step.TrimWhitespace);
                        row = NextRow(row);
                        step.CollapseWhitespace = EditorGUI.ToggleLeft(row, "Collapse Whitespace", step.CollapseWhitespace);
                        row = NextRow(row);
                        step.RemoveDuplicateSuffix = EditorGUI.ToggleLeft(row, "Remove \" (1)\" Suffix", step.RemoveDuplicateSuffix);
                        break;
                }
            }
        }

        // ---- Numbering ----

        private bool UsesNumberToken()
        {
            foreach (var step in pipeline.Steps)
            {
                if (step == null || !step.Enabled) continue;
                if ((step.ReplaceText ?? "").Contains("<num>")) return true;
                if ((step.AddPrefix ?? "").Contains("<num>")) return true;
                if ((step.AddSuffix ?? "").Contains("<num>")) return true;
                if ((step.NamePattern ?? "").Contains("<num>")) return true;
            }
            return false;
        }

        private void DrawNumberingSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Numbering (<num>)", EditorStyles.boldLabel);
            pipeline.StartNumber = Mathf.Max(0, EditorGUILayout.IntField("Start Number", pipeline.StartNumber));
            pipeline.Digits = Mathf.Clamp(EditorGUILayout.IntField("Digits", pipeline.Digits), 1, 10);
        }

        // ---- プレビュー ----

        private List<PreviewEntry> ComputePreviews()
        {
            var entries = new List<PreviewEntry>(targets.Count);

            for (var i = 0; i < targets.Count; i++)
            {
                var obj = targets[i];
                if (obj == null) continue;

                var entry = new PreviewEntry { Target = obj };
                try
                {
                    if (IsAsset(obj)) FillAssetPreview(ref entry, obj, i);
                    else FillGameObjectPreview(ref entry, obj, i);
                }
                catch (Exception e)
                {
                    // 正規表現のタイムアウト等。行単位でスキップ扱いにする
                    entry.OldName = obj.name;
                    entry.NewName = obj.name;
                    entry.Skip = true;
                    entry.SkipReason = e.Message;
                }

                entries.Add(entry);
            }

            return entries;
        }

        private void FillAssetPreview(ref PreviewEntry entry, Object obj, int index)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            var extension = Path.GetExtension(path);
            var baseName = Path.GetFileNameWithoutExtension(path);
            var newBase = BulkRenameOperations.GenerateNewName(baseName, index, pipeline);

            entry.OldName = baseName + extension;
            entry.NewName = newBase + extension;
            entry.NewBaseName = newBase;
            entry.WillChange = !string.Equals(newBase, baseName, StringComparison.Ordinal);

            if (string.IsNullOrEmpty(newBase))
            {
                entry.Skip = true;
                entry.SkipReason = "Name is empty";
                return;
            }

            if (!BulkRenameOperations.IsValidFileName(newBase))
            {
                entry.Skip = true;
                entry.SkipReason = "Contains invalid characters";
                return;
            }

            var directory = Path.GetDirectoryName(path) ?? "";
            var newPath = (directory.Length > 0 ? directory + "/" : "") + newBase + extension;
            newPath = newPath.Replace('\\', '/');

            if (!string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
            {
                entry.Skip = true;
                entry.SkipReason = "A file with this name already exists";
            }
        }

        private void FillGameObjectPreview(ref PreviewEntry entry, Object obj, int index)
        {
            var newName = BulkRenameOperations.GenerateNewName(obj.name, index, pipeline);

            entry.OldName = obj.name;
            entry.NewName = newName;
            entry.NewBaseName = newName;
            entry.WillChange = !string.Equals(newName, obj.name, StringComparison.Ordinal);

            if (string.IsNullOrEmpty(newName))
            {
                entry.Skip = true;
                entry.SkipReason = "Name is empty";
            }
        }

        private void DrawPreview(List<PreviewEntry> previews)
        {
            var changeCount = previews.Count(p => !p.Skip && p.WillChange);
            var skipCount = previews.Count(p => p.Skip);
            EditorGUILayout.LabelField($"Preview — {changeCount} will change, {skipCount} skipped", EditorStyles.boldLabel);

            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.MinHeight(120f), GUILayout.ExpandHeight(true));

            var halfWidth = Mathf.Max(120f, (position.width - 60f) / 2f);
            foreach (var p in previews)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent(p.OldName, p.OldName), GUILayout.Width(halfWidth));
                EditorGUILayout.LabelField("→", GUILayout.Width(20f));

                if (p.Skip)
                    EditorGUILayout.LabelField(new GUIContent($"(skip: {p.SkipReason})", p.SkipReason), skippedStyle);
                else if (p.WillChange)
                    EditorGUILayout.LabelField(new GUIContent(p.NewName, p.NewName), changedStyle);
                else
                    EditorGUILayout.LabelField(new GUIContent(p.NewName, p.NewName), unchangedStyle);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // ---- 適用 ----

        private static bool IsAsset(Object obj)
        {
            return !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj));
        }

        private void ApplyRename(List<PreviewEntry> previews)
        {
            var successCount = 0;
            var skipCount = 0;
            var errors = new List<string>();

            foreach (var entry in previews)
            {
                if (entry.Skip || !entry.WillChange)
                {
                    if (entry.Skip) skipCount++;
                    continue;
                }

                if (IsAsset(entry.Target))
                {
                    var path = AssetDatabase.GetAssetPath(entry.Target);
                    var error = AssetDatabase.RenameAsset(path, entry.NewBaseName);
                    if (!string.IsNullOrEmpty(error))
                    {
                        skipCount++;
                        errors.Add($"{entry.OldName}: {error}");
                        continue;
                    }
                }
                else
                {
                    Undo.RecordObject(entry.Target, "Bulk Rename");
                    entry.Target.name = entry.NewBaseName;
                }

                successCount++;
            }

            AssetDatabase.Refresh();

            lastResultMessage = $"{successCount} renamed, {skipCount} skipped";
            if (errors.Count > 0)
                lastResultMessage += "\n" + string.Join("\n", errors.Take(5));
            lastResultType = errors.Count > 0 ? MessageType.Warning : MessageType.Info;

            if (!lockTargets) RefreshFromSelection();
        }
    }
}
