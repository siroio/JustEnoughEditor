using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JustEnoughEditor.BulkRename;
using UnityEditor;
using UnityEditor.ShortcutManagement;
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
            public string DestinationKey;
            public string SequenceNumber;
            public bool Skip;
            public string SkipReason;
            public bool WillChange;
        }

        private readonly List<Object> targets = new List<Object>();
        private readonly HashSet<int> excludedTargetIds = new HashSet<int>();
        private RenamePipeline pipeline = new RenamePipeline();
        private ReorderableList stepList;
        private RenameStep expandedStep;
        private Vector2 previewScroll;
        private bool lockTargets;
        private bool showTargets;
        private bool showChangesOnly;
        private string lastResultMessage = "";
        private MessageType lastResultType = MessageType.Info;

        // プレビュー再計算の抑制: パイプライン内容とターゲット選択が変わっていなければ、
        // 毎フレームの OnGUI で ComputePreviews (正規表現含む) を再実行しないようキャッシュする
        private List<PreviewEntry> cachedPreviews;
        private string cachedPreviewKey;

        private GUIStyle changedStyle;
        private GUIStyle skippedStyle;
        private GUIStyle unchangedStyle;

        private const string PipelinePrefsKey = "JEE_BulkRenamePipeline";

        public static void ShowWindow()
        {
            var window = GetWindow<BulkRenameWindow>("Bulk Rename");
            window.RefreshFromSelection();
        }

        /// <summary>ShortcutManager 経由のウィンドウオープン。Edit > Shortcuts の "JEE/Bulk Rename" でリバインド可能。</summary>
        [Shortcut("JEE/Bulk Rename", KeyCode.R, ShortcutModifiers.Alt)]
        private static void OpenViaShortcut()
        {
            ShowWindow();
        }

        private void OnEnable()
        {
            LoadPipeline();

            if (pipeline.Steps.Count == 0)
                pipeline.Steps.Add(new RenameStep { Type = RenameStepType.FindReplace });

            expandedStep = pipeline.Steps[0];
            BuildStepList();
            RefreshFromSelection();
        }

        private void OnDisable()
        {
            SavePipeline();
        }

        private void LoadPipeline()
        {
            var json = EditorPrefs.GetString(PipelinePrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                pipeline = JsonUtility.FromJson<RenamePipeline>(json) ?? new RenamePipeline();
            }
            catch (Exception)
            {
                pipeline = new RenamePipeline();
            }
        }

        private void SavePipeline()
        {
            EditorPrefs.SetString(PipelinePrefsKey, JsonUtility.ToJson(pipeline));
        }

        private void OnSelectionChange()
        {
            if (!lockTargets) RefreshFromSelection();
        }

        private void RefreshFromSelection()
        {
            targets.Clear();
            targets.AddRange(Selection.objects);
            excludedTargetIds.Clear();
            cachedPreviewKey = null;
            lastResultMessage = "";
            Repaint();
        }

        private void OnGUI()
        {
            HandleKeyboard();
            EnsureStyles();

            DrawTargetBar();
            EditorGUILayout.Space();

            stepList.DoLayoutList();
            DrawRuleActions();

            if (UsesNumberToken())
                DrawNumberingSection();

            var validationOk = BulkRenameOperations.TryValidate(pipeline, out var validationError);
            if (!validationOk)
                EditorGUILayout.HelpBox(validationError, MessageType.Error);

            EditorGUILayout.Space();
            var previews = validationOk ? GetPreviews() : new List<PreviewEntry>();
            DrawPreview(previews);

            EditorGUILayout.Space();
            var assetChangeCount = previews.Count(p => IsIncluded(p) && !p.Skip && p.WillChange && JEEAssetUtility.IsAsset(p.Target));
            if (assetChangeCount > 0)
                EditorGUILayout.HelpBox($"{assetChangeCount} asset rename(s) cannot be undone.", MessageType.Warning);

            var changeCount = previews.Count(p => IsIncluded(p) && !p.Skip && p.WillChange);
            using (new EditorGUI.DisabledScope(!validationOk || changeCount == 0))
            {
                if (GUILayout.Button($"Rename {changeCount} Item{(changeCount == 1 ? "" : "s")}", GUILayout.Height(30f)))
                    ConfirmAndApply(previews);
            }

            if (!string.IsNullOrEmpty(lastResultMessage))
                EditorGUILayout.HelpBox(lastResultMessage, lastResultType);
        }

        /// <summary>Enter で Apply、Esc でウィンドウを閉じる。テキスト編集中はどちらも無効。</summary>
        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;

            if (e.keyCode == KeyCode.Escape)
            {
                e.Use();
                Close();
                GUIUtility.ExitGUI();
                return;
            }

            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            if (!BulkRenameOperations.TryValidate(pipeline, out _)) return;

            var previews = GetPreviews();
            if (previews.Any(p => IsIncluded(p) && !p.Skip && p.WillChange))
            {
                e.Use();
                ConfirmAndApply(previews);
            }
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
            showTargets = EditorGUILayout.Foldout(showTargets, $"Targets ({targets.Count})", true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();

            var followSelection = !lockTargets;
            var newFollowSelection = GUILayout.Toggle(followSelection, "Follow Selection", "Button", GUILayout.Width(110f));
            if (newFollowSelection != followSelection)
            {
                lockTargets = !newFollowSelection;
                if (newFollowSelection) RefreshFromSelection();
            }

            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(48f)))
                {
                    targets.Clear();
                    excludedTargetIds.Clear();
                    cachedPreviewKey = null;
                    lockTargets = true;
                }
            }

            EditorGUILayout.EndHorizontal();

            if (showTargets)
                DrawTargetList();

            DrawTargetDropArea();
        }

        private void DrawTargetList()
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(target, typeof(Object), true);

                if (GUILayout.Button("×", GUILayout.Width(24f)))
                {
                    if (target != null) excludedTargetIds.Remove(target.GetInstanceID());
                    targets.RemoveAt(i);
                    cachedPreviewKey = null;
                    lockTargets = true;
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTargetDropArea()
        {
            var rect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, targets.Count == 0 ? "Drop GameObjects or assets here" : "Drop to add more targets", EditorStyles.helpBox);

            var e = Event.current;
            if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform))
                return;

            var validTargets = DragAndDrop.objectReferences.Where(CanRenameTarget).ToArray();
            DragAndDrop.visualMode = validTargets.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (e.type != EventType.DragPerform || validTargets.Length == 0)
            {
                e.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            foreach (var target in validTargets)
            {
                if (targets.All(existing => existing == null || existing.GetInstanceID() != target.GetInstanceID()))
                    targets.Add(target);
            }

            lockTargets = true;
            cachedPreviewKey = null;
            e.Use();
        }

        private static bool CanRenameTarget(Object target)
        {
            return target is GameObject || (target != null && JEEAssetUtility.IsAsset(target));
        }

        // ---- ステップリスト ----

        private void BuildStepList()
        {
            stepList = new ReorderableList(pipeline.Steps, typeof(RenameStep), true, true, false, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Rename Rules — applied top to bottom"),
                drawElementCallback = DrawStepElement,
                elementHeightCallback = GetStepHeight,
                onRemoveCallback = list =>
                {
                    var removed = list.index >= 0 && list.index < pipeline.Steps.Count
                        ? pipeline.Steps[list.index]
                        : null;
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                    if (ReferenceEquals(expandedStep, removed)) expandedStep = null;
                }
            };
        }

        private static float Rows(int count)
        {
            return count * (EditorGUIUtility.singleLineHeight + 2f) + 6f;
        }

        private float GetStepHeight(int index)
        {
            if (!ReferenceEquals(pipeline.Steps[index], expandedStep)) return Rows(1);

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
                    var step = new RenameStep { Type = type };
                    pipeline.Steps.Add(step);
                    expandedStep = step;
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

            var foldoutRect = new Rect(row.x, row.y, 16f, line);
            var expanded = ReferenceEquals(step, expandedStep);
            var newExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none);
            if (newExpanded != expanded) expandedStep = newExpanded ? step : null;

            var toggleRect = new Rect(row.x + 18f, row.y, 18f, line);
            step.Enabled = EditorGUI.Toggle(toggleRect, step.Enabled);

            var typeRect = new Rect(row.x + 40f, row.y, row.width - 40f, line);
            step.Type = (RenameStepType)EditorGUI.Popup(typeRect, (int)step.Type, StepTypeLabels);

            if (!ReferenceEquals(step, expandedStep)) return;

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

        private void DrawRuleActions()
        {
            EditorGUILayout.BeginHorizontal();

            if (EditorGUILayout.DropdownButton(new GUIContent("+ Add Rule"), FocusType.Passive))
                ShowAddStepMenu(GUILayoutUtility.GetLastRect(), stepList);

            using (new EditorGUI.DisabledScope(UsesNumberToken()))
            {
                if (GUILayout.Button("+ Numbering"))
                {
                    var step = new RenameStep
                    {
                        Type = RenameStepType.AddPrefixSuffix,
                        AddSuffix = "_<num>"
                    };
                    pipeline.Steps.Add(step);
                    expandedStep = step;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ---- Numbering ----

        private bool UsesNumberToken()
        {
            // フラットな RenameStep は Type を切り替えても他フィールドの値が残るため、
            // 現在の Type に関係するフィールドのみをチェックする(古い値の残留で誤検知させない)
            foreach (var step in pipeline.Steps)
            {
                if (step == null || !step.Enabled) continue;

                switch (step.Type)
                {
                    case RenameStepType.FindReplace:
                        if ((step.ReplaceText ?? "").Contains("<num>")) return true;
                        break;
                    case RenameStepType.AddPrefixSuffix:
                        if ((step.AddPrefix ?? "").Contains("<num>")) return true;
                        if ((step.AddSuffix ?? "").Contains("<num>")) return true;
                        break;
                    case RenameStepType.SetName:
                        if ((step.NamePattern ?? "").Contains("<num>")) return true;
                        break;
                }
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

        /// <summary>プレビュー再計算の抑制: キャッシュキー(パイプライン JSON + ターゲットの InstanceID 列)が
        /// 前回から変化していなければキャッシュ済みの結果をそのまま返す。毎回の OnGUI や Enter キー押下のたびに
        /// 正規表現マッチングを N ターゲット分やり直すのを避けるための措置。</summary>
        private List<PreviewEntry> GetPreviews()
        {
            var key = JsonUtility.ToJson(pipeline) + "|" +
                       string.Join(",", targets.Select(t => t != null ? t.GetInstanceID() : 0)) + "|" +
                       string.Join(",", excludedTargetIds.OrderBy(id => id));

            if (cachedPreviews == null || cachedPreviewKey != key)
            {
                cachedPreviews = ComputePreviews();
                cachedPreviewKey = key;
            }

            return cachedPreviews;
        }

        private List<PreviewEntry> ComputePreviews()
        {
            var entries = new List<PreviewEntry>(targets.Count);

            for (var i = 0; i < targets.Count; i++)
            {
                var obj = targets[i];
                if (obj == null) continue;

                var entry = new PreviewEntry
                {
                    Target = obj,
                    SequenceNumber = BulkRenameOperations.FormatNumber(i, pipeline)
                };
                try
                {
                    if (JEEAssetUtility.IsAsset(obj)) FillAssetPreview(ref entry, obj, i);
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

            var destinationKeys = entries
                .Select(entry => IsIncluded(entry) && !entry.Skip && entry.WillChange
                    ? entry.DestinationKey
                    : null)
                .ToList();

            foreach (var entryIndex in BulkRenameOperations.FindDuplicateIndexes(destinationKeys))
            {
                var entry = entries[entryIndex];
                entry.Skip = true;
                entry.SkipReason = "Multiple targets would use this name";
                entries[entryIndex] = entry;
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
            entry.DestinationKey = newPath;

            if (!string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase) &&
                AssetDatabase.LoadMainAssetAtPath(newPath) != null)
            {
                entry.Skip = true;
                entry.SkipReason = "An asset with this name already exists";
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
            var changeCount = previews.Count(p => IsIncluded(p) && !p.Skip && p.WillChange);
            var skipCount = previews.Count(p => p.Skip);
            var excludedCount = previews.Count(p => p.WillChange && !p.Skip && !IsIncluded(p));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Preview — {changeCount} selected, {skipCount} skipped" +
                (excludedCount > 0 ? $", {excludedCount} excluded" : ""),
                EditorStyles.boldLabel);
            showChangesOnly = GUILayout.Toggle(showChangesOnly, "Changes only", "Button", GUILayout.Width(100f));
            EditorGUILayout.EndHorizontal();

            if (previews.Count == 0)
            {
                EditorGUILayout.HelpBox("Select or drop items to begin.", MessageType.Info);
                return;
            }

            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.MinHeight(120f), GUILayout.ExpandHeight(true));

            var halfWidth = Mathf.Max(100f, (position.width - 110f) / 2f);
            var showSequenceNumber = UsesNumberToken();
            for (var i = 0; i < previews.Count; i++)
            {
                var p = previews[i];
                if (showChangesOnly && !p.WillChange && !p.Skip) continue;

                EditorGUILayout.BeginHorizontal();
                var included = IsIncluded(p);
                var canSelect = p.WillChange;
                using (new EditorGUI.DisabledScope(!canSelect))
                {
                    var newIncluded = EditorGUILayout.Toggle(canSelect && included, GUILayout.Width(18f));
                    if (canSelect && newIncluded != included)
                    {
                        if (newIncluded) excludedTargetIds.Remove(p.Target.GetInstanceID());
                        else excludedTargetIds.Add(p.Target.GetInstanceID());
                        included = newIncluded;
                        cachedPreviewKey = null;
                    }
                }

                EditorGUILayout.LabelField(showSequenceNumber ? $"#{p.SequenceNumber}" : $"#{i + 1}", GUILayout.Width(34f));
                EditorGUILayout.LabelField(new GUIContent(p.OldName, p.OldName), GUILayout.Width(halfWidth));
                EditorGUILayout.LabelField("→", GUILayout.Width(20f));

                if (p.Skip)
                    EditorGUILayout.LabelField(new GUIContent($"(skip: {p.SkipReason})", p.SkipReason), skippedStyle);
                else if (!included)
                    EditorGUILayout.LabelField(new GUIContent($"(excluded) {p.NewName}", p.NewName), unchangedStyle);
                else if (p.WillChange)
                    EditorGUILayout.LabelField(new GUIContent(p.NewName, p.NewName), changedStyle);
                else
                    EditorGUILayout.LabelField(new GUIContent(p.NewName, p.NewName), unchangedStyle);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private bool IsIncluded(PreviewEntry entry)
        {
            return entry.Target != null && !excludedTargetIds.Contains(entry.Target.GetInstanceID());
        }

        // ---- 適用 ----

        private void ConfirmAndApply(List<PreviewEntry> previews)
        {
            var selected = previews.Where(IsIncluded).ToList();
            var assetCount = selected.Count(p => !p.Skip && p.WillChange && JEEAssetUtility.IsAsset(p.Target));
            if (assetCount > 0 && !EditorUtility.DisplayDialog(
                    "Rename assets?",
                    $"{assetCount} asset rename(s) cannot be undone. Review the preview before continuing.",
                    "Rename",
                    "Cancel"))
                return;

            ApplyRename(selected);
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

                if (JEEAssetUtility.IsAsset(entry.Target))
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

            // 名前が変わってもキャッシュキー(パイプライン JSON + InstanceID)は変化しないため、
            // 明示的にキャッシュを無効化してから再計算させる
            cachedPreviewKey = null;

            // RefreshFromSelection は lastResultMessage をクリアするため、結果メッセージの代入より先に呼ぶ
            if (!lockTargets) RefreshFromSelection();

            lastResultMessage = $"{successCount} renamed, {skipCount} skipped";
            if (errors.Count > 0)
                lastResultMessage += "\n" + string.Join("\n", errors.Take(5));
            lastResultType = errors.Count > 0 ? MessageType.Warning : MessageType.Info;
        }
    }
}
