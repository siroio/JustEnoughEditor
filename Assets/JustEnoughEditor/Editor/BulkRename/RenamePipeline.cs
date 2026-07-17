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
