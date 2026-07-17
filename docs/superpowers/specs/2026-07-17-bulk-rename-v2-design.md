# Bulk Rename v2 設計仕様

日付: 2026-07-17
ステータス: 承認済み

## 目的

既存の Bulk Rename 機能(4モード排他切替)を刷新し、以下を実現する。

1. 正規表現による検索/置換(グループ参照 `$1` 対応)
2. 複数操作を順に重ねて適用できるステップチェーン方式
3. ケース変換・トリム/正規化・トークン展開などの高度な置換
4. GUI の全面改善(選択自動追従、プレビュー強化、結果表示改善)
5. キーボードショートカット(Unity ShortcutManager 経由でユーザー設定可能)

## 非目標

- プリセットの保存/読み込み UI(最後に使った構成の自動復元のみ行う)
- アセットリネームの Undo 対応(AssetDatabase の制約により現状維持)
- 旧 API (`RenameMode` ベース) との互換レイヤー(外部利用者がいないため廃止)

## アーキテクチャ

現行と同じ 2 層構成を維持する。

- **コアロジック** `Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs`
  - UnityEngine / UnityEditor に依存しない純粋 C#。独自 asmdef (`BulkRenameOperations.asmdef`) 所属。NUnit で単体テスト可能。
- **UI** `Assets/JustEnoughEditor/Editor/BulkRenameWindow.cs`
  - EditorWindow。コアロジックを参照して名前生成とプレビュー、適用を行う。

## コアロジック設計

### データモデル

旧 `RenameMode` / 旧 `BulkRenameSettings` は廃止し、以下に置き換える。

```csharp
public enum RenameStepType
{
    FindReplace,      // 検索/置換 (正規表現対応)
    AddPrefixSuffix,  // 接頭辞/接尾辞の追加
    RemoveCharacters, // 先頭/末尾からの文字削除
    SetName,          // 名前全体の置き換え (トークン対応)
    ChangeCase,       // ケース変換
    TrimNormalize     // トリム / 空白正規化 / 重複サフィックス除去
}

public enum CaseConversion
{
    Uppercase, Lowercase, PascalCase, CamelCase, SnakeCase, KebabCase
}

[Serializable]
public class RenameStep
{
    public RenameStepType Type;
    public bool Enabled = true;

    // FindReplace
    public string FindText = "";
    public string ReplaceText = "";
    public bool CaseSensitive = true;
    public bool UseRegex = false;

    // AddPrefixSuffix
    public string AddPrefix = "";
    public string AddSuffix = "";

    // RemoveCharacters
    public int RemoveFromStart = 0;
    public int RemoveFromEnd = 0;

    // SetName
    public string NamePattern = "";

    // ChangeCase
    public CaseConversion CaseConversion = CaseConversion.Lowercase;

    // TrimNormalize
    public bool TrimWhitespace = true;
    public bool CollapseWhitespace = false;
    public bool RemoveDuplicateSuffix = false; // " (1)" 等の除去
}

[Serializable]
public class BulkRenameSettings
{
    public List<RenameStep> Steps = new List<RenameStep>();

    // <num> トークン共通設定
    public int StartNumber = 1;
    public int Digits = 2;
}
```

単一クラス + 種別 enum のフラット構造は Unity の `JsonUtility` シリアライズ(EditorPrefs 永続化)と相性が良いため採用する。ポリモーフィズムより保守性を優先。

### 適用パイプライン

```csharp
public static class BulkRenameOperations
{
    // Steps を順に適用した最終名を返す。Enabled=false のステップはスキップ。
    public static string GenerateNewName(string currentName, int index, BulkRenameSettings settings);

    // 1 ステップ分の変換。originalName はトークン <name> の展開に使用。
    public static string ApplyStep(string name, string originalName, int index, RenameStep step, BulkRenameSettings settings);

    // 設定全体の検証。正規表現の不正パターン等を検出し、最初のエラーを返す。
    public static bool TryValidate(BulkRenameSettings settings, out string error);

    public static bool IsValidFileName(string name); // 現行維持
    public static string FormatNumber(int index, BulkRenameSettings settings); // 現行維持
}
```

### トークン展開

`ReplaceText`(非 Regex 時)、`AddPrefix` / `AddSuffix`、`NamePattern` の各フィールドで以下を展開する。

- `<name>` — そのステップ適用前ではなく**元の名前**(パイプライン入力時点の名前)
- `<num>` — `StartNumber + index` を `Digits` 桁ゼロ埋め

Regex 有効時の `ReplaceText` では .NET の `$1` 等のグループ参照をそのまま使い、`<num>` / `<name>` トークンも併用可能(トークン展開 → Regex.Replace の順)。

