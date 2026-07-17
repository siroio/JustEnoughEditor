using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace JustEnoughEditor.BulkRename
{
    /// <summary>一括リネームの方式。</summary>
    public enum RenameMode
    {
        FindReplace,
        AddPrefixSuffix,
        SequentialNumber,
        ReplaceAll
    }

    /// <summary>連番を名前のどちら側に付与するか。</summary>
    public enum NumberPosition
    {
        Prefix,
        Suffix
    }

    /// <summary>一括リネームの各モードで使用するパラメータをまとめた設定クラス。</summary>
    [Serializable]
    public class BulkRenameSettings
    {
        public RenameMode Mode = RenameMode.FindReplace;

        // FindReplace
        public string FindText = "";
        public string ReplaceText = "";
        public bool CaseSensitive = true;

        // AddPrefixSuffix
        public string AddPrefix = "";
        public string AddSuffix = "";
        public int RemoveFromStart = 0;
        public int RemoveFromEnd = 0;

        // SequentialNumber (shared with ReplaceAll.AppendNumber)
        public int StartNumber = 1;
        public int Digits = 2;
        public NumberPosition NumberPosition = NumberPosition.Suffix;
        public string NumberSeparator = "_";

        // ReplaceAll
        public string NewBaseName = "";
        public bool AppendNumber = false;
    }

    /// <summary>
    /// 一括リネームの文字列変換ロジックを提供する静的クラス。
    /// UnityEngine/UnityEditor に依存しない純粋な C# ロジックとして実装され、NUnit で単体テスト可能。
    /// </summary>
    public static class BulkRenameOperations
    {
        /// <summary>現在の名前と対象内でのインデックスから、設定されたモードに応じた新しい名前を生成する。</summary>
        public static string GenerateNewName(string currentName, int index, BulkRenameSettings settings)
        {
            switch (settings.Mode)
            {
                case RenameMode.FindReplace:
                    return ApplyFindReplace(currentName, settings);
                case RenameMode.AddPrefixSuffix:
                    return ApplyAddPrefixSuffix(currentName, settings);
                case RenameMode.SequentialNumber:
                    return ApplySequentialNumber(currentName, index, settings);
                case RenameMode.ReplaceAll:
                    return ApplyReplaceAll(index, settings);
                default:
                    return currentName;
            }
        }

        /// <summary>検索/置換モード: FindText を ReplaceText に置き換える(CaseSensitive で大文字小文字の区別を制御)。</summary>
        public static string ApplyFindReplace(string currentName, BulkRenameSettings settings)
        {
            if (string.IsNullOrEmpty(settings.FindText)) return currentName;

            if (settings.CaseSensitive)
                return currentName.Replace(settings.FindText, settings.ReplaceText ?? "");

            return ReplaceCaseInsensitive(currentName, settings.FindText, settings.ReplaceText ?? "");
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

        /// <summary>接頭辞/接尾辞モード: 先頭・末尾から指定文字数を削除した後、接頭辞・接尾辞を追加する。</summary>
        public static string ApplyAddPrefixSuffix(string currentName, BulkRenameSettings settings)
        {
            var name = currentName;

            var removeStart = Math.Max(0, Math.Min(settings.RemoveFromStart, name.Length));
            name = name.Substring(removeStart);

            var removeEnd = Math.Max(0, Math.Min(settings.RemoveFromEnd, name.Length));
            name = name.Substring(0, name.Length - removeEnd);

            return (settings.AddPrefix ?? "") + name + (settings.AddSuffix ?? "");
        }

        /// <summary>連番付与モード: 既存の名前を維持したまま、指定位置に連番を付与する。</summary>
        public static string ApplySequentialNumber(string currentName, int index, BulkRenameSettings settings)
        {
            return PositionNumber(currentName, index, settings);
        }

        /// <summary>StartNumber を起点にインデックスを加算し、Digits 桁までゼロ埋めした連番文字列を返す。</summary>
        public static string FormatNumber(int index, BulkRenameSettings settings)
        {
            var number = settings.StartNumber + index;
            var digits = Math.Max(1, settings.Digits);
            return number.ToString().PadLeft(digits, '0');
        }

        private static string PositionNumber(string name, int index, BulkRenameSettings settings)
        {
            var number = FormatNumber(index, settings);
            return settings.NumberPosition == NumberPosition.Prefix
                ? number + settings.NumberSeparator + name
                : name + settings.NumberSeparator + number;
        }

        /// <summary>全体を新しい名前に置き換えるモード: NewBaseName に置き換え、AppendNumber が true なら連番も付与する。</summary>
        public static string ApplyReplaceAll(int index, BulkRenameSettings settings)
        {
            var baseName = settings.NewBaseName ?? "";
            if (!settings.AppendNumber) return baseName;

            return PositionNumber(baseName, index, settings);
        }

        /// <summary>ファイル名として使用できない文字を含んでいないかを検証する(空文字列は無効とみなす)。</summary>
        public static bool IsValidFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            var invalidChars = Path.GetInvalidFileNameChars();
            return name.IndexOfAny(invalidChars) < 0;
        }

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
                default:
                    return name;
            }
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

        private static string RemoveCharacters(string name, int fromStart, int fromEnd)
        {
            var removeStart = Math.Max(0, Math.Min(fromStart, name.Length));
            name = name.Substring(removeStart);

            var removeEnd = Math.Max(0, Math.Min(fromEnd, name.Length));
            return name.Substring(0, name.Length - removeEnd);
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
    }
}
