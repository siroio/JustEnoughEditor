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
