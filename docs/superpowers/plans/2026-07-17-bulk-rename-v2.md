# Bulk Rename v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bulk Rename を「4モード排他切替」から「ステップチェーン方式」に刷新し、正規表現・トークン・ケース変換・トリムを追加、GUI を全面改善、キーボードショートカットを追加する。

**Architecture:** コアロジック(`BulkRenameOperations.asmdef`、UnityEngine 非依存の純粋 C#)と UI(`BulkRenameWindow` EditorWindow)の 2 層構成を維持。新 API(`RenamePipeline` + `RenameStep` のリスト)を旧 API と共存させながらタスクごとに積み上げ、最終タスクで旧 API を削除する。各タスク完了時点でプロジェクトは常にコンパイル可能・全テストグリーン。

**Tech Stack:** Unity 6000.3.7f1 / C# / NUnit (Unity Test Framework, EditMode) / `System.Text.RegularExpressions` / `UnityEditorInternal.ReorderableList` / `UnityEditor.ShortcutManagement`

**Spec:** `docs/superpowers/specs/2026-07-17-bulk-rename-v2-design.md`

## Global Constraints

- UI ラベル・メニュー名は**英語**、コードコメント・コミットメッセージは**日本語**(リポジトリ既存の規約)。
- `Assets/JustEnoughEditor/Editor/BulkRename/` 配下(コアロジック)は UnityEngine / UnityEditor に**依存禁止**。純粋 C# のみ。
- コミットメッセージ末尾に `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` を付ける。
- 新規 `.cs` ファイルの `.meta` は Unity がテスト実行時に自動生成する。**テスト実行後に `git status` で新規 .meta を確認し、必ず一緒にコミットする。**
- `.sdd-test-results.xml` / `.sdd-unity.log` は**コミットしない**(テスト実行の一時ファイル)。
- **標準テストコマンド**(以下「STANDARD TEST RUN」と呼ぶ。PowerShell で実行、タイムアウトは 600000ms を指定):

```powershell
$proj = (Get-Location).Path
$p = Start-Process -FilePath "C:\Program Files\Unity\Hub\Editor\6000.3.7f1\Editor\Unity.exe" -ArgumentList "-batchmode","-projectPath","$proj","-runTests","-testPlatform","EditMode","-testResults","$proj\.sdd-test-results.xml","-logFile","$proj\.sdd-unity.log" -Wait -PassThru
"ExitCode: $($p.ExitCode)"
(Select-Xml -Path "$proj\.sdd-test-results.xml" -XPath '/test-run').Node | Select-Object testcasecount, result, passed, failed
```

  - **`-quit` フラグは絶対に付けない**(`-runTests` と競合し、テストが実行されずに終了する。過去に確認済みの罠)。
  - ExitCode 0 = 全テスト成功。非 0 のときは `.sdd-unity.log` を確認する。
  - ログに "another Unity instance is running" 系のエラーがある場合、この プロジェクトを開いている Unity Editor を閉じてから再実行する。
  - コンパイルエラーがある場合はテストが走らず ExitCode 非 0 になり、ログに `error CS` 行が出る。TDD の RED としてコンパイルエラーも有効な失敗とみなす。

---

### Task 1: RenamePipeline データモデルと基本 4 ステップのチェーン適用

**Files:**
- Create: `Assets/JustEnoughEditor/Editor/BulkRename/RenamePipeline.cs`
- Modify: `Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs`(既存コードは削除せず、末尾に追記)
- Test: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs`(新規)

**Interfaces:**
- Consumes: 既存の `private static string ReplaceCaseInsensitive(string source, string find, string replace)`(`BulkRenameOperations.cs` に存在)
- Produces(後続タスクが依存):
  - `enum RenameStepType { FindReplace, AddPrefixSuffix, RemoveCharacters, SetName, ChangeCase, TrimNormalize }`
  - `enum CaseConversion { Uppercase, Lowercase, PascalCase, CamelCase, SnakeCase, KebabCase }`
  - `class RenameStep`(全フィールドは下記コード参照)
  - `class RenamePipeline { List<RenameStep> Steps; int StartNumber; int Digits; }`
  - `string BulkRenameOperations.GenerateNewName(string currentName, int index, RenamePipeline pipeline)`
  - `string BulkRenameOperations.ApplyStep(string name, string originalName, int index, RenameStep step, RenamePipeline pipeline)`

- [ ] **Step 1: 失敗するテストを書く**

`Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs` を新規作成:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace JustEnoughEditor.BulkRename.Tests
{
    public class RenamePipelineTests
    {
        private static RenamePipeline PipelineWith(params RenameStep[] steps)
        {
            return new RenamePipeline { Steps = new List<RenameStep>(steps) };
        }

        [Test]
        public void GenerateNewName_EmptyPipelineReturnsNameUnchanged()
        {
            Assert.AreEqual("Enemy", BulkRenameOperations.GenerateNewName("Enemy", 0, new RenamePipeline()));
        }

        [Test]
        public void FindReplace_ReplacesAllOccurrences()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, FindText = "Old", ReplaceText = "New"
            });

            Assert.AreEqual("New_New_Thing", BulkRenameOperations.GenerateNewName("Old_Old_Thing", 0, pipeline));
        }

        [Test]
        public void FindReplace_CanIgnoreCase()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, FindText = "old", ReplaceText = "new", CaseSensitive = false
            });

            Assert.AreEqual("new_new_Thing", BulkRenameOperations.GenerateNewName("Old_OLD_Thing", 0, pipeline));
        }

        [Test]
        public void FindReplace_EmptyFindTextReturnsNameUnchanged()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, FindText = "", ReplaceText = "new"
            });

            Assert.AreEqual("Thing", BulkRenameOperations.GenerateNewName("Thing", 0, pipeline));
        }

        [Test]
        public void AddPrefixSuffix_AddsBoth()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.AddPrefixSuffix, AddPrefix = "FX_", AddSuffix = "_v2"
            });

            Assert.AreEqual("FX_Enemy_v2", BulkRenameOperations.GenerateNewName("Enemy", 0, pipeline));
        }

        [Test]
        public void RemoveCharacters_RemovesFromStartAndEnd()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.RemoveCharacters, RemoveFromStart = 3, RemoveFromEnd = 2
            });

            Assert.AreEqual("my", BulkRenameOperations.GenerateNewName("OLDmyXX", 0, pipeline));
        }

        [Test]
        public void RemoveCharacters_ClampsToNameLength()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.RemoveCharacters, RemoveFromStart = 100, RemoveFromEnd = 100
            });

            Assert.AreEqual("", BulkRenameOperations.GenerateNewName("Short", 0, pipeline));
        }

        [Test]
        public void SetName_ReplacesWholeName()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.SetName, NamePattern = "Enemy"
            });

            Assert.AreEqual("Enemy", BulkRenameOperations.GenerateNewName("AnythingAtAll", 0, pipeline));
        }

        [Test]
        public void Chain_AppliesStepsInOrder()
        {
            var pipeline = PipelineWith(
                new RenameStep { Type = RenameStepType.FindReplace, FindText = "Old", ReplaceText = "New" },
                new RenameStep { Type = RenameStepType.AddPrefixSuffix, AddPrefix = "FX_" });

            Assert.AreEqual("FX_New_Enemy", BulkRenameOperations.GenerateNewName("Old_Enemy", 0, pipeline));
        }

        [Test]
        public void Chain_SkipsDisabledSteps()
        {
            var pipeline = PipelineWith(
                new RenameStep { Type = RenameStepType.FindReplace, FindText = "Old", ReplaceText = "New", Enabled = false },
                new RenameStep { Type = RenameStepType.AddPrefixSuffix, AddPrefix = "FX_" });

            Assert.AreEqual("FX_Old_Enemy", BulkRenameOperations.GenerateNewName("Old_Enemy", 0, pipeline));
        }
    }
}
```

- [ ] **Step 2: テストが失敗することを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 非 0。`.sdd-unity.log` に `error CS0246`(`RenamePipeline` / `RenameStepType` が見つからない)が出る。これが RED。

- [ ] **Step 3: データモデルを実装する**

`Assets/JustEnoughEditor/Editor/BulkRename/RenamePipeline.cs` を新規作成:

```csharp
using System;
using System.Collections.Generic;

namespace JustEnoughEditor.BulkRename
{
    /// <summary>リネームステップの種別。</summary>
    public enum RenameStepType
    {
        FindReplace,
        AddPrefixSuffix,
        RemoveCharacters,
        SetName,
        ChangeCase,
        TrimNormalize
    }

    /// <summary>ケース変換の形式。</summary>
    public enum CaseConversion
    {
        Uppercase,
        Lowercase,
        PascalCase,
        CamelCase,
        SnakeCase,
        KebabCase
    }

    /// <summary>
    /// 1 つのリネーム操作を表すステップ。Type に応じて使用するフィールドが異なる。
    /// JsonUtility でシリアライズするため、種別ごとの派生クラスではなくフラットな単一クラスとする。
    /// </summary>
    [Serializable]
    public class RenameStep
    {
        public RenameStepType Type = RenameStepType.FindReplace;
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
        public bool RemoveDuplicateSuffix = false;
    }

    /// <summary>リネームステップの並びと、&lt;num&gt; トークンの共通設定。</summary>
    [Serializable]
    public class RenamePipeline
    {
        public List<RenameStep> Steps = new List<RenameStep>();

        public int StartNumber = 1;
        public int Digits = 2;
    }
}
```

- [ ] **Step 4: チェーン適用を実装する**

`Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs` の `BulkRenameOperations` クラス末尾(`IsValidFileName` の後、クラスの閉じ括弧の前)に追記:

```csharp
        // ---- ステップチェーン方式 (v2) ----

        /// <summary>パイプラインの全ステップを順に適用した新しい名前を返す。Enabled=false のステップはスキップする。</summary>
        public static string GenerateNewName(string currentName, int index, RenamePipeline pipeline)
        {
            var name = currentName;

            foreach (var step in pipeline.Steps)
            {
                if (step == null || !step.Enabled) continue;
                name = ApplyStep(name, currentName, index, step, pipeline);
            }

            return name;
        }

        /// <summary>1 ステップ分の変換を適用する。originalName はパイプライン入力時点の名前。</summary>
        public static string ApplyStep(string name, string originalName, int index, RenameStep step, RenamePipeline pipeline)
        {
            switch (step.Type)
            {
                case RenameStepType.FindReplace:
                    return ApplyFindReplaceStep(name, step);
                case RenameStepType.AddPrefixSuffix:
                    return (step.AddPrefix ?? "") + name + (step.AddSuffix ?? "");
                case RenameStepType.RemoveCharacters:
                    return RemoveCharacters(name, step.RemoveFromStart, step.RemoveFromEnd);
                case RenameStepType.SetName:
                    return step.NamePattern ?? "";
                default:
                    return name;
            }
        }

        private static string ApplyFindReplaceStep(string name, RenameStep step)
        {
            if (string.IsNullOrEmpty(step.FindText)) return name;

            var replace = step.ReplaceText ?? "";
            if (step.CaseSensitive) return name.Replace(step.FindText, replace);
            return ReplaceCaseInsensitive(name, step.FindText, replace);
        }

        private static string RemoveCharacters(string name, int fromStart, int fromEnd)
        {
            var removeStart = Math.Max(0, Math.Min(fromStart, name.Length));
            name = name.Substring(removeStart);

            var removeEnd = Math.Max(0, Math.Min(fromEnd, name.Length));
            return name.Substring(0, name.Length - removeEnd);
        }
```

- [ ] **Step 5: テストが通ることを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=30(既存 19 + 新規 11)。

- [ ] **Step 6: コミット**

```powershell
git status   # 新規 .meta (RenamePipeline.cs.meta, RenamePipelineTests.cs.meta) を確認
git add Assets/JustEnoughEditor/Editor/BulkRename/RenamePipeline.cs Assets/JustEnoughEditor/Editor/BulkRename/RenamePipeline.cs.meta Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs.meta
git commit -m @'
RenamePipeline データモデルとステップチェーン適用を追加

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: 正規表現置換と TryValidate

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs`
- Test: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs`

**Interfaces:**
- Consumes: Task 1 の `RenamePipeline` / `RenameStep`(`UseRegex`, `FindText`, `ReplaceText`, `CaseSensitive` フィールド)、`GenerateNewName(string, int, RenamePipeline)`、private `ApplyFindReplaceStep(string, RenameStep)`
- Produces: `bool BulkRenameOperations.TryValidate(RenamePipeline pipeline, out string error)`(不正 regex 検出。エラーなしなら error=null で true)

- [ ] **Step 1: 失敗するテストを書く**

`RenamePipelineTests.cs` のクラス末尾に追記:

```csharp
        [Test]
        public void Regex_ReplacesWithGroupReference()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, UseRegex = true,
                FindText = @"(\d+)_(\w+)", ReplaceText = "$2_$1"
            });

            Assert.AreEqual("Enemy_01", BulkRenameOperations.GenerateNewName("01_Enemy", 0, pipeline));
        }

        [Test]
        public void Regex_CanIgnoreCase()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, UseRegex = true,
                FindText = "enemy", ReplaceText = "Boss", CaseSensitive = false
            });

            Assert.AreEqual("Boss_01", BulkRenameOperations.GenerateNewName("ENEMY_01", 0, pipeline));
        }

        [Test]
        public void TryValidate_FailsForInvalidRegexPattern()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, UseRegex = true, FindText = "("
            });

            Assert.IsFalse(BulkRenameOperations.TryValidate(pipeline, out var error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void TryValidate_PassesForValidRegexAndLiteralSteps()
        {
            var pipeline = PipelineWith(
                new RenameStep { Type = RenameStepType.FindReplace, UseRegex = true, FindText = @"\d+" },
                new RenameStep { Type = RenameStepType.FindReplace, FindText = "(" });

            Assert.IsTrue(BulkRenameOperations.TryValidate(pipeline, out var error));
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidate_IgnoresDisabledSteps()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, UseRegex = true, FindText = "(", Enabled = false
            });

            Assert.IsTrue(BulkRenameOperations.TryValidate(pipeline, out var error));
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 非 0。`.sdd-unity.log` に `error CS0117`(`TryValidate` が存在しない)。

- [ ] **Step 3: 実装する**

`BulkRenameOperations.cs` の先頭 using に追加:

```csharp
using System.Text.RegularExpressions;
```

private `ApplyFindReplaceStep` を以下に置き換え:

```csharp
        private static string ApplyFindReplaceStep(string name, RenameStep step)
        {
            if (string.IsNullOrEmpty(step.FindText)) return name;

            var replace = step.ReplaceText ?? "";

            if (step.UseRegex)
            {
                var options = step.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                // 破滅的バックトラッキングによるエディタのフリーズを防ぐため 1 秒でタイムアウトさせる
                return Regex.Replace(name, step.FindText, replace, options, TimeSpan.FromSeconds(1));
            }

            if (step.CaseSensitive) return name.Replace(step.FindText, replace);
            return ReplaceCaseInsensitive(name, step.FindText, replace);
        }
```

クラス末尾に `TryValidate` を追加:

```csharp
        /// <summary>パイプライン全体を検証する。不正な正規表現パターンを検出したら最初のエラーを返す。</summary>
        public static bool TryValidate(RenamePipeline pipeline, out string error)
        {
            foreach (var step in pipeline.Steps)
            {
                if (step == null || !step.Enabled) continue;
                if (step.Type != RenameStepType.FindReplace || !step.UseRegex) continue;
                if (string.IsNullOrEmpty(step.FindText)) continue;

                try
                {
                    _ = new Regex(step.FindText);
                }
                catch (ArgumentException e)
                {
                    error = $"Invalid regex pattern \"{step.FindText}\": {e.Message}";
                    return false;
                }
            }

            error = null;
            return true;
        }
```

- [ ] **Step 4: テストが通ることを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=35。

- [ ] **Step 5: コミット**

```powershell
git add Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs
git commit -m @'
Find/Replace ステップに正規表現置換と TryValidate を追加

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: トークン展開 (`<name>` / `<num>`)

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs`
- Test: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs`

**Interfaces:**
- Consumes: Task 1 の `RenamePipeline.StartNumber` / `Digits`、`ApplyStep`、private `ApplyFindReplaceStep`
- Produces:
  - `string BulkRenameOperations.ExpandTokens(string text, string originalName, int index, RenamePipeline pipeline)`
  - `string BulkRenameOperations.FormatNumber(int index, RenamePipeline pipeline)`(既存の `FormatNumber(int, BulkRenameSettings)` とは別オーバーロード)
  - トークンは `ReplaceText` / `AddPrefix` / `AddSuffix` / `NamePattern` で展開される(`FindText` では展開しない)

- [ ] **Step 1: 失敗するテストを書く**

`RenamePipelineTests.cs` のクラス末尾に追記:

```csharp
        [Test]
        public void FormatNumber_PadsWithStartNumberAndDigits()
        {
            var pipeline = new RenamePipeline { StartNumber = 1, Digits = 3 };

            Assert.AreEqual("001", BulkRenameOperations.FormatNumber(0, pipeline));
            Assert.AreEqual("002", BulkRenameOperations.FormatNumber(1, pipeline));
        }

        [Test]
        public void FormatNumber_DoesNotTruncateWhenNumberExceedsDigits()
        {
            var pipeline = new RenamePipeline { StartNumber = 99, Digits = 1 };

            Assert.AreEqual("100", BulkRenameOperations.FormatNumber(1, pipeline));
        }

        [Test]
        public void NumToken_InSuffixProducesSequentialNames()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.AddPrefixSuffix, AddSuffix = "_<num>"
            });
            pipeline.StartNumber = 1;
            pipeline.Digits = 2;

            Assert.AreEqual("Enemy_01", BulkRenameOperations.GenerateNewName("Enemy", 0, pipeline));
            Assert.AreEqual("Enemy_02", BulkRenameOperations.GenerateNewName("Enemy", 1, pipeline));
        }

        [Test]
        public void NumToken_InSetNamePattern()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.SetName, NamePattern = "Enemy_<num>"
            });
            pipeline.StartNumber = 1;
            pipeline.Digits = 3;

            Assert.AreEqual("Enemy_005", BulkRenameOperations.GenerateNewName("Old", 4, pipeline));
        }

        [Test]
        public void NameToken_ExpandsToOriginalPipelineInputName()
        {
            // <name> は直前ステップの結果ではなく、パイプライン入力時点の名前を指す
            var pipeline = PipelineWith(
                new RenameStep { Type = RenameStepType.SetName, NamePattern = "X" },
                new RenameStep { Type = RenameStepType.AddPrefixSuffix, AddSuffix = "_<name>" });

            Assert.AreEqual("X_Enemy", BulkRenameOperations.GenerateNewName("Enemy", 0, pipeline));
        }

        [Test]
        public void NumToken_InLiteralReplaceText()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, FindText = "###", ReplaceText = "<num>"
            });
            pipeline.StartNumber = 10;
            pipeline.Digits = 2;

            Assert.AreEqual("Enemy_10", BulkRenameOperations.GenerateNewName("Enemy_###", 0, pipeline));
        }

        [Test]
        public void NumToken_WorksTogetherWithRegexGroupReference()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, UseRegex = true,
                FindText = "(Enemy)", ReplaceText = "$1_<num>"
            });
            pipeline.StartNumber = 1;
            pipeline.Digits = 2;

            Assert.AreEqual("Enemy_01", BulkRenameOperations.GenerateNewName("Enemy", 0, pipeline));
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 非 0。`FormatNumber(int, RenamePipeline)` オーバーロード未定義のコンパイルエラー、または(コンパイルが通る書き方をした場合)トークンが展開されず文字列そのままで Assert 失敗。

