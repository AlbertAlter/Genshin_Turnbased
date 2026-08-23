using System;
using System.Collections.Generic;
using UnityEngine;

public partial class DataManager
{
    private readonly List<string> _loadErrors = new List<string>();

    /// <summary>最近一次整套加载是否成功。</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// 安全重载全部配表。只有全部文件读取和校验成功才提交新数据；
    /// 任何异常都会恢复重载前的整套集合，并继续向调用方抛出。
    /// </summary>
    public void ReloadAllData()
    {
        LoadDataAtomically(() =>
        {
            IReadOnlyList<string> preflightErrors = DataSourcePreflightValidator.Validate(ChartsPath);
            if (preflightErrors.Count > 0)
                throw new DataLoadException("配表文件预检失败", preflightErrors);

            LoadAllTables();

            if (_loadErrors.Count > 0)
                throw new DataLoadException($"配表校验失败，共 {_loadErrors.Count} 处", _loadErrors);
        });

        Debug.Log($"DataManager: load complete. {CharacterOverviewList.Count} chars, {EnemyMainDict.Count} enemies, {StatusMainDict.Count} statuses");
    }

    /// <summary>供安全重载流程与 EditMode 回滚测试共用。</summary>
    protected void LoadDataAtomically(Action loadAction)
    {
        if (loadAction == null)
            throw new ArgumentNullException(nameof(loadAction));

        DataManagerState previousState = DataManagerState.Capture(this);
        bool wasLoaded = IsLoaded;

        new DataManagerState().ApplyTo(this);
        _loadErrors.Clear();

        try
        {
            loadAction();
            IsLoaded = true;
            _loadErrors.Clear();
        }
        catch (Exception exception)
        {
            previousState.ApplyTo(this);
            IsLoaded = wasLoaded;
            string[] collectedErrors = _loadErrors.ToArray();
            _loadErrors.Clear();

            if (exception is DataLoadException)
                throw;

            var errors = new List<string>(collectedErrors) { exception.Message };
            throw new DataLoadException("配表加载失败，已恢复上一次成功数据", errors, exception);
        }
    }

    /// <summary>执行一次配表校验并收集错误，所有表读完后统一报告。</summary>
    private void TryValidate(string tag, Action validate)
    {
        try
        {
            validate();
        }
        catch (TableValidationException exception)
        {
            _loadErrors.Add(exception.Message);
        }
        catch (Exception exception)
        {
            _loadErrors.Add($"[{tag}] {exception.Message}");
        }
    }

    private void LoadAllTables()
    {
        LoadCharacterOverview();
        LoadCharacterSheets();
        LoadGrowthCurve();
        LoadStatusData();
        LoadEnemyStatusData();
        LoadOverallStatusData();
        LoadEnemyAttributes();
        LoadEnemyCurves();
        LoadEnemySkill();
        LoadReactionLevelCoefficient();

        // 旧 StageConfig.xlsx 为可选兼容数据；当前战斗使用 Resources/StageConfig.json。
        LoadStageConfig();
    }
}
