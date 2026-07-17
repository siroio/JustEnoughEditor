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
