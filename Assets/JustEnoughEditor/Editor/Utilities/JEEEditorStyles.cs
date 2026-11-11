using UnityEditor;
using UnityEngine;

namespace JustEnoughEditor
{
    public static class JEEEditorStyles
    {
        private const float k_HeaderHeight = 58f;

        private static GUIStyle s_headerTitle;
        private static GUIStyle s_headerSubtitle;
        private static GUIStyle s_sectionTitle;
        private static GUIStyle s_card;
        private static GUIStyle s_toolbarCard;
        private static GUIStyle s_listRow;
        private static GUIStyle s_inspectorTitleBar;
        private static GUIStyle s_badge;
        private static GUIStyle s_mutedLabel;
        private static GUIStyle s_emptyTitle;
        private static GUIStyle s_emptyBody;
        private static GUIStyle s_iconButton;
        private static GUIStyle s_pill;
        private static GUIStyle s_statusLabel;

        public static GUIStyle HeaderTitle
        {
            get
            {
                if (s_headerTitle == null)
                    s_headerTitle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 14,
                        fixedHeight = 20,
                        alignment = TextAnchor.LowerLeft
                    };
                return s_headerTitle;
            }
        }

        public static GUIStyle HeaderSubtitle
        {
            get
            {
                if (s_headerSubtitle == null)
                    s_headerSubtitle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        wordWrap = true,
                        fixedHeight = 30
                    };
                return s_headerSubtitle;
            }
        }

        public static GUIStyle SectionTitle
        {
            get
            {
                if (s_sectionTitle == null)
                    s_sectionTitle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        fixedHeight = 18
                    };
                return s_sectionTitle;
            }
        }

        public static GUIStyle Card
        {
            get
            {
                if (s_card == null)
                    s_card = new GUIStyle(EditorStyles.helpBox)
                    {
                        padding = new RectOffset(8, 8, 6, 6),
                        margin = new RectOffset(4, 4, 3, 4)
                    };
                return s_card;
            }
        }

        public static GUIStyle ToolbarCard
        {
            get
            {
                if (s_toolbarCard == null)
                    s_toolbarCard = new GUIStyle(EditorStyles.toolbar)
                    {
                        padding = new RectOffset(4, 4, 2, 2),
                        margin = new RectOffset(0, 0, 0, 2),
                        fixedHeight = 0
                    };
                return s_toolbarCard;
            }
        }

        public static GUIStyle ListRow
        {
            get
            {
                if (s_listRow == null)
                    s_listRow = new GUIStyle(EditorStyles.helpBox)
                    {
                        padding = new RectOffset(6, 6, 4, 4),
                        margin = new RectOffset(2, 2, 1, 2)
                    };
                return s_listRow;
            }
        }

        public static GUIStyle InspectorTitleBar
        {
            get
            {
                if (s_inspectorTitleBar == null)
                    s_inspectorTitleBar = new GUIStyle(EditorStyles.toolbar)
                    {
                        padding = new RectOffset(4, 4, 2, 2),
                        margin = new RectOffset(0, 0, 0, 0),
                        fixedHeight = 0
                    };
                return s_inspectorTitleBar;
            }
        }

        public static GUIStyle Badge
        {
            get
            {
                if (s_badge == null)
                    s_badge = new GUIStyle(EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fixedHeight = 18,
                        padding = new RectOffset(8, 8, 2, 2)
                    };
                return s_badge;
            }
        }

        public static GUIStyle MutedLabel
        {
            get
            {
                if (s_mutedLabel == null)
                {
                    s_mutedLabel = new GUIStyle(EditorStyles.miniLabel)
                    {
                        wordWrap = true
                    };
                    s_mutedLabel.normal.textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.64f, 0.68f, 0.72f, 1f)
                        : new Color(0.38f, 0.40f, 0.43f, 1f);
                }

                return s_mutedLabel;
            }
        }

        public static GUIStyle EmptyTitle
        {
            get
            {
                if (s_emptyTitle == null)
                    s_emptyTitle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 13
                    };
                return s_emptyTitle;
            }
        }

        public static GUIStyle EmptyBody
        {
            get
            {
                if (s_emptyBody == null)
                    s_emptyBody = new GUIStyle(MutedLabel)
                    {
                        alignment = TextAnchor.UpperCenter,
                        wordWrap = true
                    };
                return s_emptyBody;
            }
        }

        public static GUIStyle IconButton
        {
            get
            {
                if (s_iconButton == null)
                    s_iconButton = new GUIStyle(EditorStyles.miniButton)
                    {
                        fixedWidth = 26,
                        fixedHeight = 22,
                        padding = new RectOffset(4, 4, 3, 3)
                    };
                return s_iconButton;
            }
        }

        public static GUIStyle StatusLabel
        {
            get
            {
                if (s_statusLabel == null)
                    s_statusLabel = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fixedHeight = 20,
                        padding = new RectOffset(10, 10, 2, 2)
                    };
                return s_statusLabel;
            }
        }

        public static GUIStyle Pill
        {
            get
            {
                if (s_pill == null)
                    s_pill = new GUIStyle(EditorStyles.miniButton)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fixedHeight = 20,
                        padding = new RectOffset(10, 10, 2, 2)
                    };
                return s_pill;
            }
        }

        public static void DrawHeader(string title, string subtitle)
        {
            var rect = EditorGUILayout.GetControlRect(false, k_HeaderHeight);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            var icon = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image;
            if (icon == null)
                icon = EditorGUIUtility.IconContent("UnityEditor.InspectorWindow").image;

            var iconRect = new Rect(rect.x + 10, rect.y + 13, 28, 28);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            var titleRect = new Rect(rect.x + 46, rect.y + 8, rect.width - 58, 20);
            var subtitleRect = new Rect(rect.x + 46, rect.y + 29, rect.width - 58, 24);
            GUI.Label(titleRect, title, HeaderTitle);
            GUI.Label(subtitleRect, subtitle, HeaderSubtitle);
            EditorGUILayout.Space(4);
        }

        public static void BeginCard(string title)
        {
            EditorGUILayout.BeginVertical(Card);
            DrawCardTitle(title, null);
        }

        public static void BeginCard(string title, string iconName)
        {
            EditorGUILayout.BeginVertical(Card);
            DrawCardTitle(title, iconName);
        }

        public static void EndCard()
        {
            EditorGUILayout.EndVertical();
        }

        public static GUIStyle SearchFieldStyle()
        {
            return GUI.skin.FindStyle("ToolbarSearchTextField") ??
                   GUI.skin.FindStyle("ToolbarSeachTextField") ??
                   EditorStyles.toolbarTextField;
        }

        public static string DrawSearchField(string query, GUIContent clearButton)
        {
            GUILayout.Label(EditorGUIUtility.IconContent("Search Icon").image, GUILayout.Width(18),
                GUILayout.Height(18));
            query = GUILayout.TextField(query, SearchFieldStyle(), GUILayout.MinWidth(120),
                GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(query)))
            {
                if (GUILayout.Button(clearButton, GUILayout.Width(52)))
                {
                    query = "";
                    GUI.FocusControl(null);
                }
            }

            return query;
        }

        public static void DrawProjectColorBar(Rect selectionRect, Color color)
        {
            if (Event.current.type != EventType.Repaint || color == Color.clear) return;

            var barRect = selectionRect.height > 20
                ? new Rect(selectionRect.x + 4, selectionRect.y + 4, 4, selectionRect.width - 8)
                : new Rect(Mathf.Max(2, selectionRect.x - 20), selectionRect.y + 2, 4, selectionRect.height - 4);
            EditorGUI.DrawRect(barRect, new Color(color.r, color.g, color.b, 1f));
        }

        public static void DrawStatusLabel(string label, bool enabled, params GUILayoutOption[] options)
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent(enabled ? $"{label}: ON" : $"{label}: OFF"),
                StatusLabel, options);
            var backgroundColor = enabled
                ? new Color(0.18f, 0.46f, 0.27f, EditorGUIUtility.isProSkin ? 0.72f : 0.22f)
                : new Color(0.55f, 0.22f, 0.20f, EditorGUIUtility.isProSkin ? 0.72f : 0.20f);
            var textColor = enabled
                ? (EditorGUIUtility.isProSkin ? new Color(0.78f, 1f, 0.82f, 1f) : new Color(0.10f, 0.38f, 0.16f, 1f))
                : (EditorGUIUtility.isProSkin ? new Color(1f, 0.80f, 0.78f, 1f) : new Color(0.45f, 0.14f, 0.12f, 1f));

            EditorGUI.DrawRect(rect, backgroundColor);
            var previousColor = StatusLabel.normal.textColor;
            StatusLabel.normal.textColor = textColor;
            GUI.Label(rect, enabled ? $"{label}: ON" : $"{label}: OFF", StatusLabel);
            StatusLabel.normal.textColor = previousColor;
        }

        public static void DrawEmptyState(string title, string body, string iconName = "console.infoicon")
        {
            using (new EditorGUILayout.VerticalScope(Card))
            {
                GUILayout.Space(10);
                var icon = GetIcon(iconName);
                var iconRect = GUILayoutUtility.GetRect(36, 36, GUILayout.ExpandWidth(true));
                if (icon != null)
                {
                    iconRect.width = 36;
                    iconRect.x = (EditorGUIUtility.currentViewWidth - iconRect.width) * 0.5f;
                    GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                }

                GUILayout.Label(title, EmptyTitle);
                GUILayout.Label(body, EmptyBody, GUILayout.MinHeight(28));
                GUILayout.Space(8);
            }
        }

        public static Texture GetIcon(params string[] iconNames)
        {
            foreach (var iconName in iconNames)
            {
                if (string.IsNullOrEmpty(iconName))
                    continue;

                var icon = EditorGUIUtility.FindTexture(iconName);
                if (icon != null)
                    return icon;
            }

            return null;
        }

        private static void DrawCardTitle(string title, string iconName)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(iconName))
                {
                    var icon = GetIcon(iconName);
                    if (icon != null)
                        GUILayout.Label(icon, GUILayout.Width(18), GUILayout.Height(18));
                }

                GUILayout.Label(title, SectionTitle);
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(3);
        }
    }
}
