using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    /// <summary>
    /// シーン選択ウィンドウ
    /// プロジェクト内のシーンを一覧表示し、お気に入り登録や検索、ビルド設定の確認などができるエディタ拡張機能です。
    /// </summary>
    public class SceneSelectorWindow : EditorWindow
    {
        #region Constants & Keys

        // EditorPrefs保存用のキー
        private const string KeyFavorites = "SceneSelector_Favorites_GUIDs";
        private const string KeyFoldFavorites = "SceneSelector_Foldout_Favorites";
        private const string KeyFoldBuild = "SceneSelector_Foldout_Build";
        private const string KeyFoldAll = "SceneSelector_Foldout_All";
        private const string KeySortMode = "SceneSelector_SortMode";
        private const string KeyUseGlyphs = "SceneSelector_UseGlyphs"; // ユーザー設定：アイコンの代わりに文字(グリフ)を使用するか

        // レイアウト定数
        private const float RowHeight = 34f;
        private const float ActionButtonWidth = 24f;
        private const float ActionButtonSpacing = 2f;
        private const float RightPadding = 6f;
        private const float BuildTagWidth = 68f;
        private const int ActionButtonCount = 4; // 操作ボタンの数 (×, ▶, ＋, ⮕)
        private const string SearchControlName = "SceneSelector_Search";

        #endregion

        #region Internal Types

        /// <summary>
        /// シーンリストの表示項目データ
        /// </summary>
        private class SceneItem
        {
            public SceneAsset Asset;       // シーンアセット参照
            public string Name;            // シーン名
            public string Path;            // アセットパス
            public string Guid;            // アセットGUID
            public bool InBuild;           // ビルド設定に含まれているか
            public int BuildIndex;         // ビルド順序 (-1なら含まれていない)
            public DateTime LastWriteTime; // 最終更新日時（最近使った順ソート用）
        }

        /// <summary>
        /// ソートモード定義
        /// </summary>
        private enum SortMode
        {
            Name = 0,       // 名前順
            Path = 1,       // パス順
            Recent = 2,     // 最近更新された順
            BuildOrder = 3  // ビルド順
        }

        /// <summary>
        /// リストの種類
        /// </summary>
        private enum ListKind { Favorites, Build, All }

        /// <summary>
        /// アイコンID定義
        /// </summary>
        private enum IconId
        {
            Refresh,
            FavoriteOn,
            FavoriteOff,
            Scene,
            SetStart,
            OpenAdd,
            OpenSingleActive,
            Clear,
            Build
        }

        #endregion

        #region Fields

        // --- アイコン設定 ---

        // アイコンの検索候補名と代替グリフ文字のマッピング
        private static readonly Dictionary<IconId, (string[] names, string glyph)> IconCandidates = new()
        {
            [IconId.Refresh] = (new[] { "Refresh", "d_Refresh", "TreeEditor.Refresh", "d_TreeEditor.Refresh" }, "↻"),
            [IconId.FavoriteOn] = (new[] { "Favorite On Icon", "d_Favorite On Icon", "Favorite", "d_Favorite" }, "★"),
            [IconId.FavoriteOff] = (new[] { "Favorite", "d_Favorite", "Favorite Icon", "d_Favorite Icon" }, "☆"),
            [IconId.Scene] = (new[] { "SceneAsset Icon", "d_SceneAsset Icon" }, "■"),
            [IconId.SetStart] = (new[] { "PlayButton On", "d_PlayButton On", "PlayButton", "d_PlayButton" }, "▶"),
            [IconId.OpenAdd] = (new[] { "Toolbar Plus", "d_Toolbar Plus" }, "+"),
            [IconId.OpenSingleActive] = (new[] { "tab_next", "d_tab_next", "Forward", "d_Forward" }, "⮕"),
            [IconId.Clear] = (new[] { "Toolbar Minus", "d_Toolbar Minus" }, "×"),
            [IconId.Build] = (new[] { "BuildSettings.Editor", "d_BuildSettings.Editor", "BuildSettings", "d_BuildSettings" }, "BUILD")
        };

        // 解決済みアイコンのキャッシュ (ID + ProSkinフラグ => GUIContent)
        private static readonly Dictionary<(IconId id, bool proSkin), GUIContent> ResolvedIconCache = new();

        // 作業用変数
        private static readonly GUIContent TemporaryContent = new();
        private static readonly List<string> EmptyTokens = new();
        private static readonly List<SceneItem> EmptySceneList = new();
        private static readonly List<SceneItem> TemporarySceneList = new();

        // --- ステート ---

        private readonly List<SceneItem> _allScenes = new();
        private readonly List<SceneItem> _buildScenes = new();
        private readonly HashSet<string> _favoriteGuids = new(StringComparer.Ordinal);
        private readonly StringBuilder _stringBuilder = new(256);

        private bool _foldAll = true;
        private bool _foldBuild = true;
        private bool _foldFavorites = true;

        private Vector2 _scrollPosition;
        private string _searchString = string.Empty;

        // 選択状態管理
        private string _selectedGuid;
        private ListKind _selectedListKind = ListKind.All;
        private int _selectedLocalIndex = -1;

        // 環境設定
        private SortMode _currentSortMode;
        private bool _useGlyphs;

        // --- GUIスタイル & リソース ---

        private bool _stylesInitialized;
        private bool _stylesUseProSkin;
        private Color _mainTextColor;
        private Color _secondaryTextColor;
        private Color _headerTextColor;
        private Color _buildTextColor;
        private GUIStyle _rowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _tagStyle;
        private GUIStyle _hitLabelStyle;
        private GUIStyle _pathLabelStyle;
        private GUIStyle _actionButtonStyle;
        private GUIStyle _toolbarButtonStyle;
        private GUIStyle _toolbarPopupStyle;

        // アイコン付きGUIContent（ツールチップ含む）
        private GUIContent _gcFavOn, _gcFavOff, _gcSceneIcon, _gcRefresh, _gcBuild;
        private GUIContent _gcSetStart, _gcOpenAdditive, _gcOpenSingleActive, _gcClearStart;

        #endregion

        #region Lifecycle Methods

        public static void ShowWindow()
        {
            var window = GetWindow<SceneSelectorWindow>();
            window.titleContent = new GUIContent("Scene Selector");
            window.minSize = new Vector2(360, 340);
            window.Show();
        }

        private void OnEnable()
        {
            // 設定の読み込み
            _currentSortMode = (SortMode)EditorPrefs.GetInt(KeySortMode, (int)SortMode.Name);
            _foldFavorites = EditorPrefs.GetBool(KeyFoldFavorites, true);
            _foldBuild = EditorPrefs.GetBool(KeyFoldBuild, true);
            _foldAll = EditorPrefs.GetBool(KeyFoldAll, true);
            _useGlyphs = EditorPrefs.GetBool(KeyUseGlyphs, false);

            LoadFavorites();
            RefreshSceneList();
        }

        private void OnDisable()
        {
            // 設定の保存
            EditorPrefs.SetInt(KeySortMode, (int)_currentSortMode);
            EditorPrefs.SetBool(KeyFoldFavorites, _foldFavorites);
            EditorPrefs.SetBool(KeyFoldBuild, _foldBuild);
            EditorPrefs.SetBool(KeyFoldAll, _foldAll);
            EditorPrefs.SetBool(KeyUseGlyphs, _useGlyphs);

            SaveFavorites();
        }

        #endregion

        #region GUI Rendering

        private void OnGUI()
        {
            InitStyles();
            DrawToolbar();
            HandleKeyboardShortcuts();

            // ホバーエフェクト等を反応よくするためにマウス移動時だけは再描画
            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
            }

            using (var scrollScope = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scrollScope.scrollPosition;
                int globalIndex = 0;

                // 1. お気に入りセクション
                var favList = FilterAndSortScenes(GetFavoriteScenes());
                if (DrawSectionHeader(ref _foldFavorites, GetIcon(IconId.FavoriteOff), $"Favorites ({favList.Count})"))
                {
                    DrawSceneList(favList, ref globalIndex, isAltBase: false, ListKind.Favorites, drawStar: false, emptyMessage: "お気に入りはまだありません。シーンをドラッグするか、選択してFキーで追加できます。");
                }

                DrawSpacing();

                // 2. Build Settingsセクション
                var buildFiltered = FilterAndSortScenes(_buildScenes);
                if (DrawSectionHeader(ref _foldBuild, GetIcon(IconId.Build), $"Build Settings ({buildFiltered.Count})"))
                {
                    DrawSceneList(buildFiltered, ref globalIndex, isAltBase: true, ListKind.Build, emptyMessage: "Build Settings に登録されているシーンはありません。選択してBキーで追加できます。");
                }

                DrawSpacing();

                // 3. 全シーンセクション
                var allFiltered = FilterAndSortScenes(_allScenes);
                if (DrawSectionHeader(ref _foldAll, GetIcon(IconId.Scene), $"All Scenes ({allFiltered.Count})"))
                {
                    DrawSceneList(allFiltered, ref globalIndex, isAltBase: false, ListKind.All, emptyMessage: string.IsNullOrWhiteSpace(_searchString) ? "プロジェクト内にシーンが見つかりません。" : "検索条件に一致するシーンがありません。");
                }
            }

            HandleDragAndDropFavorites();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // 検索ボックス
                GUI.SetNextControlName(SearchControlName);
                var newSearch = GUILayout.TextField(_searchString, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160));
                if (newSearch != _searchString)
                {
                    _searchString = newSearch;
                    // フィルタが変わったら選択解除（インデックスずれ防止）
                    _selectedLocalIndex = -1;
                    _selectedGuid = null;
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_searchString)))
                {
                    if (GUILayout.Button("Clear", _toolbarButtonStyle, GUILayout.Width(44)))
                    {
                        ClearSearch();
                    }
                }

                // 更新ボタン
                if (GUILayout.Button(_gcRefresh, _toolbarButtonStyle, GUILayout.Width(28)))
                {
                    RefreshSceneList();
                }

                if (GUILayout.Button("All", _toolbarButtonStyle, GUILayout.Width(32)))
                {
                    SetAllFoldouts(true);
                }

                if (GUILayout.Button("None", _toolbarButtonStyle, GUILayout.Width(42)))
                {
                    SetAllFoldouts(false);
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(GetSelectedSceneItem() == null))
                {
                    if (GUILayout.Button("Ping", _toolbarButtonStyle, GUILayout.Width(42)))
                    {
                        PingSelectedScene();
                    }
                }

                if (GUILayout.Button("Build Settings", _toolbarButtonStyle, GUILayout.Width(96)))
                {
                    OpenBuildSettingsWindow();
                }

                // グリフアイコン切り替えトグル
                bool newUseGlyphs = GUILayout.Toggle(_useGlyphs, new GUIContent("Glyph Icons"), EditorStyles.toolbarButton);
                if (newUseGlyphs != _useGlyphs)
                {
                    _useGlyphs = newUseGlyphs;
                    ResolvedIconCache.Clear();
                    EnsureIconTooltips(); // キャッシュクリア後に再生成
                    Repaint();
                }

                // ソートモード選択
                EditorGUI.BeginChangeCheck();
                _currentSortMode = (SortMode)EditorGUILayout.Popup(
                    (int)_currentSortMode,
                    new[] { "Name", "Path", "Recent", "Build" },
                    _toolbarPopupStyle,
                    GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetInt(KeySortMode, (int)_currentSortMode);
                }

                // ヘルプボタン
                if (GUILayout.Button("Help", _toolbarButtonStyle, GUILayout.Width(48)))
                {
                    ShowHelpDialog();
                }
            }
        }

        /// <summary>
        /// セクションヘッダー（折りたたみ）を描画
        /// </summary>
        private bool DrawSectionHeader(ref bool foldout, GUIContent icon, string title)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 30f);

            // 背景色設定 (ProSkinかFreeSkinかで調整)
            Color bgColor = EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.22f, 0.27f, 0.95f)
                : new Color(0.86f, 0.9f, 0.95f, 1f);
            EditorGUI.DrawRect(rect, bgColor);

            // ボーダーライン
            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.45f, 0.55f, 0.6f)
                : new Color(0.58f, 0.68f, 0.78f, 0.65f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), borderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), borderColor);

            // パーツの配置計算
            Rect foldRect = new(rect.x + 8, rect.y + 7, 16, 16);
            Rect iconRect = new(foldRect.xMax + 5, rect.y + 6, 18, 18);
            Rect labelRect = new(iconRect.xMax + 7, rect.y + 6, Mathf.Max(0, rect.xMax - (iconRect.xMax + 60)), 18);

            foldout = EditorGUI.Foldout(foldRect, foldout, GUIContent.none, true);
            DrawColoredLabel(iconRect, icon, _headerTextColor);
            DrawColoredLabel(labelRect, title, _headerStyle, _headerTextColor);

            return foldout;
        }

        /// <summary>
        /// シーンリストの描画
        /// </summary>
        private void DrawSceneList(List<SceneItem> items, ref int globalIndex, bool isAltBase, ListKind kind, bool drawStar = true, string emptyMessage = null)
        {
            if (items.Count == 0)
            {
                DrawEmptySectionHint(emptyMessage);
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var rowRect = EditorGUILayout.GetControlRect(false, RowHeight);

                // 交互の背景色 (Zebra stripe)
                bool isAlt = ((globalIndex + (isAltBase ? 1 : 0)) & 1) == 1;
                DrawRowBackground(rowRect, isAlt);

                // ホバーおよび選択状態の描画
                HandleSelectionAndHover(rowRect, item, kind, i);

                // プレイモード開始シーンの強調表示
                DrawPlayModeIndicator(rowRect, item);

                // 行の中身の描画
                DrawRowContent(rowRect, item, drawStar);

                globalIndex++;
            }
        }

        private void DrawRowBackground(Rect rect, bool isAlt)
        {
            if (isAlt)
            {
                var color = EditorGUIUtility.isProSkin
                    ? new Color(1, 1, 1, 0.03f)
                    : new Color(0, 0, 0, 0.03f);
                EditorGUI.DrawRect(rect, color);
            }
        }

        private void HandleSelectionAndHover(Rect rect, SceneItem item, ListKind kind, int localIndex)
        {
            bool isHovered = rect.Contains(Event.current.mousePosition);
            bool isSelected = !string.IsNullOrEmpty(_selectedGuid) && _selectedGuid == item.Guid;

            // ホバーエフェクト
            if (isHovered && !isSelected)
            {
                var hoverColor = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.08f)
                    : new Color(0f, 0f, 0f, 0.08f);
                EditorGUI.DrawRect(rect, hoverColor);
            }

            // 選択時のハイライト
            if (isSelected)
            {
                var selColor = EditorGUIUtility.isProSkin
                    ? new Color(0.3f, 0.6f, 1f, 0.45f)
                    : new Color(0.4f, 0.65f, 1f, 0.35f);
                EditorGUI.DrawRect(rect, selColor);

                // 左端のアクセントバー
                var accentColor = EditorGUIUtility.isProSkin
                    ? new Color(0.4f, 0.75f, 1f, 0.9f)
                    : new Color(0.2f, 0.5f, 0.9f, 0.9f);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), accentColor);
            }

            // マウス入力処理
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selectedGuid = item.Guid;
                _selectedListKind = kind;
                _selectedLocalIndex = localIndex;

                if (Event.current.button == 0) // 左クリック
                {
                    if (Event.current.clickCount == 2) // ダブルクリック
                    {
                        ShowNotification(new GUIContent($"シーンを開いています: {item.Name}"));
                        OpenScene(item, additive: false, setActive: true);
                    }
                    Event.current.Use();
                    Repaint(); // 即時反映
                }
                else if (Event.current.button == 1) // 右クリック
                {
                    ShowContextMenu(item);
                    Event.current.Use();
                }
            }
        }

        private void DrawPlayModeIndicator(Rect rect, SceneItem item)
        {
            if (EditorSceneManager.playModeStartScene == item.Asset)
            {
                // 背景を薄い緑に
                var playBgColor = EditorGUIUtility.isProSkin
                    ? new Color(0.4f, 1f, 0.5f, 0.15f)
                    : new Color(0.3f, 0.8f, 0.4f, 0.1f);
                EditorGUI.DrawRect(rect, playBgColor);

                // 左端に緑のバー
                var accentColor = EditorGUIUtility.isProSkin
                    ? new Color(0.5f, 1f, 0.6f, 0.9f)
                    : new Color(0.2f, 0.6f, 0.3f, 0.9f);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), accentColor);
            }
        }

        private void DrawRowContent(Rect rect, SceneItem item, bool drawStar)
        {
            float x = rect.x + 8;
            float iconY = rect.y + 8;

            // ★ お気に入りボタン
            if (drawStar)
            {
                Rect starRect = new(x, iconY, 18, 18);
                bool isFav = _favoriteGuids.Contains(item.Guid);
                if (GUI.Button(starRect, isFav ? _gcFavOn : _gcFavOff, GUIStyle.none))
                {
                    ToggleFavorite(item.Guid);
                }
                x = starRect.xMax + 4;
            }

            // アイコン
            Rect iconRect = new(x, iconY, 18, 18);
            DrawColoredLabel(iconRect, _gcSceneIcon, _secondaryTextColor);
            x = iconRect.xMax + 7;

            // 右側のボタン領域の計算
            float reservedRightWidth = CalcRightReservedWidth(item.InBuild, ActionButtonCount);
            float availableWidth = Mathf.Max(0, rect.width - (x - rect.x) - reservedRightWidth);

            // シーン名 (検索ハイライト付き)
            Rect nameRect = new(x, rect.y + 4, availableWidth, 17);
            TemporaryContent.text = BuildHighlightText(item.Name, _searchString);
            TemporaryContent.tooltip = item.Path; // パスはツールチップで確認
            DrawColoredLabel(nameRect, TemporaryContent, _hitLabelStyle, _mainTextColor);

            Rect pathRect = new(x, rect.y + 19, availableWidth, 13);
            TemporaryContent.text = item.Path;
            TemporaryContent.tooltip = item.Path;
            DrawColoredLabel(pathRect, TemporaryContent, _pathLabelStyle, _secondaryTextColor);

            // --- 右側の操作ボタン ---
            float currentRightX = rect.xMax - RightPadding;

            // "Build Settings" タグ
            if (item.InBuild)
            {
                Rect tagRect = new(currentRightX - BuildTagWidth, rect.y + 8, BuildTagWidth, 18);
                Color tagBg = EditorGUIUtility.isProSkin
                    ? new Color(0.2f, 0.34f, 0.55f, 0.65f)
                    : new Color(0.72f, 0.82f, 0.96f, 0.95f);
                EditorGUI.DrawRect(tagRect, tagBg);
                DrawColoredLabel(tagRect, _gcBuild, _tagStyle, _buildTextColor);
                currentRightX = tagRect.x - 6f;
            }

            // 操作ボタン配置 (右詰め)
            float btnY = rect.y + 8;

            // [×] PlayMode開始シーン解除
            DrawActionButton(ref currentRightX, btnY, _gcClearStart,
                () => new Color(1f, 0.5f, 0.5f, 0.3f),
                ClearPlayModeStartScene);

            // [▶] PlayMode開始シーン設定
            DrawActionButton(ref currentRightX, btnY, _gcSetStart,
                () => new Color(0.5f, 1f, 0.5f, 0.3f),
                () => { if (item.Asset != null) SetPlayModeStartScene(item.Asset); });

            // [+] Additiveで開く
            DrawActionButton(ref currentRightX, btnY, _gcOpenAdditive,
                () => new Color(0.7f, 0.9f, 1f, 0.3f),
                () => OpenScene(item, additive: true, setActive: false));

            // [⮕] Singleで開いてActiveに
            DrawActionButton(ref currentRightX, btnY, _gcOpenSingleActive,
                () => new Color(0.7f, 0.9f, 1f, 0.3f),
                () => OpenScene(item, additive: false, setActive: true));
        }

        private void DrawActionButton(ref float rightX, float y, GUIContent content, Func<Color> hoverColorProvider, Action onClick)
        {
            Rect btnRect = new(rightX - ActionButtonWidth, y, ActionButtonWidth, 16);
            rightX -= (ActionButtonWidth + ActionButtonSpacing);

            var oldBg = GUI.backgroundColor;

            // ホバー時の色変更
            if (btnRect.Contains(Event.current.mousePosition))
            {
                GUI.backgroundColor = hoverColorProvider();
            }

            // ミニボタンスタイル
            if (GUI.Button(btnRect, content, _actionButtonStyle))
            {
                onClick?.Invoke();
            }

            GUI.backgroundColor = oldBg;
        }

        private static void DrawSpacing()
        {
            GUILayout.Space(6);
        }

        private void DrawEmptySectionHint(string message)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 28f);
            Color color = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.04f)
                : new Color(0f, 0f, 0f, 0.035f);
            EditorGUI.DrawRect(rect, color);

            TemporaryContent.text = message ?? "表示できるシーンがありません。";
            TemporaryContent.tooltip = string.Empty;
            var labelRect = new Rect(rect.x + 12f, rect.y + 6f, rect.width - 24f, 16f);
            DrawColoredLabel(labelRect, TemporaryContent, _pathLabelStyle, _secondaryTextColor);
        }

        #endregion

        #region Actions & Logic

        private void ShowHelpDialog()
        {
            EditorUtility.DisplayDialog(
                "Scene Selector Shortcuts",
                "Enter: Open\n" +
                "Shift+Enter: Open Additive\n" +
                "Ctrl/Cmd+Enter: Open Additive & Set Active\n" +
                "Ctrl/Cmd+F: Focus Search\n" +
                "Esc: Clear Search\n" +
                "F: Toggle Favorite\n" +
                "B: Toggle Build Settings\n" +
                "P: Set PlayMode Start Scene\n" +
                "R: Reveal in Project\n" +
                "Right Click: Context Menu\n" +
                "Drag & Drop Scene: Add to Favorites",
                "OK");
        }

        private void ShowContextMenu(SceneItem item)
        {
            var menu = new GenericMenu();

            // 開く操作
            menu.AddItem(new GUIContent("Open (Single)"), false, () => OpenScene(item, false, true));
            menu.AddItem(new GUIContent("Open Additive"), false, () => OpenScene(item, true, false));
            menu.AddItem(new GUIContent("Open Additive + Set Active"), false, () => OpenScene(item, true, true));

            menu.AddSeparator("");

            // PlayMode開始シーン設定
            menu.AddItem(new GUIContent("Set as PlayMode Start Scene"), false, () => SetPlayModeStartScene(item.Asset));
            menu.AddItem(new GUIContent("Clear PlayMode Start Scene"), false, ClearPlayModeStartScene);

            menu.AddSeparator("");

            // ビルド設定への追加/削除
            if (item.InBuild)
            {
                menu.AddItem(new GUIContent("Build Settings/Remove from Build"), false, () => ToggleBuildSettings(item, false));
            }
            else
            {
                menu.AddItem(new GUIContent("Build Settings/Add to Build"), false, () => ToggleBuildSettings(item, true));
            }

            menu.AddSeparator("");

            // その他ユーティリティ
            menu.AddItem(new GUIContent("Reveal in Project"), false, () => EditorGUIUtility.PingObject(item.Asset));
            menu.AddItem(new GUIContent("Copy/Copy Path"), false, () => EditorGUIUtility.systemCopyBuffer = item.Path);
            menu.AddItem(new GUIContent("Copy/Copy GUID"), false, () => EditorGUIUtility.systemCopyBuffer = item.Guid);

            menu.ShowAsContext();
        }

        private void OpenScene(SceneItem item, bool additive, bool setActive)
        {
            // 変更がある場合は保存を促す
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (additive)
            {
                Scene opened = EditorSceneManager.OpenScene(item.Path, OpenSceneMode.Additive);
                if (setActive)
                {
                    EditorSceneManager.SetActiveScene(opened);
                }
            }
            else
            {
                EditorSceneManager.OpenScene(item.Path, OpenSceneMode.Single);
            }
        }

        private void ToggleBuildSettings(SceneItem item, bool add)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            int existsIndex = scenes.FindIndex(s => s.guid.ToString() == item.Guid);

            if (add && existsIndex < 0)
            {
                scenes.Add(new EditorBuildSettingsScene(item.Path, true));
            }
            else if (!add && existsIndex >= 0)
            {
                scenes.RemoveAt(existsIndex);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            RefreshSceneList(); // リスト再構築
            SelectScene(item.Guid, ListKind.All);
        }

        private void SetPlayModeStartScene(SceneAsset scene)
        {
            EditorSceneManager.playModeStartScene = scene;
            ShowNotification(new GUIContent($"Play from: {scene.name}"));
        }

        private void ClearPlayModeStartScene()
        {
            EditorSceneManager.playModeStartScene = null;
            ShowNotification(new GUIContent("PlayMode開始シーンを解除"));
        }

        private void SetAllFoldouts(bool foldout)
        {
            _foldFavorites = foldout;
            _foldBuild = foldout;
            _foldAll = foldout;
            EditorPrefs.SetBool(KeyFoldFavorites, _foldFavorites);
            EditorPrefs.SetBool(KeyFoldBuild, _foldBuild);
            EditorPrefs.SetBool(KeyFoldAll, _foldAll);
            Repaint();
        }

        private void ClearSearch()
        {
            _searchString = string.Empty;
            _selectedLocalIndex = -1;
            _selectedGuid = null;
            GUI.FocusControl(null);
            Repaint();
        }

        private void OpenBuildSettingsWindow()
        {
            Type windowType =
                typeof(EditorWindow).Assembly.GetType("UnityEditor.BuildPlayerWindow") ??
                typeof(EditorWindow).Assembly.GetType("UnityEditor.Build.Profile.BuildProfileWindow") ??
                typeof(EditorWindow).Assembly.GetType("UnityEditor.BuildProfiles.BuildProfileWindow");

            if (windowType == null)
            {
                ShowNotification(new GUIContent("Build Settings を開けませんでした"));
                return;
            }

            var window = GetWindow(windowType);
            window.titleContent ??= new GUIContent("Build Settings");
            window.Show();
            window.Focus();
        }

        private SceneItem GetSelectedSceneItem()
        {
            if (string.IsNullOrEmpty(_selectedGuid)) return null;
            return _allScenes.FirstOrDefault(x => x.Guid == _selectedGuid);
        }

        private void PingSelectedScene()
        {
            var item = GetSelectedSceneItem();
            if (item?.Asset == null) return;

            Selection.activeObject = item.Asset;
            EditorGUIUtility.PingObject(item.Asset);
        }

        private void SelectScene(string guid, ListKind kind)
        {
            _selectedGuid = guid;
            _selectedListKind = kind;

            var currentList = kind switch
            {
                ListKind.Favorites => FilterAndSortScenes(GetFavoriteScenes()),
                ListKind.Build => FilterAndSortScenes(_buildScenes),
                _ => FilterAndSortScenes(_allScenes)
            };
            _selectedLocalIndex = currentList.FindIndex(x => x.Guid == guid);
            Repaint();
        }

        /// <summary>
        /// キーボードショートカットの処理
        /// </summary>
        private void HandleKeyboardShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if ((e.control || e.command) && e.keyCode == KeyCode.F)
            {
                GUI.FocusControl(SearchControlName);
                e.Use();
                return;
            }

            bool searchFocused = GUI.GetNameOfFocusedControl() == SearchControlName;
            if (searchFocused)
            {
                if (e.keyCode == KeyCode.Escape && !string.IsNullOrEmpty(_searchString))
                {
                    ClearSearch();
                    e.Use();
                }
                return;
            }

            if (e.keyCode == KeyCode.Escape && !string.IsNullOrEmpty(_searchString))
            {
                ClearSearch();
                e.Use();
                return;
            }

            // 現在選択中のリストを取得
            var currentList = _selectedListKind switch
            {
                ListKind.Favorites => FilterAndSortScenes(GetFavoriteScenes()),
                ListKind.Build => FilterAndSortScenes(_buildScenes),
                _ => FilterAndSortScenes(_allScenes)
            };

            if (currentList.Count == 0) return;

            // インデックス範囲チェック
            if (_selectedLocalIndex < 0 || _selectedLocalIndex >= currentList.Count)
            {
                _selectedLocalIndex = 0;
            }

            // 上下移動
            if (e.keyCode == KeyCode.DownArrow)
            {
                _selectedLocalIndex = Mathf.Clamp(_selectedLocalIndex + 1, 0, currentList.Count - 1);
                _selectedGuid = currentList[_selectedLocalIndex].Guid;
                e.Use();
                Repaint();
                return;
            }

            if (e.keyCode == KeyCode.UpArrow)
            {
                _selectedLocalIndex = Mathf.Clamp(_selectedLocalIndex - 1, 0, currentList.Count - 1);
                _selectedGuid = currentList[_selectedLocalIndex].Guid;
                e.Use();
                Repaint();
                return;
            }

            // Enterで開く
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                var item = currentList[_selectedLocalIndex];
                bool shift = e.shift;
                bool ctrl = e.control || e.command;

                if (shift && ctrl) OpenScene(item, true, true);
                else if (shift) OpenScene(item, true, false);
                else OpenScene(item, false, true);

                e.Use();
                return;
            }

            var selected = currentList[_selectedLocalIndex];

            if (e.keyCode == KeyCode.F)
            {
                ToggleFavorite(selected.Guid);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.B)
            {
                ToggleBuildSettings(selected, !selected.InBuild);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.P)
            {
                if (selected.Asset != null)
                {
                    SetPlayModeStartScene(selected.Asset);
                }
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.R)
            {
                PingSelectedScene();
                e.Use();
            }
        }

        /// <summary>
        /// ドラッグ&ドロップによるお気に入り登録処理
        /// </summary>
        private void HandleDragAndDropFavorites()
        {
            Event evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            Rect windowRect = new(Vector2.zero, position.size);
            if (!windowRect.Contains(evt.mousePosition)) return;

            Object[] objs = DragAndDrop.objectReferences;
            if (!objs.Any(o => o is SceneAsset)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                bool changed = false;
                foreach (Object o in objs)
                {
                    if (o is SceneAsset sa)
                    {
                        string path = AssetDatabase.GetAssetPath(sa);
                        string guid = AssetDatabase.AssetPathToGUID(path);
                        if (!string.IsNullOrEmpty(guid) && _favoriteGuids.Add(guid))
                        {
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    SaveFavorites();
                    Repaint();
                }
            }

            evt.Use();
        }

        #endregion

        #region Data Management

        private void RefreshSceneList()
        {
            _allScenes.Clear();
            _buildScenes.Clear();

            string[] foundGuids = AssetDatabase.FindAssets("t:SceneAsset");

            // Build Settingsに含まれるシーンのGUIDセット作成
            var buildSettingsScenes = EditorBuildSettings.scenes;
            var buildGuidSet = new HashSet<string>(
                buildSettingsScenes.Select(s => s.guid.ToString()),
                StringComparer.Ordinal);

            foreach (string guid in foundGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                var fileInfo = new FileInfo(path);
                bool inBuild = buildGuidSet.Contains(guid);
                int buildIndex = -1;

                if (inBuild)
                {
                    buildIndex = Array.FindIndex(buildSettingsScenes, s => s.guid.ToString() == guid);
                }

                var item = new SceneItem
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    Guid = guid,
                    InBuild = inBuild,
                    BuildIndex = buildIndex,
                    LastWriteTime = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue,
                    Asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path)
                };

                _allScenes.Add(item);
                if (inBuild)
                {
                    _buildScenes.Add(item);
                }
            }

            // リロード時は選択解除
            _selectedLocalIndex = -1;
            _selectedGuid = null;

            Repaint();
        }

        private List<SceneItem> GetFavoriteScenes()
        {
            if (_favoriteGuids.Count == 0) return EmptySceneList;

            TemporarySceneList.Clear();
            foreach (string guid in _favoriteGuids)
            {
                var found = _allScenes.FirstOrDefault(x => x.Guid == guid);
                if (found != null)
                {
                    TemporarySceneList.Add(found);
                }
            }
            return TemporarySceneList;
        }

        private List<SceneItem> FilterAndSortScenes(List<SceneItem> source)
        {
            IEnumerable<SceneItem> query = source;

            // 1. 検索フィルタ
            List<string> tokens = SplitSearchTokens(_searchString);
            if (tokens.Count > 0)
            {
                query = query.Where(item => tokens.All(token =>
                    (item.Name?.IndexOf(token, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (item.Path?.IndexOf(token, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0));
            }

            // 2. ソート
            query = _currentSortMode switch
            {
                SortMode.Name => query.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
                SortMode.Path => query.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase),
                SortMode.Recent => query.OrderByDescending(x => x.LastWriteTime),
                SortMode.BuildOrder => query.OrderBy(x => x.BuildIndex < 0 ? int.MaxValue : x.BuildIndex),
                _ => query
            };

            return query.ToList();
        }

        private static List<string> SplitSearchTokens(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return EmptyTokens;

            // 全角スペースも区切り文字として扱う
            input = input.Trim().Replace("　", " ");
            var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? EmptyTokens : parts.ToList();
        }

        private void LoadFavorites()
        {
            _favoriteGuids.Clear();
            string csv = EditorPrefs.GetString(KeyFavorites, "");
            if (string.IsNullOrEmpty(csv)) return;

            foreach (string g in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                _favoriteGuids.Add(g);
            }
        }

        private void SaveFavorites()
        {
            if (_favoriteGuids.Count == 0)
            {
                EditorPrefs.DeleteKey(KeyFavorites);
            }
            else
            {
                string csv = string.Join(",", _favoriteGuids);
                EditorPrefs.SetString(KeyFavorites, csv);
            }
        }

        private void ToggleFavorite(string guid)
        {
            if (!_favoriteGuids.Add(guid))
            {
                _favoriteGuids.Remove(guid);
            }
            SaveFavorites();
            Repaint();
        }

        #endregion

        #region UI Helpers & Styles

        private void InitStyles()
        {
            if (_stylesInitialized &&
                _stylesUseProSkin == EditorGUIUtility.isProSkin &&
                _headerStyle != null &&
                _tagStyle != null &&
                _hitLabelStyle != null &&
                _pathLabelStyle != null &&
                _actionButtonStyle != null &&
                _toolbarButtonStyle != null &&
                _toolbarPopupStyle != null)
            {
                return;
            }

            _rowStyle = new GUIStyle("RL Element")
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 3, 3),
                fontSize = 12
            };

            _stylesUseProSkin = EditorGUIUtility.isProSkin;
            _mainTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.92f, 0.95f, 0.98f)
                : new Color(0.08f, 0.1f, 0.12f);
            _secondaryTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.68f, 0.72f, 0.76f)
                : new Color(0.38f, 0.42f, 0.46f);
            _headerTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.96f, 0.98f, 1f)
                : new Color(0.08f, 0.12f, 0.18f);
            _buildTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.9f, 0.95f, 1f)
                : new Color(0.08f, 0.18f, 0.36f);

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13
            };
            ApplyTextColor(_headerStyle, _headerTextColor);

            _tagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 1, 1)
            };
            ApplyTextColor(_tagStyle, _buildTextColor);

            _hitLabelStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            ApplyTextColor(_hitLabelStyle, _mainTextColor);

            _pathLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            ApplyTextColor(_pathLabelStyle, _secondaryTextColor);

            _actionButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                fixedWidth = ActionButtonWidth,
                fixedHeight = 16
            };

            _toolbarButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
            _toolbarPopupStyle = new GUIStyle(EditorStyles.toolbarPopup);

            EnsureIconTooltips();
            _stylesInitialized = true;
        }

        private static void ApplyTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private static void DrawColoredLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            Color oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, text, style);
            GUI.contentColor = oldColor;
        }

        private static void DrawColoredLabel(Rect rect, GUIContent content, GUIStyle style, Color color)
        {
            Color oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, content, style);
            GUI.contentColor = oldColor;
        }

        private static void DrawColoredLabel(Rect rect, GUIContent content, Color color)
        {
            Color oldColor = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, content);
            GUI.contentColor = oldColor;
        }

        private void EnsureIconTooltips()
        {
            // アイコン取得とツールチップ設定
            _gcSetStart = CreateIconWithTooltip(IconId.SetStart, "このシーンをPlayMode開始シーンに設定 (⏎で起動時にこのシーンから開始)");
            _gcOpenAdditive = CreateIconWithTooltip(IconId.OpenAdd, "Additiveで開く (現在のシーンを維持したまま追加で開く)");
            _gcOpenSingleActive = CreateIconWithTooltip(IconId.OpenSingleActive, "Singleで開いてActiveに設定 (他のシーンを閉じる)");
            _gcClearStart = CreateIconWithTooltip(IconId.Clear, "PlayMode開始シーンの設定を解除");

            _gcFavOn = CreateIconWithTooltip(IconId.FavoriteOn, "お気に入りから削除");
            _gcFavOff = CreateIconWithTooltip(IconId.FavoriteOff, "お気に入りに追加 (Drag&Dropでも追加可能)");
            _gcSceneIcon = CreateIconWithTooltip(IconId.Scene, "シーンファイル");
            _gcRefresh = CreateIconWithTooltip(IconId.Refresh, "シーンリストを再読み込み");
            _gcBuild = new GUIContent("Build", GetIcon(IconId.Build).image, "Build Settings に含まれています");
        }

        private GUIContent CreateIconWithTooltip(IconId id, string tooltip)
        {
            var content = new GUIContent(GetIcon(id));
            content.tooltip = tooltip;
            return content;
        }

        private GUIContent GetIcon(IconId id)
        {
            if (_useGlyphs)
            {
                return new GUIContent(IconCandidates[id].glyph);
            }

            var key = (id, EditorGUIUtility.isProSkin);
            if (ResolvedIconCache.TryGetValue(key, out GUIContent cached))
            {
                return cached;
            }

            GUIContent gc = null;

            // SceneアイコンはUnity標準のObjectIconを使用
            if (id == IconId.Scene)
            {
                var oc = EditorGUIUtility.ObjectContent(null, typeof(SceneAsset));
                if (oc?.image != null) gc = new GUIContent(oc.image);
            }

            // 候補名からアイコンを探す
            if (gc == null)
            {
                foreach (string name in IconCandidates[id].names)
                {
                    var c = EditorGUIUtility.IconContent(name);
                    if (c?.image != null)
                    {
                        gc = c;
                        break;
                    }
                }
            }

            // どうしても見つからない場合はグリフで代用
            if (gc == null)
            {
                gc = new GUIContent(IconCandidates[id].glyph);
            }

            ResolvedIconCache[key] = gc;
            return gc;
        }

        /// <summary>
        /// 検索クエリに基づいてテキストをハイライトするHTMLタグを挿入
        /// </summary>
        private string BuildHighlightText(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return text;

            var tokens = SplitSearchTokens(query);
            if (tokens.Count == 0) return text;

            // 長いトークンから順に処理（部分一致の誤爆を防ぐ簡易策）
            var orderedTokens = tokens.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s.Length)
                .ToList();

            string result = text;
            foreach (string t in orderedTokens)
            {
                int idx = result.IndexOf(t, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    _stringBuilder.Clear();
                    _stringBuilder.Append(result, 0, idx)
                        .Append("<b><color=#FFD166>") // ハイライト色（黄色系）
                        .Append(result.Substring(idx, t.Length))
                        .Append("</color></b>")
                        .Append(result.Substring(idx + t.Length));
                    result = _stringBuilder.ToString();
                }
            }
            return result;
        }

        private static float CalcRightReservedWidth(bool inBuild, int actionCount)
        {
            float w = RightPadding + (ActionButtonWidth * actionCount) + (ActionButtonSpacing * (actionCount - 1));
            if (inBuild)
            {
                w += 6f + BuildTagWidth;
            }
            return w;
        }

        #endregion
    }
}
