using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>只监听测试结果，不启动测试；所有运行范围均覆盖导出本轮完整结果。</summary>
[InitializeOnLoad]
internal static class TestResultsAutoExporter
{
    // 相对仓库根目录，不绑定盘符、机器或检出目录名称。
    internal const string RelativeReportPath = "codex_worktable/TestResults.xml";
    private static readonly ResultsCallbacks Callbacks = new ResultsCallbacks();
    private static TestRunnerApi api;

    static TestResultsAutoExporter()
    {
        api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.hideFlags = HideFlags.HideAndDontSave;
        api.RegisterCallbacks(Callbacks);
        AssemblyReloadEvents.beforeAssemblyReload += Unregister;
        EditorApplication.quitting += Unregister;
    }

    private static void Unregister()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
        EditorApplication.quitting -= Unregister;
        if (api == null) return;
        api.UnregisterCallbacks(Callbacks);
        UnityEngine.Object.DestroyImmediate(api);
        api = null;
    }

    private sealed class ResultsCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            try
            {
                Save(result);
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                    "[测试结果] 已保存本轮结果到 {0}：通过 {1}，失败 {2}，跳过 {3}，未定 {4}。",
                    RelativeReportPath, result.PassCount, result.FailCount,
                    result.SkipCount, result.InconclusiveCount);
            }
            catch (Exception error)
            {
                // 报告保存失败不能成为新的测试失败；保留明确告警供人工处理。
                Debug.LogWarning("[测试结果] 自动保存失败：" + error.Message);
            }
        }
    }

    internal static void Save(ITestResultAdaptor result)
    {
        if (result == null) throw new ArgumentNullException("result");
        var projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null || projectRoot.Parent == null)
            throw new InvalidOperationException("无法从 Unity 项目目录确定仓库根目录。");
        string reportPath = Path.Combine(projectRoot.Parent.FullName, RelativeReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));

        // 使用框架提供的原始结果树，保留单项/选中项/整轮的断言、堆栈和测试输出。
        var resultNode = result.ToXml();
        int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(reportPath, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("test-run");
            writer.WriteAttributeString("id", "2");
            writer.WriteAttributeString("result", result.ResultState);
            WriteCount(writer, "testcasecount", total);
            WriteCount(writer, "total", total);
            WriteCount(writer, "passed", result.PassCount);
            WriteCount(writer, "failed", result.FailCount);
            WriteCount(writer, "skipped", result.SkipCount);
            WriteCount(writer, "inconclusive", result.InconclusiveCount);
            WriteCount(writer, "asserts", result.AssertCount);
            writer.WriteAttributeString("start-time", result.StartTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("end-time", result.EndTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("duration", result.Duration.ToString(CultureInfo.InvariantCulture));
            resultNode.WriteTo(writer);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
    }

    private static void WriteCount(XmlWriter writer, string name, int value)
    {
        writer.WriteAttributeString(name, value.ToString(CultureInfo.InvariantCulture));
    }
}
