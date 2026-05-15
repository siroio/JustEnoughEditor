using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    /// <summary>
    /// Hierarchy ウィンドウの各行に背景色・コンポーネントアイコンを描画する Editor 拡張クラス。
    /// <see cref="EditorApplication.hierarchyWindowItemOnGUI" /> にフックして動作する。
    /// </summary>
    [InitializeOnLoad]
    public static class HierarchyColorAndIconDrawer
    {
        /// <summary>カスタムアイコン GUID を EditorPrefs に保存する際のキープレフィックス。</summary>
        private const string k_CustomIconKeyPrefix = "JEE_CustomIcon_";

        /// <summary>階層の深さに応じた背景色パレット（最大32階層）。</summary>
        private static readonly Color[] s_depthColors = new Color[32];

        /// <summary>背景グラデーション描画用のフェードテクスチャ（遅延初期化）。</summary>
        private static Texture2D s_fadeTexture;

        /// <summary>現在マウスがホバーしている行の instanceID。</summary>
        private static int s_hoveredInstanceID;

        /// <summary>コンポーネント型ごとのアイコンテクスチャキャッシュ。毎フレームの ObjectContent 呼び出しを避ける。</summary>
        private static readonly Dictionary<Type, Texture> s_iconCache = new();

        /// <summary>アイコン描画時に再利用する GUIContent インスタンス。毎フレームの new を避ける。</summary>
        private static GUIContent s_reusableContent = new();

        /// <summary>ObjectPicker を識別するコントロール ID。</summary>
        private static int s_objectPickerControlID = -1;

        /// <summary>カスタムアイコン割り当て待ちの保存キー。</summary>
        private static string s_pendingCustomIconKey = "";

        /// <summary>
        /// アイコン表示から除外する Unity 組み込みコンポーネントの型セット。
        /// ユーザー定義スクリプトを優先表示するために使用する。
        /// </summary>
        private static readonly HashSet<Type> s_excludedComponentTypes = new()
        {
            typeof(Transform),
            typeof(RectTransform),
            typeof(MeshRenderer),
            typeof(MeshFilter),
            typeof(SkinnedMeshRenderer),
            typeof(MeshCollider),
            typeof(BoxCollider),
            typeof(SphereCollider),
            typeof(CapsuleCollider),
            typeof(CharacterController),
            typeof(Rigidbody),
            typeof(AudioSource),
            typeof(AudioListener),
            typeof(Light),
            typeof(Camera),
            typeof(Canvas),
            typeof(CanvasRenderer),
            typeof(CanvasGroup),
            typeof(RectMask2D),
            typeof(Animator),
            typeof(Animation),
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(LineRenderer),
            typeof(TrailRenderer),
            typeof(LODGroup),
            typeof(ReflectionProbe),
            typeof(LightProbeGroup),
            typeof(NavMeshAgent),
            typeof(NavMeshObstacle)
        };

        /// <summary>instanceID から UnityEngine.Object を取得するリフレクション経由のデリゲート。</summary>
        private static Func<int, Object> s_getInstanceIDToObject;

        static HierarchyColorAndIconDrawer()
        {
            Initialize();
        }

        /// <summary>
        /// ドメインリロード時に呼ばれる初期化処理。
        /// キャッシュのクリア・カラーパレット生成・イベント登録を行う。
        /// </summary>
        private static void Initialize()
        {
            s_hoveredInstanceID = 0;
            s_iconCache.Clear();
            s_reusableContent = new GUIContent();

            for (var i = 0; i < 32; i++)
            {
                var hue = (0.6f + i * 0.15f) % 1.0f;
                var color = Color.HSVToRGB(hue, 0.6f, 0.9f);
                color.a = 0.08f;
                s_depthColors[i] = color;
            }

            var method = typeof(EditorUtility).GetMethod(
                "InstanceIDToObject",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);

            if (method != null)
                s_getInstanceIDToObject = (Func<int, Object>)Delegate.CreateDelegate(
                    typeof(Func<int, Object>), method);

            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;

            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        /// <summary>
        /// Hierarchy の変更（アクティブ/非アクティブ切り替えなど）を検知して再描画をトリガーする。
        /// </summary>
        private static void OnHierarchyChanged()
        {
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>
        /// 背景グラデーション用テクスチャを遅延初期化する。
        /// </summary>
        private static void EnsureFadeTexture()
        {
            if (s_fadeTexture != null) return;

            s_fadeTexture = new Texture2D(2, 1);
            s_fadeTexture.SetPixel(0, 0, new Color(1f, 1f, 1f, 1f));
            s_fadeTexture.SetPixel(1, 0, new Color(1f, 1f, 1f, 0f));
            s_fadeTexture.wrapMode = TextureWrapMode.Clamp;
            s_fadeTexture.hideFlags = HideFlags.HideAndDontSave;
            s_fadeTexture.Apply();
        }

        /// <summary>
        /// 指定した型のアイコンテクスチャをキャッシュから返す。
        /// キャッシュミス時は <see cref="EditorGUIUtility.ObjectContent" /> で取得してキャッシュに追加する。
        /// </summary>
        private static Texture GetCachedIcon(Type type)
        {
            if (s_iconCache.TryGetValue(type, out var cached))
                return cached;

            var icon = EditorGUIUtility.ObjectContent(null, type).image;
            s_iconCache[type] = icon;
            return icon;
        }

        /// <summary>
        /// Hierarchy ウィンドウの各行に対して呼ばれるメインの描画コールバック。
        /// </summary>
        private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            if (s_getInstanceIDToObject == null) return;
            if (s_getInstanceIDToObject(instanceID) is not GameObject obj) return;

            var id = obj.GetInstanceID();

            // ObjectPicker でテクスチャが選択されたときにカスタムアイコンの GUID を保存する
            if (Event.current.commandName == "ObjectSelectorUpdated" &&
                EditorGUIUtility.GetObjectPickerControlID() == s_objectPickerControlID)
                if (EditorGUIUtility.GetObjectPickerObject() is Texture2D selectedTexture &&
                    !string.IsNullOrEmpty(s_pendingCustomIconKey))
                {
                    var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedTexture));
                    if (!string.IsNullOrEmpty(guid))
                    {
                        EditorPrefs.SetString(s_pendingCustomIconKey, guid);
                        EditorApplication.RepaintHierarchyWindow();
                    }
                }

            if (Event.current.commandName == "ObjectSelectorClosed" &&
                EditorGUIUtility.GetObjectPickerControlID() == s_objectPickerControlID)
            {
                s_objectPickerControlID = -1;
                s_pendingCustomIconKey = "";
            }

            if (!JEEMenu.IsHierarchyEnabled) return;

            // Repaint 時の座標ズレを避けるため、MouseMove/MouseDrag でホバー行を正確にトラッキングする
            if (Event.current.type == EventType.MouseLeaveWindow)
            {
                s_hoveredInstanceID = 0;
            }
            else if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDrag)
            {
                var rowRect = new Rect(0, selectionRect.y, selectionRect.width + 1000, selectionRect.height);
                var isMouseOverHierarchy = EditorWindow.mouseOverWindow != null &&
                                           EditorWindow.mouseOverWindow.GetType().Name == "SceneHierarchyWindow";
                if (isMouseOverHierarchy && rowRect.Contains(Event.current.mousePosition))
                    s_hoveredInstanceID = id;
                else if (s_hoveredInstanceID == id)
                    s_hoveredInstanceID = 0;
            }

            // "--- Section ---" や "[Group]" 形式の名前をフォルダ行として扱う
            var isFolder = obj.name.StartsWith("---") || (obj.name.StartsWith("[") && obj.name.EndsWith("]"));

            if (isFolder)
            {
                DrawFolderStyle(obj, selectionRect);
            }
            else
            {
                if (JEEMenu.IsHierarchyColorEnabled)
                    DrawBackgroundAndDepthBar(obj, selectionRect);
            }

            if (JEEMenu.IsHierarchyIconEnabled)
                DrawComponentIcons(obj, selectionRect);
        }

        /// <summary>
        /// フォルダ行（セクションヘッダー）のスタイルを描画する。
        /// 背景色・フォルダアイコン・太字テキスト・左端カラーバーを含む。
        /// </summary>
        private static void DrawFolderStyle(GameObject obj, Rect selectionRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            var isSelected = Selection.Contains(obj);
            var isHovered = s_hoveredInstanceID == obj.GetInstanceID();

            Color bgColor;
            if (isSelected)
                bgColor = EditorGUIUtility.isProSkin ? new Color32(44, 93, 135, 255) : new Color32(58, 114, 176, 255);
            else if (isHovered)
                bgColor = EditorGUIUtility.isProSkin ? new Color32(69, 69, 69, 255) : new Color32(200, 200, 200, 255);
            else
                bgColor = EditorGUIUtility.isProSkin ? new Color32(45, 45, 45, 255) : new Color32(190, 190, 190, 255);

            EditorGUI.DrawRect(
                new Rect(selectionRect.x, selectionRect.y, selectionRect.width + 50, selectionRect.height), bgColor);

            var folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
            if (folderIcon != null)
                GUI.DrawTexture(new Rect(selectionRect.x, selectionRect.y, 16, 16), folderIcon);

            var style = new GUIStyle(EditorStyles.boldLabel);
            var textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.1f, 0.1f, 0.1f);
            if (isSelected) textColor = Color.white;
            if (!obj.activeInHierarchy) textColor.a = 0.5f;
            style.normal.textColor = textColor;
            GUI.Label(new Rect(selectionRect.x + 18, selectionRect.y, selectionRect.width - 18, selectionRect.height),
                obj.name, style);

            var depth = GetDepth(obj.transform);
            var depthColor = s_depthColors[Mathf.Min(depth, s_depthColors.Length - 1)];
            EditorGUI.DrawRect(new Rect(selectionRect.x - 4, selectionRect.y, 2, selectionRect.height),
                new Color(depthColor.r, depthColor.g, depthColor.b, 1f));
        }

        /// <summary>
        /// 通常行の背景グラデーションと左端の階層カラーバーを描画する。
        /// 選択中・ホバー中の行は Unity 標準のハイライトを優先するため背景を塗らない。
        /// </summary>
        private static void DrawBackgroundAndDepthBar(GameObject obj, Rect selectionRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            var depth = GetDepth(obj.transform);
            var bgColor = s_depthColors[Mathf.Min(depth, s_depthColors.Length - 1)];

            if (bgColor.a <= 0f) return;

            var isSelected = Selection.Contains(obj);
            var isHovered = s_hoveredInstanceID == obj.GetInstanceID();

            if (!isSelected && !isHovered)
            {
                EnsureFadeTexture();
                var startX = selectionRect.x - 4;
                var width = selectionRect.x + selectionRect.width - startX;
                var oldColor = GUI.color;
                GUI.color = bgColor;
                GUI.DrawTexture(new Rect(startX, selectionRect.y, width, selectionRect.height), s_fadeTexture);
                GUI.color = oldColor;
            }

            EditorGUI.DrawRect(new Rect(selectionRect.x - 4, selectionRect.y, 2, selectionRect.height),
                new Color(bgColor.r, bgColor.g, bgColor.b, 1f));
        }

        /// <summary>
        /// 行の右端からコンポーネントアイコンを描画する。
        /// 表示順は右から「+N ラベル → 通常アイコン → Missing Script 警告 → カスタムアイコン（最左）」。
        /// 非アクティブオブジェクトのアイコンは 40% の不透明度で描画する。
        /// </summary>
        private static void DrawComponentIcons(GameObject obj, Rect selectionRect)
        {
            var allComponents = obj.GetComponents<Component>();
            var priorityComponents = FilterPriorityComponents(allComponents);

            var savedColor = GUI.color;
            if (!obj.activeInHierarchy)
                GUI.color = new Color(savedColor.r, savedColor.g, savedColor.b, 0.4f);

            var missingScriptCount = 0;
            foreach (var c in allComponents)
                if (c == null)
                    missingScriptCount++;

            if (priorityComponents.Count == 0 && missingScriptCount == 0)
            {
                GUI.color = savedColor;
                return;
            }

            // 表示しきれなかったコンポーネント数を "+N" ラベルで示す
            var totalVisible = 0;
            foreach (var c in allComponents)
            {
                if (c == null || c is Transform) continue;
                totalVisible++;
            }

            var overflowCount = Mathf.Max(0, totalVisible - priorityComponents.Count);

            var iconSize = 16f;
            var overflowLabelWidth = overflowCount > 0 ? 24f : 0f;
            var currentX = selectionRect.x + selectionRect.width - iconSize - overflowLabelWidth;

            // Missing Script スロットごとに警告アイコンを描画する
            if (missingScriptCount > 0)
            {
                var warnIcon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                if (warnIcon != null)
                    for (var i = 0; i < missingScriptCount; i++)
                    {
                        var iconRect = new Rect(currentX, selectionRect.y, iconSize, iconSize);
                        if (iconRect.x < selectionRect.x + JEEMenu.OverlapGuard) break;

                        s_reusableContent.image = warnIcon;
                        s_reusableContent.tooltip = "Missing Script";
                        GUI.Label(iconRect, s_reusableContent, GUIStyle.none);
                        currentX -= iconSize + 2f;
                    }
            }

            // 優先コンポーネントのアイコンを右端から順に描画する
            foreach (var comp in priorityComponents)
            {
                var image = GetCachedIcon(comp.GetType());
                if (image == null) continue;

                var iconRect = new Rect(currentX, selectionRect.y, iconSize, iconSize);
                if (iconRect.x < selectionRect.x + JEEMenu.OverlapGuard) break;

                s_reusableContent.image = image;
                s_reusableContent.tooltip = comp.GetType().FullName;
                GUI.Label(iconRect, s_reusableContent, GUIStyle.none);
                currentX -= iconSize + 2f;
            }

            // カスタムアイコンをアイコン列の最左端に描画する
            var customIconKey = GetCustomIconKey(obj);
            var customIconGuid = GetCustomIconGuid(obj, customIconKey);
            if (!string.IsNullOrEmpty(customIconGuid))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(customIconGuid);
                var customTexture = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

                if (customTexture != null)
                {
                    var iconRect = new Rect(currentX, selectionRect.y, iconSize, iconSize);
                    if (iconRect.x >= selectionRect.x + JEEMenu.OverlapGuard)
                    {
                        s_reusableContent.image = customTexture;
                        s_reusableContent.tooltip = $"Custom Icon: {customTexture.name}";
                        GUI.Label(iconRect, s_reusableContent, GUIStyle.none);
                        currentX -= iconSize + 2f;
                    }
                }
                else
                {
                    // 参照先テクスチャが削除されていた場合はキーを破棄する
                    EditorPrefs.DeleteKey(customIconKey);
                }
            }

            if (overflowCount > 0)
            {
                var labelX = selectionRect.x + selectionRect.width - overflowLabelWidth;
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
                };
                GUI.Label(new Rect(labelX, selectionRect.y, overflowLabelWidth, selectionRect.height),
                    $"+{overflowCount}", labelStyle);
            }

            GUI.color = savedColor;
        }

        /// <summary>コンポーネント配列から表示優先度の高いものを設定数まで返す。</summary>
        private static List<Component> FilterPriorityComponents(Component[] components)
        {
            var userScripts = new List<Component>();
            var otherComponents = new List<Component>();
            var sceneOrderComponents = new List<Component>();

            foreach (var comp in components)
            {
                if (comp == null) continue;
                if (comp is Transform) continue;

                var type = comp.GetType();

                if (!EditorPrefs.GetBool($"JEE_IconFilter_{type.FullName}", true)) continue;

                var isUserScript = comp is MonoBehaviour
                                   && !s_excludedComponentTypes.Contains(type)
                                   && (type.Namespace == null || !type.Namespace.StartsWith("UnityEngine"));

                if (!s_excludedComponentTypes.Contains(type))
                    sceneOrderComponents.Add(comp);

                if (isUserScript)
                    userScripts.Add(comp);
                else if (!s_excludedComponentTypes.Contains(type))
                    otherComponents.Add(comp);
            }

            var maxIconCount = JEEPrefs.MaxIconCount;
            var result = new List<Component>(maxIconCount);
            switch (JEEPrefs.ComponentPriorityMode)
            {
                case JEEComponentPriorityMode.BuiltInFirst:
                    AddRangeUntilFull(result, otherComponents, maxIconCount);
                    AddRangeUntilFull(result, userScripts, maxIconCount);
                    break;
                case JEEComponentPriorityMode.SceneOrder:
                    AddRangeUntilFull(result, sceneOrderComponents, maxIconCount);
                    break;
                default:
                    AddRangeUntilFull(result, userScripts, maxIconCount);
                    AddRangeUntilFull(result, otherComponents, maxIconCount);
                    break;
            }

            return result;
        }

        private static void AddRangeUntilFull(List<Component> result, List<Component> source, int maxCount)
        {
            foreach (var comp in source)
            {
                if (result.Count >= maxCount) break;
                result.Add(comp);
            }
        }

        /// <summary>
        /// 選択中の GameObject にカスタムアイコンを割り当てる ObjectPicker を開く。
        /// </summary>
        [MenuItem("JEE/Assign Custom Icon...", false, 70)]
        private static void AssignCustomIcon()
        {
            AssignCustomIcon(Selection.activeGameObject);
        }

        public static void AssignCustomIcon(GameObject target)
        {
            if (target == null) return;
            s_pendingCustomIconKey = GetCustomIconKey(target);
            EditorGUIUtility.ShowObjectPicker<Texture2D>(null, false, "", 0);
            s_objectPickerControlID = EditorGUIUtility.GetObjectPickerControlID();
        }

        [MenuItem("JEE/Assign Custom Icon...", true)]
        private static bool ValidateAssignCustomIcon()
        {
            return Selection.activeGameObject != null;
        }

        /// <summary>
        /// 選択中の GameObject に割り当てられたカスタムアイコンを削除する。
        /// </summary>
        [MenuItem("JEE/Remove Custom Icon", false, 71)]
        private static void RemoveCustomIcon()
        {
            RemoveCustomIcon(Selection.activeGameObject);
        }

        public static void RemoveCustomIcon(GameObject target)
        {
            if (target == null) return;
            EditorPrefs.DeleteKey(GetCustomIconKey(target));
            EditorPrefs.DeleteKey(k_CustomIconKeyPrefix + target.GetInstanceID());
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("JEE/Remove Custom Icon", true)]
        private static bool ValidateRemoveCustomIcon()
        {
            if (Selection.activeGameObject == null) return false;
            return HasCustomIcon(Selection.activeGameObject);
        }

        public static bool HasCustomIcon(GameObject target)
        {
            if (target == null) return false;
            return EditorPrefs.HasKey(GetCustomIconKey(target)) ||
                   EditorPrefs.HasKey(k_CustomIconKeyPrefix + target.GetInstanceID());
        }

        /// <summary>
        /// シーン再読込後も維持される GlobalObjectId を使ってカスタムアイコン保存キーを作る。
        /// </summary>
        private static string GetCustomIconKey(GameObject obj)
        {
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            return k_CustomIconKeyPrefix + globalId;
        }

        /// <summary>
        /// 旧 instanceID キーの値があれば安定キーへ移行して返す。
        /// </summary>
        private static string GetCustomIconGuid(GameObject obj, string customIconKey)
        {
            var guid = EditorPrefs.GetString(customIconKey, "");
            if (!string.IsNullOrEmpty(guid))
                return guid;

            var legacyKey = k_CustomIconKeyPrefix + obj.GetInstanceID();
            guid = EditorPrefs.GetString(legacyKey, "");
            if (!string.IsNullOrEmpty(guid))
            {
                EditorPrefs.SetString(customIconKey, guid);
                EditorPrefs.DeleteKey(legacyKey);
            }

            return guid;
        }

        /// <summary>
        /// Transform の親を辿って階層の深さを返す。
        /// </summary>
        private static int GetDepth(Transform t)
        {
            var depth = 0;
            while (t.parent != null)
            {
                depth++;
                t = t.parent;
            }

            return depth;
        }
    }
}