- [ ] **Step 3: 実装する**

`BulkRenameOperations.cs` クラス末尾に追加:

```csharp
        /// <summary>&lt;name&gt;(パイプライン入力時点の名前)と &lt;num&gt;(連番)のトークンを展開する。</summary>
        public static string ExpandTokens(string text, string originalName, int index, RenamePipeline pipeline)
        {
            if (string.IsNullOrEmpty(text)) return "";

            return text
                .Replace("<name>", originalName)
                .Replace("<num>", FormatNumber(index, pipeline));
        }

        /// <summary>StartNumber + index を Digits 桁までゼロ埋めした連番文字列を返す。</summary>
        public static string FormatNumber(int index, RenamePipeline pipeline)
        {
            var number = pipeline.StartNumber + index;
            var digits = Math.Max(1, pipeline.Digits);
            return number.ToString().PadLeft(digits, '0');
        }
```

`ApplyStep` を以下に置き換え(トークン展開を配線):

```csharp
        /// <summary>1 ステップ分の変換を適用する。originalName はパイプライン入力時点の名前(トークン &lt;name&gt; の展開に使用)。</summary>
        public static string ApplyStep(string name, string originalName, int index, RenameStep step, RenamePipeline pipeline)
        {
            switch (step.Type)
            {
                case RenameStepType.FindReplace:
                    return ApplyFindReplaceStep(name, originalName, index, step, pipeline);
                case RenameStepType.AddPrefixSuffix:
                    return ExpandTokens(step.AddPrefix, originalName, index, pipeline)
                           + name
                           + ExpandTokens(step.AddSuffix, originalName, index, pipeline);
                case RenameStepType.RemoveCharacters:
                    return RemoveCharacters(name, step.RemoveFromStart, step.RemoveFromEnd);
                case RenameStepType.SetName:
                    return ExpandTokens(step.NamePattern, originalName, index, pipeline);
                default:
                    return name;
            }
        }
```

