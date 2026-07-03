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
            return currentName;
        }

        public static string FormatNumber(int index, BulkRenameSettings settings)
        {
            var number = settings.StartNumber + index;
            var digits = Math.Max(1, settings.Digits);
            return number.ToString().PadLeft(digits, '0');
        }
    }
}
