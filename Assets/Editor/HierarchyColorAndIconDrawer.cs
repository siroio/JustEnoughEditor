using System;
using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    [InitializeOnLoad]
    public static class HierarchyColorAndIconDrawer
    {
        // 階層の深さに応じた背景色 (最大32階層)
        private static readonly Color[] DepthColors = new Color[32];
        private static Texture2D fadeTexture;
        private static int hoveredInstanceID = 0;

        private static void EnsureFadeTexture()
        {
            if (fadeTexture == null)
            {
                fadeTexture = new Texture2D(2, 1);
                fadeTexture.SetPixel(0, 0, new Color(1f, 1f, 1f, 1f));
                fadeTexture.SetPixel(1, 0, new Color(1f, 1f, 1f, 0f));
                fadeTexture.wrapMode = TextureWrapMode.Clamp;
                fadeTexture.hideFlags = HideFlags.HideAndDontSave;
                fadeTexture.Apply();
            }
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            hoveredInstanceID = 0;

            // 32階層分のカラーパレットをHSV空間を使って自動生成
            // 0階層目(Root)から色をつける
            for (int i = 0; i < 32; i++)
            {
                // 青スタート(Hue=0.6)で、隣り合う階層で色がはっきりと変わるように適度にずらす
                float hue = (0.6f + i * 0.15f) % 1.0f;
                Color color = Color.HSVToRGB(hue, 0.6f, 0.9f);
                // 右側でフェードアウトするため、開始の左側は少し濃いめ(0.12)に設定
                color.a = 0.08f;
                DepthColors[i] = color;
            }

            var method = typeof(EditorUtility).GetMethod("InstanceIDToObject",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, new Type[] { typeof(int) }, null);

            if (method != null)
            {
                GetInstanceIDToObject = (Func<int, UnityEngine.Object>)Delegate.CreateDelegate(typeof(Func<int, UnityEngine.Object>), method);
            }

            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        }

        private static Func<int, UnityEngine.Object> GetInstanceIDToObject;

        private static void OnHierarchyGUI(int instanceID, Rect selectionRect)
        {
            if (!JEEMenu.IsHierarchyEnabled) return;
            if (GetInstanceIDToObject == null) return;
            GameObject obj = GetInstanceIDToObject(instanceID) as GameObject;
            if (obj == null) return;

            int id = obj.GetInstanceID();

            // マウスのホバー状態を正確にトラッキングする (Repaint時の古い座標バグを回避)
            if (Event.current.type == EventType.MouseLeaveWindow)
            {
                hoveredInstanceID = 0;
            }
            else if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDrag)
            {
                Rect rowRect = new Rect(0, selectionRect.y, selectionRect.width + 1000, selectionRect.height);
                bool isMouseOverHierarchy = EditorWindow.mouseOverWindow != null && EditorWindow.mouseOverWindow.GetType().Name == "SceneHierarchyWindow";
                if (isMouseOverHierarchy && rowRect.Contains(Event.current.mousePosition))
                {
                    hoveredInstanceID = id;
                }
                else if (hoveredInstanceID == id)
                {
                    hoveredInstanceID = 0;
                }
            }

            // 名前によるフォルダ判定 (例: "--- UI ---" や "[Managers]" など)
            bool isFolder = obj.name.StartsWith("---") || (obj.name.StartsWith("[") && obj.name.EndsWith("]"));

            if (isFolder)
            {
                DrawFolderStyle(obj, selectionRect);
            }
            else
            {
                // 1. 階層ごとの色づけ
                DrawBackgroundAndDepthBar(obj, selectionRect);
            }

            // 2. 右側にコンポーネントのアイコンを表示
            DrawComponentIcons(obj, selectionRect);
        }

        private static void DrawFolderStyle(GameObject obj, Rect selectionRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            bool isSelected = Selection.Contains(obj);
            bool isHovered = (hoveredInstanceID == obj.GetInstanceID());

            // フォルダ用の背景色（目立つヘッダーのような色）
            Color bgColor;
            if (isSelected)
            {
                bgColor = EditorGUIUtility.isProSkin ? new Color32(44, 93, 135, 255) : new Color32(58, 114, 176, 255);
            }
            else if (isHovered)
            {
                bgColor = EditorGUIUtility.isProSkin ? new Color32(69, 69, 69, 255) : new Color32(200, 200, 200, 255);
            }
            else
            {
                bgColor = EditorGUIUtility.isProSkin ? new Color32(45, 45, 45, 255) : new Color32(190, 190, 190, 255);
            }

            // テキストや元のアイコンを隠すために背景を塗りつぶす (矢印は selectionRect.x より左なので隠れない)
            Rect bgRect = new Rect(selectionRect.x, selectionRect.y, selectionRect.width + 50, selectionRect.height);
            EditorGUI.DrawRect(bgRect, bgColor);

            // フォルダーアイコンを描画
            Texture folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
            if (folderIcon != null)
            {
                Rect iconRect = new Rect(selectionRect.x, selectionRect.y, 16, 16);
                GUI.DrawTexture(iconRect, folderIcon);
            }

            // テキストを太字で描画
            Rect textRect = new Rect(selectionRect.x + 18, selectionRect.y, selectionRect.width - 18, selectionRect.height);
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            Color textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.1f, 0.1f, 0.1f);

            if (isSelected) textColor = Color.white;
            if (!obj.activeInHierarchy) textColor.a = 0.5f; // 非アクティブ時は半透明

            style.normal.textColor = textColor;
            GUI.Label(textRect, obj.name, style);

            // 階層を示すカラーバーも左端に描画しておく
            int depth = GetDepth(obj.transform);
            Color depthColor = DepthColors[Mathf.Min(depth, DepthColors.Length - 1)];
            Rect leftBarRect = new Rect(selectionRect.x - 4, selectionRect.y, 2, selectionRect.height);
            EditorGUI.DrawRect(leftBarRect, new Color(depthColor.r, depthColor.g, depthColor.b, 1f));
        }

        private static void DrawBackgroundAndDepthBar(GameObject obj, Rect selectionRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            int depth = GetDepth(obj.transform);
            Color bgColor = DepthColors[Mathf.Min(depth, DepthColors.Length - 1)];

            if (bgColor.a > 0f)
            {
                // 選択中のオブジェクトは標準のハイライトを生かすため背景色を塗らない
                bool isSelected = Selection.Contains(obj);

                // マウスホバー中の行も、Unity標準のホバーフィードバック（薄いハイライト）を見せるために背景色を塗らない
                // 正確にトラッキングしたホバー状態を使用する
                bool isHovered = (hoveredInstanceID == obj.GetInstanceID());

                if (!isSelected && !isHovered)
                {
                    EnsureFadeTexture();

                    // 左側のカラーバーの位置 (selectionRect.x - 4) から右端までの範囲を計算
                    float startX = selectionRect.x - 4;
                    float width = (selectionRect.x + selectionRect.width) - startX;
                    Rect bgRect = new Rect(startX, selectionRect.y, width, selectionRect.height);

                    // フェードテクスチャを使ってグラデーションを描画
                    Color oldColor = GUI.color;
                    GUI.color = bgColor;
                    GUI.DrawTexture(bgRect, fadeTexture);
                    GUI.color = oldColor;
                }

                // 階層を視覚的に分かりやすくするための左側の細いカラーバー
                // 矢印アイコンとテキストの間あたりに配置 (x - 4)
                Rect leftBarRect = new Rect(selectionRect.x - 4, selectionRect.y, 2, selectionRect.height);
                Color barColor = new Color(bgColor.r, bgColor.g, bgColor.b, 1f);
                EditorGUI.DrawRect(leftBarRect, barColor);
            }
        }

        private static void DrawComponentIcons(GameObject obj, Rect selectionRect)
        {
            Component[] components = obj.GetComponents<Component>();
            if (components.Length <= 1) return; // Transformのみの場合はスキップ

            float iconSize = 16f;
            // 右端から左に向かってアイコンを配置していく
            float currentX = selectionRect.x + selectionRect.width - iconSize;

            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component comp = components[i];
                if (comp == null) continue; // Missing Script等の場合はnullになる
                if (comp is Transform) continue; // Transformは自明なので省略

                // コンポーネントのアイコン画像を取得
                Texture image = EditorGUIUtility.ObjectContent(comp, comp.GetType()).image;
                if (image == null) continue;

                Rect iconRect = new Rect(currentX, selectionRect.y, iconSize, iconSize);

                // オブジェクト名(テキスト)と被るのを防ぐため、一定以上左に行ったら描画停止
                if (iconRect.x < selectionRect.x + 80) break;

                // アイコンを描画（マウスオーバーでコンポーネント名がツールチップ表示されるようにする）
                GUIContent content = new GUIContent(image, comp.GetType().Name);
                GUI.Label(iconRect, content, GUIStyle.none);

                currentX -= iconSize + 2f; // 次のアイコンのために位置を左へずらす
            }
        }

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
