using System;

namespace JustEnoughEditor.BulkRename
{
    public enum RenameMode
    {
        FindReplace,
        AddPrefixSuffix,
        SequentialNumber,
        ReplaceAll
    }

    public enum NumberPosition
    {
        Prefix,
        Suffix
    }

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

    public static class BulkRenameOperations
    {
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

        public static string ApplyAddPrefixSuffix(string currentName, BulkRenameSettings settings)
        {
            var name = currentName;

            var removeStart = Math.Max(0, Math.Min(settings.RemoveFromStart, name.Length));
            name = name.Substring(removeStart);

            var removeEnd = Math.Max(0, Math.Min(settings.RemoveFromEnd, name.Length));
            name = name.Substring(0, name.Length - removeEnd);

            return (settings.AddPrefix ?? "") + name + (settings.AddSuffix ?? "");
        }

        public static string ApplySequentialNumber(string currentName, int index, BulkRenameSettings settings)
        {
            var number = FormatNumber(index, settings);
            return settings.NumberPosition == NumberPosition.Prefix
                ? number + settings.NumberSeparator + currentName
                : currentName + settings.NumberSeparator + number;
        }

        public static string FormatNumber(int index, BulkRenameSettings settings)
        {
            var number = settings.StartNumber + index;
            var digits = Math.Max(1, settings.Digits);
            return number.ToString().PadLeft(digits, '0');
        }

        public static string ApplyReplaceAll(int index, BulkRenameSettings settings)
        {
            var baseName = settings.NewBaseName ?? "";
            if (!settings.AppendNumber) return baseName;

            var number = FormatNumber(index, settings);
            return settings.NumberPosition == NumberPosition.Prefix
                ? number + settings.NumberSeparator + baseName
                : baseName + settings.NumberSeparator + number;
        }
    }
}
