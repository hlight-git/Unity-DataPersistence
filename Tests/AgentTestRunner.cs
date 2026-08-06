using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Hlight.DataPersistence.Tests
{
    /// Temporary agent-only runner — MCP can invoke a menu item but not TestRunnerApi.
    /// Mirrors Packages/com.hlight.debug-hub/Tests/Editor/AgentTestRunner.cs.
    public static class AgentTestRunner
    {
        private const string RESULT_PATH = "Temp/data-persistence-tests.txt";
        private const string ASSEMBLY    = "Hlight.DataPersistence.Tests";

        // Must stay static: TestRunnerApi is a ScriptableObject; if it is collected the
        // registered callback dies and RunFinished never fires.
        private static TestRunnerApi api;

        public static void Run()
        {
            if (File.Exists(RESULT_PATH)) File.Delete(RESULT_PATH);
            if (File.Exists(RESULT_PATH + ".started")) File.Delete(RESULT_PATH + ".started");

            if (api == null) api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new ResultWriter());
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode      = TestMode.EditMode,
                assemblyNames = new[] { ASSEMBLY }
            }));
        }

        private class ResultWriter : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
                => File.WriteAllText(RESULT_PATH + ".started", testsToRun.TestCaseCount.ToString());

            public void RunFinished(ITestResultAdaptor testResults)
            {
                var builder = new StringBuilder();
                builder.Append("PASS=").Append(testResults.PassCount)
                       .Append(" FAIL=").Append(testResults.FailCount)
                       .Append(" SKIP=").Append(testResults.SkipCount).Append('\n');
                Collect(testResults, builder);
                File.WriteAllText(RESULT_PATH, builder.ToString());
            }

            private static void Collect(ITestResultAdaptor node, StringBuilder builder)
            {
                if (!node.HasChildren)
                {
                    builder.Append(node.TestStatus).Append(' ').Append(node.FullName).Append('\n');
                    if (node.TestStatus == TestStatus.Failed)
                    {
                        builder.Append("  ").Append(node.Message).Append('\n');
                        builder.Append("  ").Append(node.StackTrace).Append('\n');
                    }
                }

                if (node.Children == null) return;
                foreach (var child in node.Children) Collect(child, builder);
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
