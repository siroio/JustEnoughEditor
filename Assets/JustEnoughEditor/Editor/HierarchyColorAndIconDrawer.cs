using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    /// <summary>
    /// Hierarchy ウィンドウの各行に背景色・コンポーネントアイコンを描画する Editor 拡張クラス。
    /// <see cref="EditorApplication.hierarchyWindowItemOnGUI"/> にフックして動作する。
    /// </summary>
    [InitializeOnLoad]
    public static class HierarchyColorAndIconDrawer
    {
        /// <summary>階層の深さに応じた背景色パレット（最大32階層）。</summary>
        private static readonly Color[] s_depthColors = new Color[32];

        /// <summary>背景グラデーション描画用のフェードテクスチャ（遅延初期化）。</summary>
        private static Texture2D s_fadeTexture;

        /// <summary>現在マウスがホバーしている行の instanceID。</summary>
        private static int s_hoveredInstanceID;

        /// <summary>コンポーネント型ごとのアイコンテクスチャキャッシュ。毎フレームの ObjectContent 呼び出しを避ける。</summary>
        private static readonly Dictionary<Type, Texture> s_iconCache = new Dictionary<Type, Texture>();

        /// <summary>アイコン描画時に再利用する GUIContent インスタンス。毎フレームの new を避ける。</summary>
        private static GUIContent s_reusableContent = new GUIContent();

        /// <summary>1行に表示するコンポーネントアイコンの最大数。</summary>
        private const int k_MaxIconCount = 4;

        /// <summary>カスタムアイコン GUID を EditorPrefs に保存する際のキープレフィックス。</summary>
        private const string k_CustomIconKeyPrefix = "JEE_CustomIcon_";

        /// <summary>ObjectPicker を識別するコントロール ID。</summary>
        private static int s_objectPickerControlID = -1;

        /// <summary>カスタムアイコン割り当て待ちの GameObject instanceID。</summary>
        private static int s_pendingCustomIconInstanceID = -1;

        /// <summary>
        /// アイコン表示から除外する Unity 組み込みコンポーネントの型セット。
        /// ユーザー定義スクリプトを優先表示するために使用する。
        /// </summary>
        private static readonly HashSet<Type> s_excludedComponentTypes = new HashSet<Type>
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
            typeof(NavMeshObstacle),
        };

        /// <summary>instanceID から UnityEngine.Object を取得するリフレクション経由のデリゲート。</summary>
        private static Func<int, UnityEngine.Object> s_getInstanceIDToObject;

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

            for (int i = 0; i < 32; i++)
            {
                float hue = (0.6f + i * 0.15f) % 1.0f;
                Color color = Color.HSVToRGB(hue, 0.6f, 0.9f);
                color.a = 0.08f;
                s_depthColors[i] = color;
            }

            var method = typeof(EditorUtility).GetMethod(
                "InstanceIDToObject",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null,
                new Type[] { typeof(int) },
                null);

            if (method != null)
            {
                s_getInstanceIDToObject = (Func<int, UnityEngine.Object>)Delegate.CreateDelegate(
                    typeof(Func<int, UnityEngine.Object>), method);
            }

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
        /// キャッシュミス時は <see cref="EditorGUIUtility.ObjectContent"/> で取得してキャッシュに追加する。
        /// </summary>
        private static Texture GetCachedIcon(Type type)
        {
            if (s_iconCache.TryGetValue(type, out Texture cached))
                return cached;

            Texture icon = EditorGUIUtility.ObjectContent(null, type).image;
            s_iconCache[type] = icon;
            return icon;
        }

        /// <summary>
        /// Hierarchy ウィンドウの各行に対して呼ばれるメインの描画コールバック。
        /// </summary>
        private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            if (!JEEMenu.IsHierarchyEnabled) return;
            if (s_getInstanceIDToObject == null) return;
            if (s_getInstanceIDToObject(instanceID) is not GameObject obj) return;

            int id = obj.GetInstanceID();

            // ObjectPicker でテクスチャが選択されたときにカスタムアイコンの GUID を保存する
            if (Event.current.commandName == "ObjectSelectorUpdated" &&
                EditorGUIUtility.GetObjectPickerControlID() == s_objectPickerControlID)
            {
                if (EditorGUIUtility.GetObjectPickerObject() is Texture2D selectedTexture &&
                    s_pendingCustomIconInstanceID != -1)
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selectedTexture));
                    if (!string.IsNullOrEmpty(guid))
                    {
                        EditorPrefs.SetString(k_CustomIconKeyPrefix + s_pendingCustomIconInstanceID, guid);
                        EditorApplication.RepaintHierarchyWindow();
                    }
                }
            }
            if (Event.current.commandName == "ObjectSelectorClosed" &&
                EditorGUIUtility.GetObjectPickerControlID() == s_objectPickerControlID)
            {
                s_objectPickerControlID = -1;
                s_pendingCustomIconInstanceID = -1;
            }

            // Repaint 時の座標ズレを避けるため、MouseMove/MouseDrag でホバー行を正確にトラッキングする
            if (Event.current.type == EventType.MouseLeaveWindow)
            {
                s_hoveredInstanceID = 0;
            }
            else if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDrag)
            {
                Rect rowRect = new Rect(0, selectionRect.y, selectionRect.width + 1000, selectionRect.height);
                bool isMouseOverHierarchy = EditorWindow.mouseOverWindow != null &&
                    EditorWindow.mouseOverWindow.GetType().Name == "SceneHierarchyWindow";
                if (isMouseOverHierarchy && rowRect.Contains(Event.current.mousePosition))
                    s_hoveredInstanceID = id;
                else if (s_hoveredInstanceID == id)
                    s_hoveredInstanceID = 0;
            }

            // "--- Section ---" や "[Group]" 形式の名前をフォルダ行として扱う
            bool isFolder = obj.name.StartsWith("---") || (obj.name.StartsWith("[") && obj.name.EndsWith("]"));

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

            bool isSelected = Selection.Contains(obj);
            bool isHovered = s_hoveredInstanceID == obj.GetInstanceID();

            Color bgColor;
            if (isSelected)
                bgColor = EditorGUIUtility.isProSkin ? new Color32(44, 93, 135, 255) : new Color32(58, 114, 176, 255);
            else if (isHovered)
                bgColor = EditorGUIUtility.isProSkin ? new Color32(69, 69, 69, 255) : new Color32(200, 200, 200, 255);
            else
                bgColor = EditorGUIUtility.isProSkin ? new Color32(45, 45, 45, 255) : new Color32(190, 190, 190, 255);

            EditorGUI.DrawRect(new Rect(selectionRect.x, selectionRect.y, selectionRect.width + 50, selectionRect.height), bgColor);

            Texture folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
            if (folderIcon != null)
                GUI.DrawTexture(new Rect(selectionRect.x, selectionRect.y, 16, 16), folderIcon);

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            Color textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.1f, 0.1f, 0.1f);
            if (isSelected) textColor = Color.white;
            if (!obj.activeInHierarchy) textColor.a = 0.5f;
            style.normal.textColor = textColor;
            GUI.Label(new Rect(selectionRect.x + 18, selectionRect.y, selectionRect.width - 18, selectionRect.height), obj.name, style);

            int depth = GetDepth(obj.transform);
            Color depthColor = s_depthColors[Mathf.Min(depth, s_depthColors.Length - 1)];
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

            int depth = GetDepth(obj.transform);
            Color bgColor = s_depthColors[Mathf.Min(depth, s_depthColors.Length - 1)];

            if (bgColor.a <= 0f) return;

            bool isSelected = Selection.Contains(obj);
            bool isHovered = s_hoveredInstanceID == obj.GetInstanceID();

            if (!isSelected && !isHovered)
            {
                EnsureFadeTexture();
                float startX = selectionRect.x - 4;
                float width = selectionRect.x + selectionRect.width - startX;
                Color oldColor = GUI.color;
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
            Component[] allComponents = obj.GetComponents<Component>();
            List<Component> priorityComponents = FilterPriorityComponents(allComponents);

            Color savedColor = GUI.color;
            if (!obj.activeInHierarchy)
                GUI.color = new Color(savedColor.r, savedColor.g, savedColor.b, 0.4f);

            int missingScriptCount = 0;
            foreach (var c in allComponents)
            {
                if (c == null) missingScriptCount++;
            }

            if (priorityComponents.Count == 0 && missingScriptCount == 0)
            {
                GUI.color = savedColor;
                return;
            }

            // 表示しきれなかったコンポーネント数を "+N" ラベルで示す
            int totalVisible = 0;
            foreach (var c in allComponents)
            {
                if (c == null || c is Transform) continue;
                totalVisible++;
            }
            int overflowCount = Mathf.Max(0, totalVisible - priorityComponents.Count);

            float iconSize = 16f;
            float overflowLabelWidth = overflowCount > 0 ? 24f : 0f;
            float currentX = selectionRect.x + selectionRect.width - iconSize - overflowLabelWidth;

            // Missing Script スロットごとに警告アイコンを描画する
            if (missingScriptCount > 0)
            {
                Texture warnIcon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                if (warnIcon != null)
                {
                    for (int i = 0; i < missingScriptCount; i++)
                    {
                        Rect iconRect = new Rect(currentX, selectionRect.y, iconSize, iconSize);
                        if (iconRect.x < selectionRect.x + JEEMenu.OverlapGuard) break;

                        s_reusableContent.image = warnIcon;
                        s_reusableContent.tooltip = "Missing Script";
                        GUI.Label(iconRect, s_reusableContent, GUIStyle.none);
                        currentX -= iconSize + 2f;
                    }
                }
            }

            // 優先コンポーネントのアイコンを右端から順に描画する
            foreach (var comp in priorityComponents)
            {
                Texture image = GetCachedIcon(comp.GetType());
                if (image == null) continue;

                Rect iconRect = new Rect(currentX, selectionRect.y, iconSize, iconSize);
                if (iconRect.x < selectionRect.x + JEEMenu.OverlapGuard) break;

                s_reusableContent.image = image;
                s_reusableContent.tooltip = comp.GetType().FullName;
                GUI.Label(iconRect, s_reusableContent, GUIStyle.none);
                currentX -= iconSize + 2f;
            }

            // カスタムアイコンをアイコン列の最左端に描画する
            string customIconKey = k_CustomIconKeyPrefix + obj.GetInstanceID();
            string customIconGuid = EditorPrefs.GetString(customIconKey, "");
            if (!string.IsNullOrEmpty(customIconGuid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(customIconGuid);
                Texture2D customTexture = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

                if (customTexture != null)
                {
                    Rect iconRect = new Rect(currentX, selectionRect.y, iconSize, iconSize);
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
                float labelX = selectionRect.x + selectionRect.width - overflowLabelWidth;
                GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
                };
                GUI.Label(new Rect(labelX, selectionRect.y, overflowLabelWidth, selectionRect.height),
                    $"+{overflowCount}", labelStyle);
            }

            GUI.color = savedColor;
        }

        /// <summary>
        /// コンポーネント配列から表示優先度の高いものを最大 <see cref="k_MaxIconCount"/> 個返す。
        /// ユーザー定義 MonoBehaviour を優先し、除外リスト外の組み込みコンポーネントで補完する。
        /// アイコンフィルターで非表示に設定された型はスキップする。
        /// </summary>
        private static List<Component> FilterPriorityComponents(Component[] components)
        {
            var userScripts = new List<Component>();
            var otherComponents = new List<Component>();

            foreach (var comp in components)
            {
                if (comp == null) continue;
                if (comp is Transform) continue;

                Type type = comp.GetType();

                if (!EditorPrefs.GetBool($"JEE_IconFilter_{type.FullName}", true)) continue;

                bool isUserScript = comp is MonoBehaviour
                    && !s_excludedComponentTypes.Contains(type)
                    && (type.Namespace == null || !type.Namespace.StartsWith("UnityEngine"));

                if (isUserScript)
                    userScripts.Add(comp);
                else if (!s_excludedComponentTypes.Contains(type))
                    otherComponents.Add(comp);
            }

            var result = new List<Component>(k_MaxIconCount);
            result.AddRange(userScripts);

            foreach (var comp in otherComponents)
            {
                if (result.Count >= k_MaxIconCount) break;
                result.Add(comp);
            }

            if (result.Count > k_MaxIconCount)
                result.RemoveRange(k_MaxIconCount, result.Count - k_MaxIconCount);

            return result;
        }

        /// <summary>
        /// 選択中の GameObject にカスタムアイコンを割り当てる ObjectPicker を開く。
        /// </summary>
        [MenuItem("JEE/Assign Custom Icon...", false, 70)]
        private static void AssignCustomIcon()
        {
            if (Selection.activeGameObject == null) return;
            s_pendingCustomIconInstanceID = Selection.activeGameObject.GetInstanceID();
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
            if (Selection.activeGameObject == null) return;
            EditorPrefs.DeleteKey(k_CustomIconKeyPrefix + Selection.activeGameObject.GetInstanceID());
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("JEE/Remove Custom Icon", true)]
        private static bool ValidateRemoveCustomIcon()
        {
            if (Selection.activeGameObject == null) return false;
            return EditorPrefs.HasKey(k_CustomIconKeyPrefix + Selection.activeGameObject.GetInstanceID());
        }

        /// <summary>
        /// Transform の親を辿って階層の深さを返す。
        /// </summary>
        private static int GetDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }
    }
}
