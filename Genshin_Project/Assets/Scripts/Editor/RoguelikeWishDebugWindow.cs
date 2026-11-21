using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class RoguelikeWishDebugWindow : EditorWindow
{
    private RoguelikeWishView view;

    [MenuItem("Tools/调试/祈愿")]
    private static void Open() => GetWindow<RoguelikeWishDebugWindow>("祈愿调试");

    private void OnInspectorUpdate() => Repaint();

    private void OnGUI()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("进入播放模式并打开祈愿界面后，可以切换语言和查看各版本卡池。", MessageType.Info);
            return;
        }

        if (view == null) view = FindObjectOfType<RoguelikeWishView>();
        view = (RoguelikeWishView)EditorGUILayout.ObjectField("祈愿界面", view, typeof(RoguelikeWishView), true);
        if (view == null) return;

        int language = EditorGUILayout.Popup("界面语言", view.DebugEnglish ? 1 : 0, new[] { "中文", "English" });
        view.SetEnglish(language == 1);

        var phases = view.DebugPhases;
        if (phases.Length == 0)
        {
            EditorGUILayout.HelpBox("当前祈愿界面还没有载入卡池。", MessageType.Info);
            return;
        }

        var versions = phases.Select(VersionOf).Distinct().ToArray();
        var phase = view.DebugPhase;
        int active = phase == null ? 0 : Array.IndexOf(versions, VersionOf(phase)) + 1;
        var versionLabels = new[] { "跟随存档" }.Concat(versions.Select(version => version.ToString())).ToArray();
        using (new EditorGUI.DisabledScope(!view.CanChangeDebugPhase))
        {
            int selected = EditorGUILayout.Popup("版本", active, versionLabels);
            if (selected != active)
            {
                phase = selected == 0 ? null : phases.First(candidate => VersionOf(candidate).Equals(versions[selected - 1]));
                view.SetDebugPhase(phase);
            }

            if (phase != null)
            {
                var periods = phases.Where(candidate => VersionOf(candidate).Equals(VersionOf(phase))).ToArray();
                int index = Array.IndexOf(periods, phase);
                int chosen = EditorGUILayout.Popup("期数", index,
                    periods.Select(candidate => $"第 {candidate.Build} 期").ToArray());
                if (chosen != index) view.SetDebugPhase(periods[chosen]);
            }
        }

        if (phase != null) EditorGUILayout.HelpBox("当前为只读卡池。切回跟随存档即可祈愿。", MessageType.Info);
    }

    private static Version VersionOf(Version phase) => new Version(phase.Major, phase.Minor);
}
