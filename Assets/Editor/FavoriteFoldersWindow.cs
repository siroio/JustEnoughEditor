using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    [InitializeOnLoad]
    public class FavoriteFoldersWindow : EditorWindow
    {
        private enum StorageMode
        {
            Personal_EditorPrefs,
            Shared_ScriptableObject
        }

        private StorageMode currentMode = StorageMode.Personal_EditorPrefs;
        private List<FavoriteFolderItem> favoriteItems = new List<FavoriteFolderItem>();
        private static Dictionary<string, Color> folderColors = new Dictionary<string, Color>();
        
        private const string PrefsKey = "JustEnoughEditor_FavoriteFolders";
        private const string SharedDataPath = "Assets/Editor/FavoriteFoldersData.asset";

        private Vector2 scrollPosition;


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

            if (!folderColors.TryGetValue(guid, out Color color)) return;
            if (color == new Color(0, 0, 0, 0)) return; // 色なし(デフォルト)の場合はスキップ

            if (selectionRect.height > 20)
            {
                // Grid View (アイコン表示)
                // アイコンの左側に垂直なカラーバーを描画
                Rect leftBarRect = new Rect(selectionRect.x + 4, selectionRect.y + 4, 4, selectionRect.width - 8);
                EditorGUI.DrawRect(leftBarRect, new Color(color.r, color.g, color.b, 1f));
            }
            else
            {
                // List View (リスト表示)
                // アイコンの左側にカラーバーを描画
                // selectionRect.x はテキストの開始位置、アイコンは x - 16 なので x - 20 に配置
                float barX = selectionRect.x - 20f;
                if (barX < 2) barX = 2; // 最左端のガード
                
                Rect leftBarRect = new Rect(barX, selectionRect.y + 2, 4, selectionRect.height - 4);
                EditorGUI.DrawRect(leftBarRect, new Color(color.r, color.g, color.b, 1f));
            }
        }

        private static void LoadStaticData()
        {
            // PersonalとSharedの両方の設定を読み込んで、ProjectWindowに色を反映させる
            folderColors.Clear();

            // Load Personal
            string json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JsonUtility.FromJson<PersonalData>(json);
                    if (data != null && data.items != null)
                    {
                        foreach (var item in data.items)
                        {
                            folderColors[item.guid] = item.color;
                        }
                    }
                }
                catch (Exception) { }
            }

            // Load Shared
            var dataAsset = AssetDatabase.LoadAssetAtPath<FavoriteFoldersData>(SharedDataPath);
            if (dataAsset != null && dataAsset.items != null)
            {
                foreach (var item in dataAsset.items)
                {
                    folderColors[item.guid] = item.color;
                }
            }
        }

        public static void ShowWindow()
        {
            var window = GetWindow<FavoriteFoldersWindow>("Favorite Folders");
            window.minSize = new Vector2(350, 200);
            window.Show();
        }

        private void OnEnable()
        {
            LoadData();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawDragAndDropArea();
            DrawFolderList();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            currentMode = (StorageMode)EditorGUILayout.EnumPopup(currentMode, EditorStyles.toolbarDropDown, GUILayout.Width(180));
            if (EditorGUI.EndChangeCheck())
            {
                LoadData();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawDragAndDropArea()
        {
            Rect dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drag & Drop Folders Here", EditorStyles.helpBox);

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropRect.Contains(evt.mousePosition))
                        break;

                    bool hasFolder = false;
                    foreach (var path in DragAndDrop.paths)
                    {
                        if (AssetDatabase.IsValidFolder(path))
                        {
                            hasFolder = true;
                            break;
                        }
                    }

                    if (!hasFolder) return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (var path in DragAndDrop.paths)
                        {
                            if (AssetDatabase.IsValidFolder(path))
                            {
                                AddFolder(path);
                            }
                        }
                        evt.Use();
                    }
                    break;
            }
        }

        private void DrawFolderList()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            int indexToRemove = -1;
            bool dataChanged = false;

            for (int i = 0; i < favoriteItems.Count; i++)
            {
                var item = favoriteItems[i];
                string path = AssetDatabase.GUIDToAssetPath(item.guid);

                if (string.IsNullOrEmpty(path))
                {
                    GUILayout.BeginHorizontal(EditorStyles.helpBox);
                    GUILayout.Label("Missing Folder (Deleted or Moved)", GUILayout.Height(20), GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("X", GUILayout.Width(24), GUILayout.Height(20)))
                    {
                        indexToRemove = i;
                    }
                    GUILayout.EndHorizontal();
                    continue;
                }

                string folderName = Path.GetFileName(path);
                Texture folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;

                GUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Icon and Name
                GUILayout.Label(new GUIContent(" " + folderName, folderIcon), GUILayout.Height(20), GUILayout.Width(130));
                
                // Full Path (Assets/...)
                GUIStyle pathStyle = new GUIStyle(EditorStyles.miniLabel);
                pathStyle.normal.textColor = Color.gray;
                GUILayout.Label(path, pathStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true));

                // Color Picker
                EditorGUI.BeginChangeCheck();
                Color newColor = EditorGUILayout.ColorField(GUIContent.none, item.color, false, true, false, GUILayout.Width(40), GUILayout.Height(20));
                if (EditorGUI.EndChangeCheck())
                {
                    // ユーザーが色を変えたのにAlphaが0のままだった場合、自動でAlphaを1にする
                    if (newColor.a == 0)
                    {
                        newColor.a = 1f;
                    }
                    item.color = newColor;
                    dataChanged = true;
                }

                // Clear Color button (Optional helper)
                if (GUILayout.Button("C", GUILayout.Width(20), GUILayout.Height(20)))
                {
                    item.color = new Color(0, 0, 0, 0);
                    dataChanged = true;
                }

                // Reveal button
                if (GUILayout.Button("Reveal", GUILayout.Width(60), GUILayout.Height(20)))
                {
                    UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (obj != null)
                    {
                        EditorGUIUtility.PingObject(obj);
                    }
                }

                // Open button
                if (GUILayout.Button("Open", GUILayout.Width(50), GUILayout.Height(20)))
                {
                    UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (obj != null)
                    {
                        Selection.activeObject = obj;
                        AssetDatabase.OpenAsset(obj);
                    }
                }

                // Remove button
                if (GUILayout.Button("X", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    indexToRemove = i;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

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

        private void AddFolder(string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
            {
                bool exists = false;
                foreach (var item in favoriteItems)
                {
                    if (item.guid == guid) { exists = true; break; }
                }

                if (!exists)
                {
                    favoriteItems.Add(new FavoriteFolderItem { guid = guid, color = new Color(0, 0, 0, 0) });
                    SaveData();
                    LoadStaticData();
                    EditorApplication.RepaintProjectWindow();
                }
            }
        }

        [Serializable]
        private class PersonalData
        {
            // 旧バージョンのデータ移行用
            public List<string> guids;
            
            public List<FavoriteFolderItem> items = new List<FavoriteFolderItem>();
        }

        private void LoadData()
        {
            favoriteItems.Clear();

            if (currentMode == StorageMode.Personal_EditorPrefs)
            {
                string json = EditorPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json))
                {
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
                                foreach (string g in data.guids)
                                {
                                    favoriteItems.Add(new FavoriteFolderItem { guid = g, color = new Color(0,0,0,0) });
                                }
                                SaveData(); // Upgrade format
                            }
                        }
                    }
                    catch (Exception) { }
                }
            }
            else
            {
                var dataAsset = AssetDatabase.LoadAssetAtPath<FavoriteFoldersData>(SharedDataPath);
                if (dataAsset != null && dataAsset.items != null)
                {
                    // 深いコピーでUI用のリストを作成
                    foreach (var item in dataAsset.items)
                    {
                        favoriteItems.Add(new FavoriteFolderItem { guid = item.guid, color = item.color });
                    }
                }
            }
            
            LoadStaticData();
        }

        private void SaveData()
        {
            if (currentMode == StorageMode.Personal_EditorPrefs)
            {
                var data = new PersonalData { items = favoriteItems };
                string json = JsonUtility.ToJson(data);
                EditorPrefs.SetString(PrefsKey, json);
            }
            else
            {
                var dataAsset = AssetDatabase.LoadAssetAtPath<FavoriteFoldersData>(SharedDataPath);
                if (dataAsset == null)
                {
                    dataAsset = ScriptableObject.CreateInstance<FavoriteFoldersData>();
                    AssetDatabase.CreateAsset(dataAsset, SharedDataPath);
                }
                
                // 深いコピーで保存
                dataAsset.items.Clear();
                foreach (var item in favoriteItems)
                {
                    dataAsset.items.Add(new FavoriteFolderItem { guid = item.guid, color = item.color });
                }
                
                EditorUtility.SetDirty(dataAsset);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