private `ApplyFindReplaceStep` を以下に置き換え(シグネチャ変更。トークン展開 → Regex.Replace の順):

```csharp
        private static string ApplyFindReplaceStep(string name, string originalName, int index, RenameStep step, RenamePipeline pipeline)
        {
            if (string.IsNullOrEmpty(step.FindText)) return name;

            var replace = ExpandTokens(step.ReplaceText, originalName, index, pipeline);

            if (step.UseRegex)
            {
                var options = step.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                // 破滅的バックトラッキングによるエディタのフリーズを防ぐため 1 秒でタイムアウトさせる
                return Regex.Replace(name, step.FindText, replace, options, TimeSpan.FromSeconds(1));
            }

            if (step.CaseSensitive) return name.Replace(step.FindText, replace);
            return ReplaceCaseInsensitive(name, step.FindText, replace);
        }
```

注意: `ExpandTokens` は空文字列/null に対して `""` を返すため、`ReplaceText` が空でも従来どおり「削除置換」として動作する。

- [ ] **Step 4: テストが通ることを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=42。

- [ ] **Step 5: コミット**

```powershell
git add Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs
git commit -m @'
<name> / <num> トークン展開を追加

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: ケース変換ステップ

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs`
- Test: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs`

**Interfaces:**
- Consumes: Task 1 の `CaseConversion` enum、`RenameStep.CaseConversion` フィールド、`ApplyStep` の switch
- Produces: `string BulkRenameOperations.ConvertCase(string name, CaseConversion conversion)`

- [ ] **Step 1: 失敗するテストを書く**

`RenamePipelineTests.cs` のクラス末尾に追記:

```csharp
        [Test]
        public void ChangeCase_Uppercase()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.ChangeCase, CaseConversion = CaseConversion.Uppercase
            });

            Assert.AreEqual("ENEMY_01", BulkRenameOperations.GenerateNewName("enemy_01", 0, pipeline));
        }

        [Test]
        public void ChangeCase_Lowercase()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.ChangeCase, CaseConversion = CaseConversion.Lowercase
            });

            Assert.AreEqual("enemy_01", BulkRenameOperations.GenerateNewName("Enemy_01", 0, pipeline));
        }

        [Test]
        public void ChangeCase_PascalCase()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.ChangeCase, CaseConversion = CaseConversion.PascalCase
            });

            Assert.AreEqual("OldEnemyBoss", BulkRenameOperations.GenerateNewName("old_enemy boss", 0, pipeline));
        }

        [Test]
        public void ChangeCase_CamelCase()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.ChangeCase, CaseConversion = CaseConversion.CamelCase
            });

            Assert.AreEqual("oldEnemy", BulkRenameOperations.GenerateNewName("Old_Enemy", 0, pipeline));
        }

        [Test]
        public void ChangeCase_SnakeCase_SplitsOnLowerToUpperBoundary()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.ChangeCase, CaseConversion = CaseConversion.SnakeCase
            });

            Assert.AreEqual("old_enemy_boss", BulkRenameOperations.GenerateNewName("OldEnemyBoss", 0, pipeline));
        }

        [Test]
        public void ChangeCase_KebabCase()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.ChangeCase, CaseConversion = CaseConversion.KebabCase
            });

            Assert.AreEqual("old-enemy-boss", BulkRenameOperations.GenerateNewName("OldEnemy Boss", 0, pipeline));
        }

        [Test]
        public void ChangeCase_PascalCase_HandlesMixedSeparators()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.ChangeCase, CaseConversion = CaseConversion.PascalCase
            });

            Assert.AreEqual("MyCoolName", BulkRenameOperations.GenerateNewName("my-Cool_name", 0, pipeline));
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 非 0。`ApplyStep` の default 分岐で名前が変換されず Assert 失敗(ChangeCase の 7 テストすべて)。