### 正規表現

- `Regex.Replace` を使用。`CaseSensitive=false` 時は `RegexOptions.IgnoreCase`。
- 不正パターンは `TryValidate` が検出し、UI 側でエラー表示 + Apply 無効化。
- 暴走防止のため `Regex` に 1 秒の `matchTimeout` を設定する。

### ケース変換

単語分割規則: 既存の区切り(空白、`_`、`-`)と小文字→大文字境界で分割し、各形式に再結合する。

- `Uppercase`: 全体を大文字
- `Lowercase`: 全体を小文字
- `PascalCase`: 各単語の先頭を大文字にして連結
- `CamelCase`: PascalCase の先頭のみ小文字
- `SnakeCase`: 小文字化して `_` 連結
- `KebabCase`: 小文字化して `-` 連結

### トリム / 正規化

- `TrimWhitespace`: 前後の空白を削除
- `CollapseWhitespace`: 連続する空白を 1 個に圧縮
- `RemoveDuplicateSuffix`: 末尾の ` (数字)` パターン(Unity の複製サフィックス)を除去

## UI 設計 (BulkRenameWindow)

### レイアウト(上から順)

1. **ターゲットバー**: `Targets: N` + ロックトグル(Inspector 風)。ロック解除時は `OnSelectionChange` で選択を自動追従。ロック時は現在のリストを固定。
2. **ステップリスト**: `UnityEditorInternal.ReorderableList`。
   - 各行: Enabled トグル + ステップ種別ポップアップ + 種別ごとのフィールドをインライン表示
   - フッターの + ボタンで種別を選んで追加、- で削除、ドラッグで並べ替え
3. **Numbering セクション**: いずれかのフィールドに `<num>` が含まれる場合のみ Start Number / Digits を表示。
4. **プレビュー**: スクロールビュー(全幅、flexible 幅の 2 カラム)。
   - 変更あり: 新しい名前を緑系の色で表示
   - スキップ(空名・不正文字・重複): 赤色 + 理由テキストをインライン表示
   - 変更なし: グレー表示
   - ヘッダーに「N will change, M skipped」の集計
5. **Apply ボタン**: ターゲット 0 件、変更 0 件、またはバリデーションエラー時は無効化。アセットを含む場合は Undo 不可の警告を表示(現行踏襲)。
6. **結果表示**: Apply 後に HelpBox で「N renamed, M skipped」+ エラー詳細。

### 永続化

ウィンドウを閉じる際(`OnDisable`)に `BulkRenameSettings` を `JsonUtility` で JSON 化して EditorPrefs に保存し、`OnEnable` で復元する。キーは既存の JEEPrefs の命名規則に従う。

### キーボード操作

- **Enter**: Apply 実行(`EditorGUIUtility.editingTextField` が true の間は無効)
- **Esc**: ウィンドウを閉じる

## キーボードショートカット

`UnityEditor.ShortcutManagement.Shortcut` 属性でウィンドウオープンを登録する。

```csharp
[Shortcut("JEE/Bulk Rename", KeyCode.R, ShortcutModifiers.Alt)]
```

- デフォルト: **Alt+R**。Unity 標準の Edit > Shortcuts からユーザーが自由にリバインド可能。
- 既存のメニュー項目(`JEE/Bulk Rename`、`GameObject/JEE/Bulk Rename...`、`Assets/JEE/Bulk Rename...`)は維持。

## エラーハンドリング

- 正規表現の不正パターン: プレビュー無効化 + HelpBox でエラー表示 + Apply 無効化
- 生成名が空 / 不正文字 / 既存ファイルと衝突: 該当行をスキップ扱いにして理由を表示(現行踏襲)
- `AssetDatabase.RenameAsset` のエラー: スキップ数に計上し、Apply 後の結果に反映

## テスト計画

`BulkRenameOperationsTests.cs` を新 API 向けに書き直す。対象:

- Regex 置換: 基本置換、`$1` グループ参照、IgnoreCase、不正パターンの検証エラー
- トークン展開: `<name>` / `<num>`(桁数・開始番号)、Regex 置換との併用
- ケース変換: 6 形式それぞれの変換、区切り文字混在の単語分割
- トリム / 正規化: 各フラグの単独・併用動作
- ステップチェーン: 複数ステップの順序どおりの合成、Enabled=false のスキップ
- 既存機能の回帰: 接頭辞/接尾辞、文字削除、Set Name、IsValidFileName、FormatNumber

UI(EditorWindow)は単体テスト対象外とする。

## 実装方針

サブエージェント駆動開発(superpowers:subagent-driven-development)で、TDD によりタスク分割して実装する。
