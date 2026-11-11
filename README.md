# Just Enough Editor 使い方ガイド

Just Enough Editor は、Unity Editor の見た目と操作を少しだけ整理するためのツールです。
Hierarchy と Project ウィンドウを見やすくし、よく使うアセットへすぐアクセスできるようにします。

コードを書かなくても使えます。Unity の上部メニューに追加される `JEE` メニューから操作します。

## できること

- Hierarchy の行に階層ごとの色を付けて、オブジェクトのまとまりを見やすくする
- 選択中の GameObject と子オブジェクトの範囲をガイド線で表示する
- Hierarchy の右側にコンポーネントのアイコンを表示する
- 任意の GameObject にカスタムアイコンを付ける
- Hierarchy に見出し用の区切り行を作る
- よく使うフォルダやアセットを `Favorite Assets` に登録する
- `Scene Selector` でシーンを検索し、お気に入りやビルド設定を管理する
- Unity のウィンドウタブを名前・色付きのグループにまとめて折りたたむ
- 登録したお気に入りを Project ウィンドウ上で色付き表示する
- Missing Script が残っている GameObject を探して削除する
- 表示設定を JSON ファイルとして書き出し、別環境へ読み込む

## 開き方

Unity を開くと、上部メニューに `JEE` が追加されます。

主に使う入口は次の4つです。

| やりたいこと | メニュー |
| --- | --- |
| お気に入りアセットを管理する | `JEE > Favorite Assets` |
| シーンを検索して開く | `JEE > Scene Selector` |
| 全体設定を変更する | `JEE > Settings` |
| Missing Script を探す | `JEE > Missing Script Finder` |

Unity の Preferences から設定したい場合は、`Edit > Preferences > Just Enough Editor` を開きます。

## スクリプトから使う

Unity 6.0 以降では、公開ユーティリティを自作の Editor 拡張から利用できます。使用するスクリプトは `Editor` フォルダに置くか、Editor 専用の asmdef に含めてください。

その asmdef の参照に `JustEnoughEditor.Utilities` を追加します。

```json
"references": [
  "JustEnoughEditor.Utilities"
]
```

```csharp
var data = JEEAssetUtility.LoadOrCreateAsset<MyEditorData>("Assets/Editor/MyEditorData.asset");
Debug.Log(JEEHierarchyUtility.GetPath(Selection.activeTransform));

JEEEditorStyles.BeginCard("My Tool");
EditorGUILayout.LabelField("Ready");
JEEEditorStyles.EndCard();
```

## 最初に確認する設定

`JEE > Settings` を開くと、現在の ON/OFF 状態と基本設定をまとめて変更できます。

| 設定 | 内容 |
| --- | --- |
| `Enable Hierarchy Extension` | Hierarchy 拡張全体の ON/OFF |
| `Draw hierarchy colors` | Hierarchy の背景色と左端の色バーを表示する |
| `Draw component icons` | Hierarchy の右端にコンポーネントアイコンを表示する |
| `Draw selected scope guide` | 選択中の GameObject と子オブジェクトの範囲をガイド線で表示する |
| `Icon Overlap Guard` | アイコンが名前に重ならないようにする余白 |
| `Max Icon Count` | 1行に表示するアイコン数 |
| `Component Priority` | どの種類のコンポーネントを優先して表示するか |
| `Draw favorite item colors in Project window` | お気に入りの色を Project ウィンドウにも表示する |

表示が多すぎると感じた場合は、まず `Max Icon Count` を下げるか、`Icon Filters` で不要なアイコンを非表示にしてください。

## Favorite Assets の使い方

`Favorite Assets` は、よく使うフォルダやアセットをまとめておく小さなブックマーク一覧です。

### 登録する

1. `JEE > Favorite Assets` を開きます。
2. Project ウィンドウからフォルダやアセットをウィンドウ内へドラッグします。
3. または Project ウィンドウでアセットを選択してから `Add Selected` を押します。

登録できるものは、Unity の Project ウィンドウにあるフォルダやアセットです。

### 開く・探す

- 行をダブルクリックすると、そのアセットを開きます。
- `Open` を押しても開けます。
- `Reveal` を押すと、Project ウィンドウ内の場所を表示します。
- 上部の検索欄で名前やパスを絞り込めます。

### 並べ替える・削除する

- 行をドラッグすると順番を変えられます。
- ゴミ箱アイコンを押すと一覧から削除します。
- 削除しても、元のアセット自体は消えません。

### 色を変える

各行の色欄から色を選ぶと、Project ウィンドウでそのアセットに色バーが表示されます。
見た目だけの印です。アセットの中身は変更されません。

### 個人用と共有用

