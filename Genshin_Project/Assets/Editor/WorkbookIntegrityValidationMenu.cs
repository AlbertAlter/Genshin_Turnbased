using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class WorkbookIntegrityValidationMenu
{
    [MenuItem("Tools/Genshin/Validate Runtime Tables", priority = 2100)]
    public static void ValidateRuntimeTables()
    {
        string dataRoot = Path.Combine(Application.streamingAssetsPath, "Data");
        IReadOnlyList<WorkbookValidationIssue> issues = WorkbookIntegrityValidator.Validate(dataRoot);
        int errors = issues.Count(issue => issue.Severity == WorkbookValidationSeverity.Error);
        int warnings = issues.Count - errors;

        foreach (WorkbookValidationIssue issue in issues)
        {
            if (issue.Severity == WorkbookValidationSeverity.Error) Debug.LogError(issue.ToString());
            else Debug.LogWarning(issue.ToString());
        }

        string summary = $"Table validation finished: {errors} error(s), {warnings} warning(s).\n{dataRoot}";
        if (errors == 0) Debug.Log(summary);
        else Debug.LogError(summary);
        EditorUtility.DisplayDialog("Genshin Table Validation", summary, "OK");
    }
}