- [ ] **Step 3: 実装する**

`BulkRenameOperations.cs` の先頭 using に追加(なければ):

```csharp
using System.Collections.Generic;
using System.Linq;
```

`ApplyStep` の switch に case を追加(`case RenameStepType.SetName:` の後):

```csharp
                case RenameStepType.ChangeCase:
                    return ConvertCase(name, step.CaseConversion);
```

クラス末尾に追加:

```csharp
        /// <summary>名前を指定したケース形式に変換する。</summary>
        public static string ConvertCase(string name, CaseConversion conversion)
        {
            switch (conversion)
            {
                case CaseConversion.Uppercase:
                    return name.ToUpperInvariant();
                case CaseConversion.Lowercase:
                    return name.ToLowerInvariant();
            }

            var words = SplitWords(name);
            switch (conversion)
            {
                case CaseConversion.PascalCase:
                    return string.Concat(words.Select(Capitalize));
                case CaseConversion.CamelCase:
                    var pascal = string.Concat(words.Select(Capitalize));
                    return pascal.Length > 0 ? char.ToLowerInvariant(pascal[0]) + pascal.Substring(1) : pascal;
                case CaseConversion.SnakeCase:
                    return string.Join("_", words.Select(w => w.ToLowerInvariant()));
                case CaseConversion.KebabCase:
                    return string.Join("-", words.Select(w => w.ToLowerInvariant()));
                default:
                    return name;
            }
        }

        private static string Capitalize(string word)
        {
            if (word.Length == 0) return word;
            return char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
        }

        /// <summary>空白・アンダースコア・ハイフン、および小文字→大文字の境界で単語に分割する。</summary>
        private static string[] SplitWords(string name)
        {
            var words = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var c in name)
            {
                if (c == ' ' || c == '_' || c == '-')
                {
                    if (current.Length > 0)
                    {
                        words.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                if (char.IsUpper(c) && current.Length > 0 && char.IsLower(current[current.Length - 1]))
                {
                    words.Add(current.ToString());
                    current.Clear();
                }

                current.Append(c);
            }

            if (current.Length > 0) words.Add(current.ToString());
            return words.ToArray();
        }
```

- [ ] **Step 4: テストが通ることを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=49。

- [ ] **Step 5: コミット**

```powershell
git add Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs
git commit -m @'
ケース変換ステップ (6形式) を追加

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 5: トリム / 正規化ステップ

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs`
- Test: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs`

**Interfaces:**
- Consumes: Task 1 の `RenameStep.TrimWhitespace` / `CollapseWhitespace` / `RemoveDuplicateSuffix` フィールド、`ApplyStep` の switch
- Produces: `string BulkRenameOperations.ApplyTrimNormalize(string name, RenameStep step)`

- [ ] **Step 1: 失敗するテストを書く**

`RenamePipelineTests.cs` のクラス末尾に追記:

```csharp
        [Test]
        public void TrimNormalize_TrimsLeadingAndTrailingWhitespace()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.TrimNormalize, TrimWhitespace = true
            });

            Assert.AreEqual("Enemy", BulkRenameOperations.GenerateNewName("  Enemy  ", 0, pipeline));
        }

        [Test]
        public void TrimNormalize_CollapsesWhitespaceRuns()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.TrimNormalize,
                TrimWhitespace = false, CollapseWhitespace = true
            });

            Assert.AreEqual("Enemy Boss", BulkRenameOperations.GenerateNewName("Enemy   Boss", 0, pipeline));
        }

        [Test]
        public void TrimNormalize_RemovesDuplicateSuffix()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.TrimNormalize,
                TrimWhitespace = false, RemoveDuplicateSuffix = true
            });

            Assert.AreEqual("Enemy", BulkRenameOperations.GenerateNewName("Enemy (1)", 0, pipeline));
        }

        [Test]
        public void TrimNormalize_DuplicateSuffixOnlyStripsAtEnd()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.TrimNormalize,
                TrimWhitespace = false, RemoveDuplicateSuffix = true
            });

            Assert.AreEqual("Enemy (1) Boss", BulkRenameOperations.GenerateNewName("Enemy (1) Boss", 0, pipeline));
        }

        [Test]
        public void TrimNormalize_AllFlagsCombined()
        {
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.TrimNormalize,
                TrimWhitespace = true, CollapseWhitespace = true, RemoveDuplicateSuffix = true
            });

            Assert.AreEqual("Enemy Clone", BulkRenameOperations.GenerateNewName("  Enemy   Clone (3) ", 0, pipeline));
        }
```

- [ ] **Step 2: テストが失敗することを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 非 0。`ApplyStep` の default 分岐で名前が変換されず Assert 失敗(TrimNormalize の 5 テスト)。

- [ ] **Step 3: 実装する**

`ApplyStep` の switch に case を追加(`case RenameStepType.ChangeCase:` の後):

```csharp
                case RenameStepType.TrimNormalize:
                    return ApplyTrimNormalize(name, step);