上部の切り替えで保存先を選べます。

| 保存先 | 使い分け |
| --- | --- |
| `Personal_EditorPrefs` | 自分だけのメモとして使う |
| `Shared_ScriptableObject` | プロジェクト内の他の人と共有したい一覧に使う |

共有用を選ぶと、プロジェクト内に共有データが保存されます。

## Scene Selector の使い方

`JEE > Scene Selector` を開くと、お気に入り・ビルド設定・全シーンの一覧を表示します。

- 検索欄でシーン名やパスを絞り込み、`Name`・`Path`・`Recent`・`Build` で並べ替えます。
- シーンを選択して `Enter` で開き、`Shift+Enter` で現在のシーンに追加します。未保存の変更があれば保存確認が表示されます。
- シーンをドラッグ＆ドロップしてお気に入りに追加し、選択して `F` で登録を切り替えます。
- 選択して `B` でビルド設定への登録、`P` で PlayMode の開始シーンを設定できます。
- 右クリックで各操作を選べます。`Help` でショートカットを確認できます。

お気に入りと表示設定は個人用の EditorPrefs に保存されます。

## Window Groups の使い方

Scene、Game、Inspector、Console、自作の Editor ウィンドウなどを、同じタブバーの中でグループ化できます。

タブを右クリックすると、`Window Groups` から直接操作できます。

- `New Group`：右クリックしたタブから、その場でグループを作成します。名前はタブ名から設定され、色は同じタブバーのグループと重ならないよう自動で割り当てます。
- `Add to Group`：右クリックしたタブを、同じタブバーの既存グループへ追加します。
- 登録済みのタブでは `Settings…` で Project Settings を開き、`Remove from Group` でそのタブをグループから外せます。

設定は `Edit > Project Settings > Just Enough Editor > Window Groups` にまとめています。グループ名の変更、所属タブの追加・解除、折りたたみ・展開、グループ解除ができます。`JEE > Window Groups > Settings…` からも開けます。専用の管理・作成ウィンドウはありません。

グループの先頭に、色付きアイコン・名前・件数を持つ見出しタブが追加されます。見出しから所属タブの末尾まで同じ色のラインがつながり、グループの範囲を示します。元のタブ名・アイコンはそのままです。色は名前変更や折りたたみで変わりません。

- 見出しタブのクリックは選択だけです。誤操作を避けるため、折りたたみ・展開は見出しを右クリックして `Collapse Group` / `Expand Group` を選ぶか、Project Settings の `Collapse` / `Expand` ボタンで操作します。
- 見出しを右クリックして `Add Tab` を選ぶと、同じタブバーのウィンドウを直接追加できます。
- 折りたたみ中もウィンドウは保持されます。通常の Window メニューから対象を開くと、グループが展開されます。
- Project Settings の `Add Tab…` でも同じタブバーの未登録ウィンドウを追加できます。`Remove` は対象を通常のタブに戻します。
- 同じ種類のウィンドウを複数開いている場合は、一覧の `#番号` で区別できます。この番号は現在の起動中の識別用です。
- 見出しタブを別のドックへ移動すると、グループ内のウィンドウも移動します。展開中のメンバーを別のドックへ移すと、そのメンバーだけがグループから外れます。
- 見出しを最大化した場合は、`Restore Group View` で元の配置に戻してからグループを操作します。
- `Ungroup` または見出しタブを閉じる操作で、メンバーを閉じずにグループを解除します。
- すべて解除する場合は `JEE > Window Groups > Restore All Grouped Tabs` を選びます。

グループはUnityのレイアウトと一緒に保存されます。入れ子のグループや、ポップアップ・モーダル・ユーティリティ専用ウィンドウは対象外です。
動作確認済みのバージョンは Unity 6000.3.7f1 です。Unity の内部 API を使うため、他のバージョンは未検証です。必要な API が見つからない環境では操作を無効にし、理由を表示します。右クリックメニューの追加に非対応の環境では、新規作成は利用できませんが、Project Settings での既存グループの変更と `Restore All Grouped Tabs` は利用できます。

## Hierarchy を見やすくする

Hierarchy 拡張が ON のとき、Hierarchy の行に色やアイコンが表示されます。

### 選択範囲を見やすくする

Hierarchy で GameObject を選択すると、その GameObject と表示中の子オブジェクトの範囲にガイド線が表示されます。
子が多いオブジェクトを選んだときに、どこまでが選択中オブジェクトの階層かを追いやすくするための表示です。

この表示は `JEE > Settings` の `Draw selected scope guide`、または `JEE > Enable Hierarchy Scope Guide` で切り替えられます。
Hierarchy の色表示を OFF にしている場合、Scope Guide も表示されません。

