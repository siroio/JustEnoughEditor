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
    }
}