```

クラス末尾に追加:

```csharp
        // Unity が複製時に付ける " (1)" 形式の末尾サフィックス
        private static readonly Regex DuplicateSuffixRegex = new Regex(@"\s*\(\d+\)\s*$", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRunRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        /// <summary>トリム / 空白正規化 / 重複サフィックス除去を適用する。順序: サフィックス除去 → 空白圧縮 → トリム。</summary>
        public static string ApplyTrimNormalize(string name, RenameStep step)
        {
            var result = name;

            if (step.RemoveDuplicateSuffix) result = DuplicateSuffixRegex.Replace(result, "");
            if (step.CollapseWhitespace) result = WhitespaceRunRegex.Replace(result, " ");
            if (step.TrimWhitespace) result = result.Trim();

            return result;
        }
```

- [ ] **Step 4: テストが通ることを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=54。

- [ ] **Step 5: コミット**

```powershell
git add Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs Assets/JustEnoughEditor/Tests/Editor/BulkRename/RenamePipelineTests.cs
git commit -m @'
トリム / 空白正規化 / 重複サフィックス除去ステップを追加

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 6: BulkRenameWindow 全面刷新

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/BulkRenameWindow.cs`(全置換)

**Interfaces:**
- Consumes: Task 1–5 の `RenamePipeline` / `RenameStep` / `RenameStepType` / `CaseConversion`、`BulkRenameOperations.GenerateNewName(string, int, RenamePipeline)`、`TryValidate`、`IsValidFileName`
- Produces: `BulkRenameWindow.ShowWindow()`(既存メニュー `JEEMenu.cs` / `JEEHierarchyActions.cs` から呼ばれる。シグネチャ不変のためメニュー側の変更は不要)

UI テストは書かない(EditorWindow は単体テスト対象外)。検証はコンパイル + 既存全テストのグリーン維持で行う。

- [ ] **Step 1: ウィンドウを全面的に書き換える**

`Assets/JustEnoughEditor/Editor/BulkRenameWindow.cs` の内容全体を以下に置き換える:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JustEnoughEditor.BulkRename;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JustEnoughEditor
{
    /// <summary>
    /// ステップチェーン方式の一括リネームウィンドウ。
    /// 選択中の GameObject / Asset に対して、複数のリネームステップを上から順に適用する。
    /// </summary>
    public class BulkRenameWindow : EditorWindow
    {
        private static readonly string[] StepTypeLabels =
        {
            "Find & Replace", "Add Prefix/Suffix", "Remove Characters",
            "Set Name", "Change Case", "Trim/Normalize"
        };

        /// <summary>プレビュー 1 行分の計算結果。</summary>
        private struct PreviewEntry
        {
            public Object Target;
            public string OldName;      // 表示用 (アセットは拡張子付き)
            public string NewName;      // 表示用 (アセットは拡張子付き)
            public string NewBaseName;  // 適用用 (拡張子なし)
            public bool Skip;
            public string SkipReason;
            public bool WillChange;
        }

        private readonly List<Object> targets = new List<Object>();
        private RenamePipeline pipeline = new RenamePipeline();
        private ReorderableList stepList;
        private Vector2 previewScroll;
        private bool lockTargets;
        private string lastResultMessage = "";
        private MessageType lastResultType = MessageType.Info;

        private GUIStyle changedStyle;
        private GUIStyle skippedStyle;
        private GUIStyle unchangedStyle;

        public static void ShowWindow()
        {
            var window = GetWindow<BulkRenameWindow>("Bulk Rename");
            window.RefreshFromSelection();
        }

        private void OnEnable()
        {
            if (pipeline.Steps.Count == 0)
                pipeline.Steps.Add(new RenameStep { Type = RenameStepType.FindReplace });

            BuildStepList();
            RefreshFromSelection();
        }

        private void OnSelectionChange()
        {
            if (!lockTargets) RefreshFromSelection();
        }

        private void RefreshFromSelection()
        {
            targets.Clear();
            targets.AddRange(Selection.objects);
            lastResultMessage = "";
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();

            DrawTargetBar();
            EditorGUILayout.Space();

            stepList.DoLayoutList();

            if (UsesNumberToken())
                DrawNumberingSection();

            var validationOk = BulkRenameOperations.TryValidate(pipeline, out var validationError);
            if (!validationOk)
                EditorGUILayout.HelpBox(validationError, MessageType.Error);

            EditorGUILayout.Space();
            var previews = validationOk ? ComputePreviews() : new List<PreviewEntry>();
            DrawPreview(previews);

            EditorGUILayout.Space();
            if (targets.Any(t => t != null && IsAsset(t)))
                EditorGUILayout.HelpBox("Renaming assets cannot be undone.", MessageType.Warning);

            var changeCount = previews.Count(p => !p.Skip && p.WillChange);
            using (new EditorGUI.DisabledScope(!validationOk || changeCount == 0))
            {
                if (GUILayout.Button($"Apply ({changeCount})", GUILayout.Height(28f)))
                    ApplyRename(previews);
            }

            if (!string.IsNullOrEmpty(lastResultMessage))
                EditorGUILayout.HelpBox(lastResultMessage, lastResultType);
        }

        private void EnsureStyles()
        {
            if (changedStyle != null) return;

            changedStyle = new GUIStyle(EditorStyles.label);
            changedStyle.normal.textColor = new Color(0.4f, 0.8f, 0.4f);
            skippedStyle = new GUIStyle(EditorStyles.label);
            skippedStyle.normal.textColor = new Color(0.95f, 0.45f, 0.45f);
            unchangedStyle = new GUIStyle(EditorStyles.label);
            unchangedStyle.normal.textColor = Color.gray;
        }

        // ---- ターゲットバー ----

        private void DrawTargetBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Targets: {targets.Count}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            var newLock = GUILayout.Toggle(lockTargets, "Lock Targets", "Button", GUILayout.Width(100f));
            if (newLock != lockTargets)
            {
                lockTargets = newLock;
                if (!lockTargets) RefreshFromSelection();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ---- ステップリスト ----

        private void BuildStepList()
        {
            stepList = new ReorderableList(pipeline.Steps, typeof(RenameStep), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Rename Steps (applied top to bottom)"),
                drawElementCallback = DrawStepElement,
                elementHeightCallback = GetStepHeight,
                onAddDropdownCallback = ShowAddStepMenu
            };
        }

        private static float Rows(int count)
        {
            return count * (EditorGUIUtility.singleLineHeight + 2f) + 6f;
        }

        private float GetStepHeight(int index)
        {
            switch (pipeline.Steps[index].Type)
            {
                case RenameStepType.FindReplace: return Rows(4);
                case RenameStepType.AddPrefixSuffix: return Rows(3);
                case RenameStepType.RemoveCharacters: return Rows(3);
                case RenameStepType.SetName: return Rows(2);
                case RenameStepType.ChangeCase: return Rows(2);
                case RenameStepType.TrimNormalize: return Rows(4);
                default: return Rows(1);
            }
        }

        private void ShowAddStepMenu(Rect buttonRect, ReorderableList list)
        {
            var menu = new GenericMenu();
            for (var i = 0; i < StepTypeLabels.Length; i++)
            {
                var type = (RenameStepType)i;
                menu.AddItem(new GUIContent(StepTypeLabels[i]), false, () =>
                {
                    pipeline.Steps.Add(new RenameStep { Type = type });
                    Repaint();
                });
            }
            menu.DropDown(buttonRect);
        }

        private static Rect NextRow(Rect row)
        {
            return new Rect(row.x, row.y + EditorGUIUtility.singleLineHeight + 2f, row.width, EditorGUIUtility.singleLineHeight);
        }

        private void DrawStepElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var step = pipeline.Steps[index];
            var line = EditorGUIUtility.singleLineHeight;
            var row = new Rect(rect.x, rect.y + 3f, rect.width, line);

            var toggleRect = new Rect(row.x, row.y, 18f, line);
            step.Enabled = EditorGUI.Toggle(toggleRect, step.Enabled);

            var typeRect = new Rect(row.x + 22f, row.y, row.width - 22f, line);
            step.Type = (RenameStepType)EditorGUI.Popup(typeRect, (int)step.Type, StepTypeLabels);

            using (new EditorGUI.DisabledScope(!step.Enabled))
            {
                switch (step.Type)
                {
                    case RenameStepType.FindReplace:
                        row = NextRow(row);
                        step.FindText = EditorGUI.TextField(row, "Find", step.FindText);
                        row = NextRow(row);
                        step.ReplaceText = EditorGUI.TextField(row, "Replace", step.ReplaceText);
                        row = NextRow(row);
                        var half = new Rect(row.x, row.y, row.width / 2f, line);
                        step.UseRegex = EditorGUI.ToggleLeft(half, "Regex", step.UseRegex);
                        half.x += row.width / 2f;
                        step.CaseSensitive = EditorGUI.ToggleLeft(half, "Case Sensitive", step.CaseSensitive);
                        break;

                    case RenameStepType.AddPrefixSuffix:
                        row = NextRow(row);
                        step.AddPrefix = EditorGUI.TextField(row, "Prefix", step.AddPrefix);
                        row = NextRow(row);
                        step.AddSuffix = EditorGUI.TextField(row, "Suffix", step.AddSuffix);
                        break;

                    case RenameStepType.RemoveCharacters:
                        row = NextRow(row);
                        step.RemoveFromStart = Mathf.Max(0, EditorGUI.IntField(row, "From Start", step.RemoveFromStart));
                        row = NextRow(row);
                        step.RemoveFromEnd = Mathf.Max(0, EditorGUI.IntField(row, "From End", step.RemoveFromEnd));
                        break;

                    case RenameStepType.SetName:
                        row = NextRow(row);
                        step.NamePattern = EditorGUI.TextField(row, "Name Pattern", step.NamePattern);
                        break;

                    case RenameStepType.ChangeCase:
                        row = NextRow(row);
                        step.CaseConversion = (CaseConversion)EditorGUI.EnumPopup(row, "Convert To", step.CaseConversion);
                        break;

                    case RenameStepType.TrimNormalize:
                        row = NextRow(row);
                        step.TrimWhitespace = EditorGUI.ToggleLeft(row, "Trim Whitespace", step.TrimWhitespace);
                        row = NextRow(row);
                        step.CollapseWhitespace = EditorGUI.ToggleLeft(row, "Collapse Whitespace", step.CollapseWhitespace);
                        row = NextRow(row);
                        step.RemoveDuplicateSuffix = EditorGUI.ToggleLeft(row, "Remove \" (1)\" Suffix", step.RemoveDuplicateSuffix);
                        break;
                }
            }
        }

        // ---- Numbering ----

        private bool UsesNumberToken()
        {
            foreach (var step in pipeline.Steps)
            {
                if (step == null || !step.Enabled) continue;
                if ((step.ReplaceText ?? "").Contains("<num>")) return true;
                if ((step.AddPrefix ?? "").Contains("<num>")) return true;
                if ((step.AddSuffix ?? "").Contains("<num>")) return true;
                if ((step.NamePattern ?? "").Contains("<num>")) return true;
            }
            return false;
        }

        private void DrawNumberingSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Numbering (<num>)", EditorStyles.boldLabel);
            pipeline.StartNumber = Mathf.Max(0, EditorGUILayout.IntField("Start Number", pipeline.StartNumber));
            pipeline.Digits = Mathf.Clamp(EditorGUILayout.IntField("Digits", pipeline.Digits), 1, 10);
        }

        // ---- プレビュー ----

        private List<PreviewEntry> ComputePreviews()
        {
            var entries = new List<PreviewEntry>(targets.Count);

            for (var i = 0; i < targets.Count; i++)
            {
                var obj = targets[i];
                if (obj == null) continue;

                var entry = new PreviewEntry { Target = obj };
                try
                {
                    if (IsAsset(obj)) FillAssetPreview(ref entry, obj, i);
                    else FillGameObjectPreview(ref entry, obj, i);
                }
                catch (Exception e)
                {
                    // 正規表現のタイムアウト等。行単位でスキップ扱いにする
                    entry.OldName = obj.name;
                    entry.NewName = obj.name;
                    entry.Skip = true;
                    entry.SkipReason = e.Message;
                }

                entries.Add(entry);
            }

            return entries;
        }

        private void FillAssetPreview(ref PreviewEntry entry, Object obj, int index)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            var extension = Path.GetExtension(path);
            var baseName = Path.GetFileNameWithoutExtension(path);
            var newBase = BulkRenameOperations.GenerateNewName(baseName, index, pipeline);

            entry.OldName = baseName + extension;
            entry.NewName = newBase + extension;
            entry.NewBaseName = newBase;
            entry.WillChange = !string.Equals(newBase, baseName, StringComparison.Ordinal);

            if (string.IsNullOrEmpty(newBase))
            {
                entry.Skip = true;
                entry.SkipReason = "Name is empty";
                return;
            }

            if (!BulkRenameOperations.IsValidFileName(newBase))
            {
                entry.Skip = true;
                entry.SkipReason = "Contains invalid characters";
                return;
            }

            var directory = Path.GetDirectoryName(path) ?? "";
            var newPath = (directory.Length > 0 ? directory + "/" : "") + newBase + extension;
            newPath = newPath.Replace('\\', '/');

            if (!string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
            {
                entry.Skip = true;
                entry.SkipReason = "A file with this name already exists";
            }
        }

        private void FillGameObjectPreview(ref PreviewEntry entry, Object obj, int index)
        {
            var newName = BulkRenameOperations.GenerateNewName(obj.name, index, pipeline);

            entry.OldName = obj.name;
            entry.NewName = newName;
            entry.NewBaseName = newName;
            entry.WillChange = !string.Equals(newName, obj.name, StringComparison.Ordinal);

            if (string.IsNullOrEmpty(newName))
            {
                entry.Skip = true;
                entry.SkipReason = "Name is empty";
            }
        }

        private void DrawPreview(List<PreviewEntry> previews)
        {
            var changeCount = previews.Count(p => !p.Skip && p.WillChange);
            var skipCount = previews.Count(p => p.Skip);
            EditorGUILayout.LabelField($"Preview — {changeCount} will change, {skipCount} skipped", EditorStyles.boldLabel);

            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.MinHeight(120f), GUILayout.ExpandHeight(true));

            var halfWidth = Mathf.Max(120f, (position.width - 60f) / 2f);
            foreach (var p in previews)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent(p.OldName, p.OldName), GUILayout.Width(halfWidth));
                EditorGUILayout.LabelField("→", GUILayout.Width(20f));

                if (p.Skip)
                    EditorGUILayout.LabelField(new GUIContent($"(skip: {p.SkipReason})", p.SkipReason), skippedStyle);
                else if (p.WillChange)
                    EditorGUILayout.LabelField(new GUIContent(p.NewName, p.NewName), changedStyle);
                else
                    EditorGUILayout.LabelField(new GUIContent(p.NewName, p.NewName), unchangedStyle);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // ---- 適用 ----

        private static bool IsAsset(Object obj)
        {
            return !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj));
        }

        private void ApplyRename(List<PreviewEntry> previews)
        {
            var successCount = 0;
            var skipCount = 0;
            var errors = new List<string>();

            foreach (var entry in previews)
            {
                if (entry.Skip || !entry.WillChange)
                {
                    if (entry.Skip) skipCount++;
                    continue;
                }

                if (IsAsset(entry.Target))
                {
                    var path = AssetDatabase.GetAssetPath(entry.Target);
                    var error = AssetDatabase.RenameAsset(path, entry.NewBaseName);
                    if (!string.IsNullOrEmpty(error))
                    {
                        skipCount++;
                        errors.Add($"{entry.OldName}: {error}");
                        continue;
                    }
                }
                else
                {
                    Undo.RecordObject(entry.Target, "Bulk Rename");
                    entry.Target.name = entry.NewBaseName;
                }

                successCount++;
            }

            AssetDatabase.Refresh();

            lastResultMessage = $"{successCount} renamed, {skipCount} skipped";
            if (errors.Count > 0)
                lastResultMessage += "\n" + string.Join("\n", errors.Take(5));
            lastResultType = errors.Count > 0 ? MessageType.Warning : MessageType.Info;

            if (!lockTargets) RefreshFromSelection();
        }
    }
}
```

- [ ] **Step 2: コンパイルと回帰を確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=54(コンパイル成功 + 回帰なし)。

- [ ] **Step 3: コミット**

```powershell
git add Assets/JustEnoughEditor/Editor/BulkRenameWindow.cs
git commit -m @'
BulkRenameWindow をステップチェーン方式に全面刷新

- 選択の自動追従と Lock Targets トグル
- ReorderableList によるステップの追加/削除/並べ替え/有効無効
- プレビューの全幅表示・色分け・スキップ理由のインライン表示
- 変更件数の集計表示と Apply ボタンの適切な無効化

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 7: キーボードショートカットと設定永続化

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/BulkRenameWindow.cs`

**Interfaces:**
- Consumes: Task 6 の `BulkRenameWindow`(`OnEnable` / `OnGUI` / `ApplyRename` / `ComputePreviews` / `pipeline` フィールド)
- Produces: ShortcutManager 登録 ID `"JEE/Bulk Rename"`(デフォルト Alt+R)、EditorPrefs キー `"JEE_BulkRenamePipeline"`

- [ ] **Step 1: ショートカット・キーボード操作・永続化を追加する**

`BulkRenameWindow.cs` の using に追加:

```csharp
using UnityEditor.ShortcutManagement;
```

クラス内の `ShowWindow()` の直後に追加:

```csharp
        /// <summary>ShortcutManager 経由のウィンドウオープン。Edit &gt; Shortcuts の "JEE/Bulk Rename" でリバインド可能。</summary>
        [Shortcut("JEE/Bulk Rename", KeyCode.R, ShortcutModifiers.Alt)]
        private static void OpenViaShortcut()
        {
            ShowWindow();
        }
```

フィールド宣言部(`private GUIStyle unchangedStyle;` の下)に追加:

```csharp
        private const string PipelinePrefsKey = "JEE_BulkRenamePipeline";
```

`OnEnable()` を以下に置き換え(先頭で復元してからリスト構築):

```csharp
        private void OnEnable()
        {
            LoadPipeline();

            if (pipeline.Steps.Count == 0)
                pipeline.Steps.Add(new RenameStep { Type = RenameStepType.FindReplace });

            BuildStepList();
            RefreshFromSelection();
        }
```

`OnEnable()` の直後に追加:

```csharp
        private void OnDisable()
        {
            SavePipeline();
        }

        private void LoadPipeline()
        {
            var json = EditorPrefs.GetString(PipelinePrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                pipeline = JsonUtility.FromJson<RenamePipeline>(json) ?? new RenamePipeline();
            }
            catch (Exception)
            {
                pipeline = new RenamePipeline();
            }
        }

        private void SavePipeline()
        {
            EditorPrefs.SetString(PipelinePrefsKey, JsonUtility.ToJson(pipeline));
        }
```

`OnGUI()` の先頭(`EnsureStyles();` の直前)に追加:

```csharp
            HandleKeyboard();
```

クラス内(`OnGUI` の直後)に追加:

```csharp
        /// <summary>Enter で Apply、Esc でウィンドウを閉じる。テキスト編集中はどちらも無効。</summary>
        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return;

            if (e.keyCode == KeyCode.Escape)
            {
                e.Use();
                Close();
                GUIUtility.ExitGUI();
                return;
            }

            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            if (!BulkRenameOperations.TryValidate(pipeline, out _)) return;

            var previews = ComputePreviews();
            if (previews.Any(p => !p.Skip && p.WillChange))
            {
                e.Use();
                ApplyRename(previews);
            }
        }
```

- [ ] **Step 2: コンパイルと回帰を確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=54。

- [ ] **Step 3: コミット**

```powershell
git add Assets/JustEnoughEditor/Editor/BulkRenameWindow.cs
git commit -m @'
Bulk Rename にショートカット (Alt+R) と Enter/Esc 操作、設定の永続化を追加

- ShortcutManager 登録によりユーザーがリバインド可能
- Enter で Apply、Esc でウィンドウを閉じる (テキスト編集中は無効)
- ステップ構成を EditorPrefs (JEE_BulkRenamePipeline) に保存・復元

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 8: 旧 API の削除と最終確認

**Files:**
- Modify: `Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs`(旧 API 削除)
- Delete: `Assets/JustEnoughEditor/Tests/Editor/BulkRename/BulkRenameOperationsTests.cs`(+ `.meta`)

**Interfaces:**
- Consumes: Task 1–7 完了後の状態(この時点で旧 API を参照するのは旧テストファイルのみ)
- Produces: 旧 `RenameMode` / `NumberPosition` / `BulkRenameSettings` および旧モード系メソッドが存在しない最終形

- [ ] **Step 1: 旧 API 参照が旧テスト以外に残っていないことを確認する**

```powershell
git grep -n -E "RenameMode|NumberPosition|BulkRenameSettings" -- "Assets/"
```

Expected: ヒットは `BulkRenameOperations.cs`(定義)と `BulkRenameOperationsTests.cs`(旧テスト)のみ。他のファイルにヒットがある場合は先にそちらを新 API へ移行する(想定外なので原因を調査すること)。

- [ ] **Step 2: 旧テストファイルを削除する**

```powershell
git rm Assets/JustEnoughEditor/Tests/Editor/BulkRename/BulkRenameOperationsTests.cs Assets/JustEnoughEditor/Tests/Editor/BulkRename/BulkRenameOperationsTests.cs.meta
```

- [ ] **Step 3: 旧 API を削除する**

`Assets/JustEnoughEditor/Editor/BulkRename/BulkRenameOperations.cs` の内容全体を以下に置き換える(新 API のみの最終形。Task 2–5 の実装がすべて含まれていることを確認しながら置き換えること):

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace JustEnoughEditor.BulkRename
{
    /// <summary>
    /// 一括リネームの文字列変換ロジックを提供する静的クラス。
    /// UnityEngine/UnityEditor に依存しない純粋な C# ロジックとして実装され、NUnit で単体テスト可能。
    /// </summary>
    public static class BulkRenameOperations
    {
        /// <summary>パイプラインの全ステップを順に適用した新しい名前を返す。Enabled=false のステップはスキップする。</summary>
        public static string GenerateNewName(string currentName, int index, RenamePipeline pipeline)
        {
            var name = currentName;

            foreach (var step in pipeline.Steps)
            {
                if (step == null || !step.Enabled) continue;
                name = ApplyStep(name, currentName, index, step, pipeline);
            }

            return name;
        }

        /// <summary>1 ステップ分の変換を適用する。originalName はパイプライン入力時点の名前(トークン &lt;name&gt; の展開に使用)。</summary>
        public static string ApplyStep(string name, string originalName, int index, RenameStep step, RenamePipeline pipeline)
        {
            switch (step.Type)
            {
                case RenameStepType.FindReplace:
                    return ApplyFindReplaceStep(name, originalName, index, step, pipeline);
                case RenameStepType.AddPrefixSuffix:
                    return ExpandTokens(step.AddPrefix, originalName, index, pipeline)
                           + name
                           + ExpandTokens(step.AddSuffix, originalName, index, pipeline);
                case RenameStepType.RemoveCharacters:
                    return RemoveCharacters(name, step.RemoveFromStart, step.RemoveFromEnd);
                case RenameStepType.SetName:
                    return ExpandTokens(step.NamePattern, originalName, index, pipeline);
                case RenameStepType.ChangeCase:
                    return ConvertCase(name, step.CaseConversion);
                case RenameStepType.TrimNormalize:
                    return ApplyTrimNormalize(name, step);
                default:
                    return name;
            }
        }

        /// <summary>パイプライン全体を検証する。不正な正規表現パターンを検出したら最初のエラーを返す。</summary>
        public static bool TryValidate(RenamePipeline pipeline, out string error)
        {
            foreach (var step in pipeline.Steps)
            {
                if (step == null || !step.Enabled) continue;
                if (step.Type != RenameStepType.FindReplace || !step.UseRegex) continue;
                if (string.IsNullOrEmpty(step.FindText)) continue;

                try
                {
                    _ = new Regex(step.FindText);
                }
                catch (ArgumentException e)
                {
                    error = $"Invalid regex pattern \"{step.FindText}\": {e.Message}";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>&lt;name&gt;(パイプライン入力時点の名前)と &lt;num&gt;(連番)のトークンを展開する。</summary>
        public static string ExpandTokens(string text, string originalName, int index, RenamePipeline pipeline)
        {
            if (string.IsNullOrEmpty(text)) return "";

            return text
                .Replace("<name>", originalName)
                .Replace("<num>", FormatNumber(index, pipeline));
        }

        /// <summary>StartNumber + index を Digits 桁までゼロ埋めした連番文字列を返す。</summary>
        public static string FormatNumber(int index, RenamePipeline pipeline)
        {
            var number = pipeline.StartNumber + index;
            var digits = Math.Max(1, pipeline.Digits);
            return number.ToString().PadLeft(digits, '0');
        }

        /// <summary>名前を指定したケース形式に変換する。</summary>
        public static string ConvertCase(string name, CaseConversion conversion)
        {
            switch (conversion)
            {
                case CaseConversion.Uppercase:
                    return name.ToUpperInvariant();
                case CaseConversion.Lowercase:
                    return name.ToLowerInvariant();
            }

            var words = SplitWords(name);
            switch (conversion)
            {
                case CaseConversion.PascalCase:
                    return string.Concat(words.Select(Capitalize));
                case CaseConversion.CamelCase:
                    var pascal = string.Concat(words.Select(Capitalize));
                    return pascal.Length > 0 ? char.ToLowerInvariant(pascal[0]) + pascal.Substring(1) : pascal;
                case CaseConversion.SnakeCase:
                    return string.Join("_", words.Select(w => w.ToLowerInvariant()));
                case CaseConversion.KebabCase:
                    return string.Join("-", words.Select(w => w.ToLowerInvariant()));
                default:
                    return name;
            }
        }

        // Unity が複製時に付ける " (1)" 形式の末尾サフィックス
        private static readonly Regex DuplicateSuffixRegex = new Regex(@"\s*\(\d+\)\s*$", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRunRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        /// <summary>トリム / 空白正規化 / 重複サフィックス除去を適用する。順序: サフィックス除去 → 空白圧縮 → トリム。</summary>
        public static string ApplyTrimNormalize(string name, RenameStep step)
        {
            var result = name;

            if (step.RemoveDuplicateSuffix) result = DuplicateSuffixRegex.Replace(result, "");
            if (step.CollapseWhitespace) result = WhitespaceRunRegex.Replace(result, " ");
            if (step.TrimWhitespace) result = result.Trim();

            return result;
        }

        /// <summary>ファイル名として使用できない文字を含んでいないかを検証する(空文字列は無効とみなす)。</summary>
        public static bool IsValidFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            var invalidChars = Path.GetInvalidFileNameChars();
            return name.IndexOfAny(invalidChars) < 0;
        }

        private static string ApplyFindReplaceStep(string name, string originalName, int index, RenameStep step, RenamePipeline pipeline)
        {
            if (string.IsNullOrEmpty(step.FindText)) return name;

            var replace = ExpandTokens(step.ReplaceText, originalName, index, pipeline);

            if (step.UseRegex)
            {
                var options = step.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                // 破滅的バックトラッキングによるエディタのフリーズを防ぐため 1 秒でタイムアウトさせる
                return Regex.Replace(name, step.FindText, replace, options, TimeSpan.FromSeconds(1));
            }

            if (step.CaseSensitive) return name.Replace(step.FindText, replace);
            return ReplaceCaseInsensitive(name, step.FindText, replace);
        }

        private static string ReplaceCaseInsensitive(string source, string find, string replace)
        {
            var result = new System.Text.StringBuilder();
            var searchStart = 0;

            while (true)
            {
                var index = source.IndexOf(find, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    result.Append(source, searchStart, source.Length - searchStart);
                    break;
                }

                result.Append(source, searchStart, index - searchStart);
                result.Append(replace);
                searchStart = index + find.Length;
            }

            return result.ToString();
        }

        private static string RemoveCharacters(string name, int fromStart, int fromEnd)
        {
            var removeStart = Math.Max(0, Math.Min(fromStart, name.Length));
            name = name.Substring(removeStart);

            var removeEnd = Math.Max(0, Math.Min(fromEnd, name.Length));
            return name.Substring(0, name.Length - removeEnd);
        }

        private static string Capitalize(string word)
        {
            if (word.Length == 0) return word;
            return char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
        }

        /// <summary>空白・アンダースコア・ハイフン、および小文字→大文字の境界で単語に分割する。</summary>
        private static string[] SplitWords(string name)
        {
            var words = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var c in name)
            {
                if (c == ' ' || c == '_' || c == '-')
                {
                    if (current.Length > 0)
                    {
                        words.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                if (char.IsUpper(c) && current.Length > 0 && char.IsLower(current[current.Length - 1]))
                {
                    words.Add(current.ToString());
                    current.Clear();
                }

                current.Append(c);
            }

            if (current.Length > 0) words.Add(current.ToString());
            return words.ToArray();
        }
    }
}
```

- [ ] **Step 4: 全テストが通ることを確認する**

STANDARD TEST RUN を実行。
Expected: ExitCode 0、result="Passed"、testcasecount=35(新テスト 35 のみ。旧 19 テストは削除済み)。

- [ ] **Step 5: コミット**

```powershell
git add -A Assets/JustEnoughEditor/Editor/BulkRename/ Assets/JustEnoughEditor/Tests/Editor/BulkRename/
git commit -m @'
旧モード方式 API (RenameMode / BulkRenameSettings) を削除

ステップチェーン方式 (RenamePipeline) への移行が完了したため、
旧 API とそのテストを削除して最終形にする。

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