### 区切り行を作る

メニューから見出し用の GameObject を作れます。

- `JEE > Create Section Separator`
- `JEE > Create Group Separator`

Hierarchy 上で GameObject を右クリックして、`JEE > Create Section Separator` または `JEE > Create Group Separator` を選ぶこともできます。
右クリックから作ると、選択中の GameObject の近くに作成されます。

### カスタムアイコンを付ける

1. Hierarchy で GameObject を選択します。
2. `JEE > Assign Custom Icon...` を選びます。
3. 表示された選択画面から使いたい Texture を選びます。

Hierarchy で GameObject を右クリックして、`JEE > Assign Custom Icon...` からも設定できます。

外したいときは、対象の GameObject を選んで `JEE > Remove Custom Icon` を選びます。

## Icon Filter Settings の使い方

`JEE > Icon Filter Settings` では、Hierarchy に表示するコンポーネントアイコンを種類ごとに ON/OFF できます。

使い方は次の通りです。

1. 表示を調整したいシーンを開きます。
2. `JEE > Icon Filter Settings` を開きます。
3. 不要なコンポーネントのチェックを外します。
4. 一覧に出てこない場合は `Refresh` を押します。

この一覧には、現在開いているシーンに存在するコンポーネントが表示されます。
すべて元に戻したい場合は `Reset All Filters` を押します。

## Missing Script Finder の使い方

`Missing Script Finder` は、Missing Script が残っている GameObject を探すためのツールです。

1. 調べたいシーンを開きます。
2. `JEE > Missing Script Finder` を開きます。
3. 一覧に表示された GameObject を確認します。
4. 必要に応じて `Select`、`Ping`、`Remove` を使います。

| ボタン | 内容 |
| --- | --- |
| `Refresh` | もう一度探す |
| `Select` | 対象の GameObject を選択する |
| `Ping` | Hierarchy 上で場所を分かりやすく表示する |
| `Remove` | その GameObject から Missing Script を削除する |
| `Remove All` | 一覧にある Missing Script をまとめて削除する |

`Remove` と `Remove All` は Missing Script の参照だけを削除します。
GameObject 自体は削除されません。

## 設定の書き出し・読み込み

設定を別のPCや別プロジェクトへ移したい場合は、JSON ファイルとして保存できます。

- 書き出し: `JEE > Export Settings...`
- 読み込み: `JEE > Import Settings...`

書き出される主な内容は、Hierarchy の表示設定、Scope Guide の表示設定、Project ウィンドウの色表示設定、アイコン表示数、アイコンフィルターです。

## 表示を一時的に止めたいとき

上部メニューから各機能を ON/OFF できます。

| メニュー | 内容 |
| --- | --- |
| `JEE > Enable Hierarchy Extension` | Hierarchy 拡張全体を切り替える |
| `JEE > Enable Hierarchy Colors` | Hierarchy の色表示だけを切り替える |
| `JEE > Enable Hierarchy Icons` | Hierarchy のアイコン表示だけを切り替える |
| `JEE > Enable Hierarchy Scope Guide` | 選択中オブジェクトの階層ガイドだけを切り替える |
| `JEE > Enable Favorite Assets Extension` | Project ウィンドウのお気に入り色表示を切り替える |

メニューにチェックが付いているものが ON です。

## 困ったとき

| 状況 | 確認すること |
| --- | --- |
| Hierarchy に色やアイコンが出ない | `JEE > Enable Hierarchy Extension` が ON か確認する |
| アイコンが多すぎる | `JEE > Settings` の `Max Icon Count` を下げる |
| 不要なアイコンが出る | `JEE > Icon Filter Settings` でチェックを外す |
| 名前とアイコンが重なる | `Icon Overlap Guard` を大きくする |
| Scope Guide が出ない | `JEE > Enable Hierarchy Colors` と `JEE > Enable Hierarchy Scope Guide` が ON か確認する |
| Project ウィンドウに色が出ない | `JEE > Enable Favorite Assets Extension` が ON か確認する |
| Favorite Assets に追加できない | Project ウィンドウ内のフォルダやアセットをドラッグしているか確認する |
| Missing Script が見つからない | 対象のシーンを開いてから `Refresh` を押す |

## 注意点

- このツールは Unity Editor の見た目と操作補助を追加するものです。
- ゲーム実行時の動作には影響しません。
- 個人用設定は自分の Unity Editor に保存されます。
- 共有用の Favorite Assets はプロジェクト内にデータを作成します。
- Missing Script の削除は元に戻せるよう Unity の Undo に登録されますが、実行前に対象を確認してください。
