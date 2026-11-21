using System;
using System.Collections.Generic;
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
    private const string DiscoveryPath = "codex_worktable/TestDiscovery.txt";
#if ROGUELIKE_SAVE_TESTS
    private const string SaveTestDefine = "ON";
#else
    private const string SaveTestDefine = "OFF";
#endif
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

    [MenuItem("Tools/Tests/Export Test Discovery (No Run)")]
    private static void ExportDiscovery()
    {
        api.RetrieveTestList(TestMode.EditMode, editRoot =>
            api.RetrieveTestList(TestMode.PlayMode, playRoot =>
            {
                try
                {
                    var lines = new List<string> { $"发现清单（未运行） UTC={DateTime.UtcNow:o} 目标={EditorUserBuildSettings.activeBuildTarget}",
                        $"ROGUELIKE_SAVE_TESTS={SaveTestDefine}；OFF 时受此宏保护的用例未编译，不计作通过。" };
                    AppendDiscovered(lines, TestMode.EditMode, editRoot);
                    AppendDiscovered(lines, TestMode.PlayMode, playRoot);
                    File.WriteAllLines(ReportPath(DiscoveryPath), lines, new UTF8Encoding(false));
                    Debug.Log($"测试发现清单：{DiscoveryPath}。未启动测试，未覆盖运行结果。");
                }
                catch (Exception error) { Debug.LogError("导出测试发现清单失败：" + error.Message); }
            }));
    }

    private static void AppendDiscovered(List<string> lines, TestMode mode, ITestAdaptor root)
    {
        if (root == null) throw new InvalidOperationException($"{mode} 未返回测试树。");
        var leaves = new List<string>();
        void Visit(ITestAdaptor node)
        {
            if (!node.IsSuite) leaves.Add($"{node.FullName}\t{node.RunState}");
            else if (node.HasChildren) foreach (var child in node.Children) Visit(child);
        }
        Visit(root);
        leaves.Sort(StringComparer.Ordinal);
        lines.Add($"[{mode}] {leaves.Count}");
        lines.AddRange(leaves);
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

    private static string ReportPath(string relativePath)
    {
        var projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null || projectRoot.Parent == null)
            throw new InvalidOperationException("无法从 Unity 项目目录确定仓库根目录。");
        string reportPath = Path.Combine(projectRoot.Parent.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        return reportPath;
    }

    internal static void Save(ITestResultAdaptor result)
    {
        if (result == null) throw new ArgumentNullException("result");
        string reportPath = ReportPath(RelativeReportPath);

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
            writer.WriteAttributeString("roguelike-save-tests", SaveTestDefine);
            writer.WriteAttributeString("build-target", EditorUserBuildSettings.activeBuildTarget.ToString());
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
