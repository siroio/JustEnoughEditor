using NUnit.Framework;
using JustEnoughEditor.BulkRename;

namespace JustEnoughEditor.BulkRename.Tests
{
    public class BulkRenameOperationsTests
    {
        [Test]
        public void FormatNumber_PadsWithZerosToDigitCount()
        {
            var settings = new BulkRenameSettings { StartNumber = 1, Digits = 3 };

            Assert.AreEqual("001", BulkRenameOperations.FormatNumber(0, settings));
            Assert.AreEqual("002", BulkRenameOperations.FormatNumber(1, settings));
        }

        [Test]
        public void FormatNumber_UsesStartNumberAsOffset()
        {
            var settings = new BulkRenameSettings { StartNumber = 10, Digits = 2 };

            Assert.AreEqual("10", BulkRenameOperations.FormatNumber(0, settings));
            Assert.AreEqual("11", BulkRenameOperations.FormatNumber(1, settings));
        }

        [Test]
        public void FormatNumber_DoesNotTruncateWhenNumberExceedsDigits()
        {
            var settings = new BulkRenameSettings { StartNumber = 99, Digits = 1 };

            Assert.AreEqual("100", BulkRenameOperations.FormatNumber(1, settings));
        }

        [Test]
        public void ApplyFindReplace_ReplacesAllOccurrences()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.FindReplace,
                FindText = "Old",
                ReplaceText = "New",
                CaseSensitive = true
            };

            Assert.AreEqual("New_New_Thing", BulkRenameOperations.GenerateNewName("Old_Old_Thing", 0, settings));
        }

        [Test]
        public void ApplyFindReplace_IsCaseSensitiveByDefault()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.FindReplace,
                FindText = "old",
                ReplaceText = "new",
                CaseSensitive = true
            };

            Assert.AreEqual("Old_Thing", BulkRenameOperations.GenerateNewName("Old_Thing", 0, settings));
        }

        [Test]
        public void ApplyFindReplace_CanIgnoreCase()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.FindReplace,
                FindText = "old",
                ReplaceText = "new",
                CaseSensitive = false
            };

            Assert.AreEqual("new_new_Thing", BulkRenameOperations.GenerateNewName("Old_OLD_Thing", 0, settings));
        }

        [Test]
        public void ApplyFindReplace_EmptyFindTextReturnsNameUnchanged()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.FindReplace,
                FindText = "",
                ReplaceText = "new"
            };

            Assert.AreEqual("Thing", BulkRenameOperations.GenerateNewName("Thing", 0, settings));
        }

        [Test]
        public void ApplyAddPrefixSuffix_AddsPrefixAndSuffix()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.AddPrefixSuffix,
                AddPrefix = "FX_",
                AddSuffix = "_v2"
            };

            Assert.AreEqual("FX_Enemy_v2", BulkRenameOperations.GenerateNewName("Enemy", 0, settings));
        }

        [Test]
        public void ApplyAddPrefixSuffix_RemovesCharsFromStartAndEnd()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.AddPrefixSuffix,
                RemoveFromStart = 3,
                RemoveFromEnd = 2
            };

            Assert.AreEqual("my", BulkRenameOperations.GenerateNewName("OLDmyXX", 0, settings));
        }

        [Test]
        public void ApplyAddPrefixSuffix_RemovalHappensBeforeAdding()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.AddPrefixSuffix,
                RemoveFromStart = 4,
                AddPrefix = "New_"
            };

            Assert.AreEqual("New_Enemy", BulkRenameOperations.GenerateNewName("Old_Enemy", 0, settings));
        }

        [Test]
        public void ApplyAddPrefixSuffix_ClampsRemovalCountsToNameLength()
        {
            var settings = new BulkRenameSettings
            {
                Mode = RenameMode.AddPrefixSuffix,
                RemoveFromStart = 100,
                RemoveFromEnd = 100
            };

            Assert.AreEqual("", BulkRenameOperations.GenerateNewName("Short", 0, settings));
        }
    }
}
