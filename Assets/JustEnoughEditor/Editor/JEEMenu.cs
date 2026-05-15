using UnityEditor;

namespace JustEnoughEditor
{
    /// <summary>
    /// JustEnoughEditor の各機能の ON/OFF トグルと設定メニューを提供する静的クラス。
    /// 設定値はすべて EditorPrefs に永続化される。
    /// </summary>
    public static class JEEMenu
    {
        /// <summary>
        /// Hierarchy 拡張機能全体のマスタートグル。
        /// false の場合、背景色・アイコン描画をすべてスキップする。
        /// </summary>
        public static bool IsHierarchyEnabled
        {
            get => JEEPrefs.IsHierarchyEnabled;
            set => JEEPrefs.IsHierarchyEnabled = value;
        }

        /// <summary>
        /// Favorite Folders のプロジェクトウィンドウ色付け機能のトグル。
        /// </summary>
        public static bool IsProjectColorsEnabled
        {
            get => JEEPrefs.IsProjectColorsEnabled;
            set => JEEPrefs.IsProjectColorsEnabled = value;
        }

        /// <summary>
        /// Hierarchy の背景色・カラーバー描画の個別トグル。
        /// </summary>
        public static bool IsHierarchyColorEnabled
        {
            get => JEEPrefs.IsHierarchyColorEnabled;
            set => JEEPrefs.IsHierarchyColorEnabled = value;
        }

        /// <summary>
        /// Hierarchy のコンポーネントアイコン描画の個別トグル。
        /// </summary>
        public static bool IsHierarchyIconEnabled
        {
            get => JEEPrefs.IsHierarchyIconEnabled;
            set => JEEPrefs.IsHierarchyIconEnabled = value;
        }

        /// <summary>
        /// アイコンがオブジェクト名テキストに重なるのを防ぐ最小水平距離（ピクセル）。
        /// 読み取り時は常に [40, 200] にクランプして書き戻す。
        /// </summary>
        public static int OverlapGuard
        {
            get => JEEPrefs.OverlapGuard;
            set => JEEPrefs.OverlapGuard = value;
        }

        /// <summary>Favorite Assets ウィンドウを開く。</summary>
        [MenuItem("JEE/Favorite Assets", false, 1)]
        public static void OpenFavoriteAssetsWindow()
        {
            FavoriteAssetsWindow.ShowWindow();
        }

        /// <summary>JustEnoughEditor の統合設定ウィンドウを開く。</summary>
        [MenuItem("JEE/Settings", false, 2)]
        public static void OpenSettingsWindow()
        {
            JEESettingsWindow.ShowWindow();
        }

        /// <summary>Hierarchy 拡張機能全体の ON/OFF を切り替える。</summary>
        [MenuItem("JEE/Enable Hierarchy Extension", false, 50)]
        public static void ToggleHierarchyExtension()
        {
            IsHierarchyEnabled = !IsHierarchyEnabled;
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("JEE/Enable Hierarchy Extension", true)]
        public static bool ValidateToggleHierarchyExtension()
        {
            Menu.SetChecked("JEE/Enable Hierarchy Extension", IsHierarchyEnabled);
            return true;
        }

        /// <summary>Hierarchy の背景色・カラーバー描画の ON/OFF を切り替える。</summary>
        [MenuItem("JEE/Enable Hierarchy Colors", false, 51)]
        public static void ToggleHierarchyColor()
        {
            IsHierarchyColorEnabled = !IsHierarchyColorEnabled;
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("JEE/Enable Hierarchy Colors", true)]
        public static bool ValidateToggleHierarchyColor()
        {
            Menu.SetChecked("JEE/Enable Hierarchy Colors", IsHierarchyColorEnabled);
            return true;
        }

        /// <summary>Hierarchy のコンポーネントアイコン描画の ON/OFF を切り替える。</summary>
        [MenuItem("JEE/Enable Hierarchy Icons", false, 52)]
        public static void ToggleHierarchyIcon()
        {
            IsHierarchyIconEnabled = !IsHierarchyIconEnabled;
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("JEE/Enable Hierarchy Icons", true)]
        public static bool ValidateToggleHierarchyIcon()
        {
            Menu.SetChecked("JEE/Enable Hierarchy Icons", IsHierarchyIconEnabled);
            return true;
        }

        /// <summary>Favorite Assets のプロジェクトウィンドウ色付けの ON/OFF を切り替える。</summary>
        [MenuItem("JEE/Enable Favorite Assets Extension", false, 53)]
        public static void ToggleProjectColors()
        {
            IsProjectColorsEnabled = !IsProjectColorsEnabled;
            EditorApplication.RepaintProjectWindow();
        }

        [MenuItem("JEE/Enable Favorite Assets Extension", true)]
        public static bool ValidateToggleProjectColors()
        {
            Menu.SetChecked("JEE/Enable Favorite Assets Extension", IsProjectColorsEnabled);
            return true;
        }

        /// <summary>アイコンフィルター設定ウィンドウを開く。</summary>
        [MenuItem("JEE/Icon Filter Settings", false, 60)]
        public static void OpenIconFilterWindow()
        {
            IconFilterWindow.ShowWindow();
        }

        /// <summary>Missing Script Finder ウィンドウを開く。</summary>
        [MenuItem("JEE/Missing Script Finder", false, 60)]
        public static void OpenMissingScriptFinderWindow()
        {
            MissingScriptFinderWindow.ShowWindow();
        }

        [MenuItem("JEE/Export Settings...", false, 90)]
        public static void ExportSettings()
        {
            JEESettingsIO.ExportSettings();
        }

        [MenuItem("JEE/Import Settings...", false, 91)]
        public static void ImportSettings()
        {
            JEESettingsIO.ImportSettings();
        }

        /// <summary>Overlap Guard を 40px に設定する。</summary>
        [MenuItem("JEE/Icon Overlap Guard/40px", false, 61)]
        public static void SetOverlapGuard40()
        {
            OverlapGuard = 40;
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>Overlap Guard を 80px に設定する。</summary>
        [MenuItem("JEE/Icon Overlap Guard/80px", false, 62)]
        public static void SetOverlapGuard80()
        {
            OverlapGuard = 80;
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>Overlap Guard を 120px に設定する。</summary>
        [MenuItem("JEE/Icon Overlap Guard/120px", false, 63)]
        public static void SetOverlapGuard120()
        {
            OverlapGuard = 120;
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>Overlap Guard を 160px に設定する。</summary>
        [MenuItem("JEE/Icon Overlap Guard/160px", false, 64)]
        public static void SetOverlapGuard160()
        {
            OverlapGuard = 160;
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>Overlap Guard を 200px に設定する。</summary>
        [MenuItem("JEE/Icon Overlap Guard/200px", false, 65)]
        public static void SetOverlapGuard200()
        {
            OverlapGuard = 200;
            EditorApplication.RepaintHierarchyWindow();
        }
    }
}
