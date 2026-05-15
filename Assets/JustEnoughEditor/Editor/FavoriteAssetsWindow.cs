using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    public class FavoriteAssetsWindow : EditorWindow
    {
        private const string PrefsKey = "JustEnoughEditor_FavoriteFolders";
        private const string DataFileName = "FavoriteFoldersData.asset";
        private const float RowActionWidth = 138f;
        private const float RowColorWidth = 46f;
        private const float RowTypeWidth = 54f;
        private const float RowHeight = 28f;
        private static Dictionary<string, Color> folderColors = new();

        private StorageMode currentMode = StorageMode.Personal_EditorPrefs;
        private int draggedIndex = -1;
        private List<FavoriteAssetItem> favoriteItems = new();
        private string hoveredPath = "";
        private bool isReordering;

        private Vector2 scrollPosition;
        private string searchQuery = "";

        // このスクリプト自身の場所を基準に Data/ フォルダのパスを動的に解決する。
        // スクリプトがどこに置かれていても JustEnoughEditor/Data/ に保存される。
        private static string SharedDataDir
        {
            get
            {
                var guids = AssetDatabase.FindAssets("FavoriteAssetsWindow t:Script");
                if (guids.Length > 0)
                {
                    var scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    // .../JustEnoughEditor/Editor/FavoriteAssetsWindow.cs
                    // → .../JustEnoughEditor/Data
                    var editorDir = Path.GetDirectoryName(scriptPath); // .../Editor
                    var packageDir = Path.GetDirectoryName(editorDir); // .../JustEnoughEditor
                    return packageDir.Replace('\\', '/') + "/Data";
                }

                // フォールバック
                return "Assets/JustEnoughEditor/Data";
            }
        }

        private static string SharedDataPath => SharedDataDir + "/" + DataFileName;

        private void OnEnable()
        {
            LoadData();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
                Repaint();

            hoveredPath = "";

            using (new EditorGUILayout.VerticalScope(JEEEditorStyles.Card))
            {
                DrawToolbar();
                DrawSearchField();
                DrawFolderList();
                DrawStatusBar();
            }

            HandleWindowDragAndDrop();
        }


        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            folderColors = new Dictionary<string, Color>();

            EditorApplication.delayCall -= LoadStaticData;
            EditorApplication.delayCall += LoadStaticData;

            EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        }

        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            if (!JEEMenu.IsProjectColorsEnabled) return;
            if (Event.current.type != EventType.Repaint) return;

            if (!folderColors.TryGetValue(guid, out var color)) return;
            if (color == new Color(0, 0, 0, 0)) return; // 色なし(デフォルト)の場合はスキップ

            if (selectionRect.height > 20)
            {
                // Grid View (アイコン表示)
                // アイコンの左側に垂直なカラーバーを描画
                var leftBarRect = new Rect(selectionRect.x + 4, selectionRect.y + 4, 4, selectionRect.width - 8);
                EditorGUI.DrawRect(leftBarRect, new Color(color.r, color.g, color.b, 1f));
            }
            else
            {
                // List View (リスト表示)
                // アイコンの左側にカラーバーを描画
                // selectionRect.x はテキストの開始位置、アイコンは x - 16 なので x - 20 に配置
                var barX = selectionRect.x - 20f;
                if (barX < 2) barX = 2; // 最左端のガード

                var leftBarRect = new Rect(barX, selectionRect.y + 2, 4, selectionRect.height - 4);
                EditorGUI.DrawRect(leftBarRect, new Color(color.r, color.g, color.b, 1f));
            }
        }

        private static void LoadStaticData()
        {
            // PersonalとSharedの両方の設定を読み込んで、ProjectWindowに色を反映させる
            folderColors.Clear();

            // Load Personal
            var json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
                try
                {
                    var data = JsonUtility.FromJson<PersonalData>(json);
                    if (data != null && data.items != null)
                        foreach (var item in data.items)
                            folderColors[item.guid] = item.color;
                }
                catch (Exception)
                {
                }

            // Load Shared
            var dataAsset = AssetDatabase.LoadAssetAtPath<FavoriteAssetsData>(SharedDataPath);
            if (dataAsset != null && dataAsset.items != null)
                foreach (var item in dataAsset.items)
                    folderColors[item.guid] = item.color;
        }

        public static void ShowWindow()
        {
            var window = GetWindow<FavoriteAssetsWindow>("Favorite Assets");
            window.minSize = new Vector2(420, 300);
            window.Show();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(JEEEditorStyles.InspectorTitleBar);
            GUILayout.Label(EditorGUIUtility.IconContent("d_FolderOpened Icon").image, GUILayout.Width(18),
                GUILayout.Height(18));
            GUILayout.Label("Favorite Assets", EditorStyles.boldLabel, GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            currentMode =
                (StorageMode)EditorGUILayout.EnumPopup(currentMode, EditorStyles.toolbarDropDown, GUILayout.Width(170));
            if (EditorGUI.EndChangeCheck()) LoadData();
            GUILayout.Space(4);
            using (new EditorGUI.DisabledScope(Selection.objects == null || Selection.objects.Length == 0))
            {
                if (GUILayout.Button(new GUIContent("Add Selected", "Add selected Project assets"),
                        EditorStyles.toolbarButton, GUILayout.Width(96)))
                    AddSelectedItems();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawSearchField()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(EditorGUIUtility.IconContent("Search Icon").image, GUILayout.Width(18),
                GUILayout.Height(18));
            var searchStyle = JEEEditorStyles.SearchFieldStyle();
            searchQuery = GUILayout.TextField(searchQuery, searchStyle, GUILayout.MinWidth(120),
                GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(searchQuery)))
            {
                if (GUILayout.Button(new GUIContent("Clear", "Clear search"), GUILayout.Width(52)))
                {
                    searchQuery = "";
                    GUI.FocusControl(null);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void HandleWindowDragAndDrop()
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;

            if (!HasValidDraggedAsset())
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var added = false;
                foreach (var path in DragAndDrop.paths)
                    if (IsValidAssetPath(path) && AddItem(path))
                        added = true;

                if (added)
                    Repaint();
            }

            evt.Use();
        }

        private void DrawFolderList()
        {
            if (favoriteItems.Count == 0)
            {
                JEEEditorStyles.DrawEmptyState(
                    "No favorite items yet",
                    "Drag project folders or assets into the drop area to pin them here.",
                    "Folder Icon");
                return;
            }

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);

            var indexToRemove = -1;
            var dataChanged = false;
            var visibleCount = 0;

            for (var i = 0; i < favoriteItems.Count; i++)
            {
                var item = favoriteItems[i];
                var path = AssetDatabase.GUIDToAssetPath(item.guid);

                if (string.IsNullOrEmpty(path))
                {
                    if (!string.IsNullOrEmpty(searchQuery))
                        continue;

                    visibleCount++;
                    var missingRowRect = EditorGUILayout.BeginHorizontal(JEEEditorStyles.ListRow, GUILayout.Height(RowHeight));
                    DrawRowDragFeedback(missingRowRect, i);
                    HandleReorderEvents(missingRowRect, i, ref dataChanged);
                    DrawCenteredIcon(EditorGUIUtility.IconContent("console.warnicon").image, 20);
                    DrawCenteredLabel("Missing Asset", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                    DrawCenteredLabel("Deleted", JEEEditorStyles.MutedLabel, GUILayout.Width(RowTypeWidth));

                    if (GUILayout.Button(
                            new GUIContent(EditorGUIUtility.IconContent("TreeEditor.Trash").image, "Remove"),
                            JEEEditorStyles.IconButton)) indexToRemove = i;
                    GUILayout.EndHorizontal();
                    continue;
                }

                if (!MatchesSearch(path))
                    continue;

                visibleCount++;
                var itemName = Path.GetFileName(path);
                var isFolder = AssetDatabase.IsValidFolder(path);
                var itemIcon = AssetDatabase.GetCachedIcon(path) ??
                               EditorGUIUtility.IconContent(isFolder ? "Folder Icon" : "DefaultAsset Icon").image;

                var rowRect = EditorGUILayout.BeginHorizontal(JEEEditorStyles.ListRow, GUILayout.Height(RowHeight));
                if (rowRect.Contains(Event.current.mousePosition))
                    hoveredPath = path;

                DrawRowDragFeedback(rowRect, i);
                HandleReorderEvents(rowRect, i, ref dataChanged);
                HandleDoubleClick(GetOpenableRowRect(rowRect), path);

                DrawCenteredIcon(itemIcon, 20);
                DrawCenteredLabel(new GUIContent(itemName, path), EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
                DrawCenteredLabel(GetItemTypeLabel(path, isFolder), JEEEditorStyles.MutedLabel,
                    GUILayout.Width(RowTypeWidth));

                using (new EditorGUILayout.HorizontalScope(GUILayout.Width(RowColorWidth)))
                {
                    EditorGUI.BeginChangeCheck();
                    var newColor = EditorGUILayout.ColorField(GUIContent.none, item.color, false, true, false,
                        GUILayout.Width(42), GUILayout.Height(20));
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newColor.a == 0) newColor.a = 1f;
                        item.color = newColor;
                        dataChanged = true;
                    }
                }

                using (new EditorGUILayout.HorizontalScope(GUILayout.Width(RowActionWidth)))
                {
                    if (GUILayout.Button(new GUIContent("Reveal", "Reveal in Project window"), GUILayout.Width(58),
                            GUILayout.Height(20)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                        if (obj != null) EditorGUIUtility.PingObject(obj);
                    }

                    if (GUILayout.Button(new GUIContent("Open", isFolder ? "Open folder" : "Open asset"),
                            GUILayout.Width(46), GUILayout.Height(20))) OpenItem(path);

                    if (GUILayout.Button(
                            new GUIContent(EditorGUIUtility.IconContent("TreeEditor.Trash").image, "Remove"),
                            JEEEditorStyles.IconButton)) indexToRemove = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            if (visibleCount == 0)
                JEEEditorStyles.DrawEmptyState(
                    "No items match the search",
                    "Try a different asset name or path.",
                    "Search Icon");

            if (indexToRemove >= 0)
            {
                favoriteItems.RemoveAt(indexToRemove);
                dataChanged = true;
            }

            if (dataChanged)
            {
                SaveData();
                LoadStaticData();
                EditorApplication.RepaintProjectWindow();
            }
        }

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var text = !string.IsNullOrEmpty(hoveredPath)
                    ? hoveredPath
                    : draggedIndex >= 0 && draggedIndex < favoriteItems.Count
                        ? "Reordering favorite items"
                        : "Drop Project assets anywhere in this window. Double-click an item to open it.";

                GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            }
        }

        private static void DrawCenteredIcon(Texture icon, float width)
        {
            var rect = GUILayoutUtility.GetRect(width, RowHeight, GUILayout.Width(width), GUILayout.Height(RowHeight));
            if (Event.current.type == EventType.Repaint && icon != null)
            {
                var iconRect = new Rect(rect.x, rect.y + (rect.height - 20f) * 0.5f, 20f, 20f);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            }
        }

        private static void DrawCenteredLabel(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            DrawCenteredLabel(new GUIContent(text), style, options);
        }

        private static void DrawCenteredLabel(GUIContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            var rect = GUILayoutUtility.GetRect(content, style, options);
            rect.height = RowHeight;
            GUI.Label(rect, content, style);
        }

        private void HandleReorderEvents(Rect rowRect, int index, ref bool dataChanged)
        {
            if (!string.IsNullOrWhiteSpace(searchQuery))
                return;

            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && rowRect.Contains(evt.mousePosition))
            {
                draggedIndex = index;
                isReordering = false;
            }
            else if (evt.type == EventType.MouseDrag && draggedIndex >= 0 && rowRect.Contains(evt.mousePosition))
            {
                isReordering = true;
                if (draggedIndex != index)
                {
                    var item = favoriteItems[draggedIndex];
                    favoriteItems.RemoveAt(draggedIndex);
                    favoriteItems.Insert(index, item);
                    draggedIndex = index;
                    dataChanged = true;
                    Repaint();
                }

                evt.Use();
            }
            else if (evt.type == EventType.MouseUp)
            {
                draggedIndex = -1;
                isReordering = false;
            }
        }

        private void DrawRowDragFeedback(Rect rowRect, int index)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (draggedIndex == index && isReordering)
                EditorGUI.DrawRect(rowRect, EditorGUIUtility.isProSkin
                    ? new Color(0.28f, 0.40f, 0.58f, 0.28f)
                    : new Color(0.35f, 0.55f, 0.85f, 0.22f));
            else if (draggedIndex >= 0 && rowRect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, 2f), EditorGUIUtility.isProSkin
                    ? new Color(0.58f, 0.72f, 0.95f, 0.75f)
                    : new Color(0.22f, 0.44f, 0.78f, 0.70f));
        }

        private void HandleDoubleClick(Rect rowRect, string path)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0 || evt.clickCount != 2)
                return;

            if (!rowRect.Contains(evt.mousePosition))
                return;

            OpenItem(path);
            evt.Use();
        }

        private static Rect GetOpenableRowRect(Rect rowRect)
        {
            const float trailingPadding = 12f;
            var reservedWidth = RowColorWidth + RowActionWidth + trailingPadding;
            return new Rect(rowRect.x, rowRect.y, Mathf.Max(0f, rowRect.width - reservedWidth), rowRect.height);
        }

        private bool MatchesSearch(string path)
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
                return true;

            var folderName = Path.GetFileName(path);
            return path.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   folderName.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetItemTypeLabel(string path, bool isFolder)
        {
            if (isFolder)
                return "Folder";

            var extension = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(extension))
                return extension.TrimStart('.').ToUpperInvariant();

            return "Asset";
        }

        private static Color GetDefaultColorForPath(string path)
        {
            var fileName = string.IsNullOrEmpty(path) ? "Favorite Asset" : Path.GetFileNameWithoutExtension(path);
            unchecked
            {
                var hash = 23;
                for (var i = 0; i < fileName.Length; i++)
                    hash = hash * 31 + fileName[i];

                var hue = (Mathf.Abs(hash) % 360) / 360f;
                var color = Color.HSVToRGB(hue, 0.62f, EditorGUIUtility.isProSkin ? 0.92f : 0.82f);
                color.a = 1f;
                return color;
            }
        }

        private static bool IsValidAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
        }

        private static bool HasValidDraggedAsset()
        {
            foreach (var path in DragAndDrop.paths)
                if (IsValidAssetPath(path))
                    return true;

            return false;
        }

        private static string ShortenPath(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
                return path;

            return "..." + path.Substring(path.Length - maxLength + 3);
        }

        private static void OpenItem(string path)
        {
            var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (obj == null)
                return;

            Selection.activeObject = obj;
            AssetDatabase.OpenAsset(obj);
        }

        private void AddSelectedItems()
        {
            var added = false;
            foreach (var obj in Selection.objects)
            {
                if (obj == null)
                    continue;

                var path = AssetDatabase.GetAssetPath(obj);
                if (AddItem(path))
                    added = true;
            }

            if (added)
                Repaint();
        }

        private bool AddItem(string path)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
            {
                var exists = false;
                foreach (var item in favoriteItems)
                    if (item.guid == guid)
                    {
                        exists = true;
                        break;
                    }

                if (!exists)
                {
                    favoriteItems.Add(new FavoriteAssetItem { guid = guid, color = GetDefaultColorForPath(path) });
                    SaveData();
                    LoadStaticData();
                    EditorApplication.RepaintProjectWindow();
                    return true;
                }
            }

            return false;
        }

        private void LoadData()
        {
            favoriteItems.Clear();

            if (currentMode == StorageMode.Personal_EditorPrefs)
            {
                var json = EditorPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json))
                    try
                    {
                        var data = JsonUtility.FromJson<PersonalData>(json);
                        if (data != null)
                        {
                            if (data.items != null && data.items.Count > 0)
                            {
                                favoriteItems = data.items;
                            }
                            else if (data.guids != null && data.guids.Count > 0)
                            {
                                // Migration from old list format
                                foreach (var g in data.guids)
                                    favoriteItems.Add(
                                        new FavoriteAssetItem
                                        {
                                            guid = g,
                                            color = GetDefaultColorForPath(AssetDatabase.GUIDToAssetPath(g))
                                        });
                                SaveData(); // Upgrade format
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
            }
            else
            {
                var dataAsset = AssetDatabase.LoadAssetAtPath<FavoriteAssetsData>(SharedDataPath);
                if (dataAsset != null && dataAsset.items != null)
                    // 深いコピーでUI用のリストを作成
                    foreach (var item in dataAsset.items)
                        favoriteItems.Add(new FavoriteAssetItem { guid = item.guid, color = item.color });
            }

            LoadStaticData();
        }

        private void SaveData()
        {
            if (currentMode == StorageMode.Personal_EditorPrefs)
            {
                var data = new PersonalData { items = favoriteItems };
                var json = JsonUtility.ToJson(data);
                EditorPrefs.SetString(PrefsKey, json);
            }
            else
            {
                var dataAsset = AssetDatabase.LoadAssetAtPath<FavoriteAssetsData>(SharedDataPath);
                if (dataAsset == null)
                {
                    if (!AssetDatabase.IsValidFolder(SharedDataDir))
                    {
                        Directory.CreateDirectory(SharedDataDir);
                        AssetDatabase.Refresh();
                    }

                    dataAsset = CreateInstance<FavoriteAssetsData>();
                    AssetDatabase.CreateAsset(dataAsset, SharedDataPath);
                }

                // 深いコピーで保存
                dataAsset.items.Clear();
                foreach (var item in favoriteItems)
                    dataAsset.items.Add(new FavoriteAssetItem { guid = item.guid, color = item.color });

                EditorUtility.SetDirty(dataAsset);
                AssetDatabase.SaveAssets();
            }
        }

        private enum StorageMode
        {
            Personal_EditorPrefs,
            Shared_ScriptableObject
        }

        [Serializable]
        private class PersonalData
        {
            // 旧バージョンのデータ移行用
            public List<string> guids;

            public List<FavoriteAssetItem> items = new();
        }
    }
}
