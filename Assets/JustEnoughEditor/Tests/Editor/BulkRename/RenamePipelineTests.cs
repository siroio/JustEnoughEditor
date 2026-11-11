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
        public void FindDuplicateIndexes_ReturnsEveryCaseInsensitiveCollision()
        {
            var duplicates = BulkRenameOperations.FindDuplicateIndexes(new[]
            {
                "Assets/Props/Wall.prefab",
                "Assets/Props/wall.prefab",
                "Assets/Props/Door.prefab",
                "Assets/Props/WALL.prefab"
            });

            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, duplicates);
        }

        [Test]
        public void FindDuplicateIndexes_IgnoresNullCandidates()
        {
            var duplicates = BulkRenameOperations.FindDuplicateIndexes(new string[] { null, null, "Door.prefab" });

            CollectionAssert.IsEmpty(duplicates);
        }

        [Test]
        public void IsValidFileName_RejectsWindowsReservedDeviceNames()
        {
            Assert.IsFalse(BulkRenameOperations.IsValidFileName("CON"));
            Assert.IsFalse(BulkRenameOperations.IsValidFileName("lpt1.backup"));
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
        public void Regex_NameTokenWithDollarIsNotTreatedAsGroupReference()
        {
            // <name> の展開値に $ が含まれていても、Regex.Replace のグループ参照として誤解釈されないことを確認する
            var pipeline = PipelineWith(new RenameStep
            {
                Type = RenameStepType.FindReplace, UseRegex = true,
                FindText = "(Enemy).*", ReplaceText = "<name>_ok"
            });

            Assert.AreEqual("Enemy$1_ok", BulkRenameOperations.GenerateNewName("Enemy$1", 0, pipeline));
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
    }
}
