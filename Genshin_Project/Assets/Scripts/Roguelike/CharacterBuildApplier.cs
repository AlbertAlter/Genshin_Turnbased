using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ============================================================
// 天赋/命座/突破初始化器（独立玩法层，2026-08-12）
// 按角色档案（等级/命座/突破）读取 Talent/Constellation 配表，
// 过滤激活条件后应用 Modifiers 指令：
//   InitiateStatus(状态ID2) / ModifyEffect(效果ID2;字段=值)
//   InsertEffect(位置;目标效果ID2;字段=值...) / ModifySkill(技能ID2;字段=值)
//   SkillLevelUp(技能类型,等级)
// 不污染 BattleManager/CharacterBattleController：
//   所有数据修改走其公开的克隆式接口（GetSkillEffects/SetSkillEffects/ModifySkillData），
//   多次进出战斗不叠加。
// 调用时机：战斗开始前（现在由 BattleTester 调用；将来由章节系统/存档流程接管）。
// ============================================================

public static class CharacterBuildApplier
{
    /// <summary>对出战角色应用档案对应的天赋/命座/突破效果（战斗开始前调用）。</summary>
    public static void ApplyBuild(CharacterBattleController ctrl, CharacterProfile profile)
    {
        if (ctrl == null || profile == null) return;
        var dm = DataManager.Instance;
        if (dm == null) return;

        // 1. 技能等级（关卡配置直接指定，1-15）
        for (int i = 0; i < 4 && i < profile.SkillLevels.Length; i++)
        {
            if (profile.SkillLevels[i] > 0)
                ctrl.SkillLevels[i] = Mathf.Clamp(profile.SkillLevels[i], 1, 15);
        }

        // 2. 天赋（等级 >= UnlockAfter 自动激活）
        if (dm.TalentDict.TryGetValue(profile.CharacterID, out var talents))
        {
            foreach (var t in talents)
            {
                if (profile.Level < t.UnlockAfter) continue;
                ApplyModifiers(ctrl, t.Modifiers);
                LogManager.Log(LogCategory.Build, $"天赋{t.TalentIndex} 生效（Lv{profile.Level}≥{t.UnlockAfter}）: {t.Description}");
            }
        }

        // 3. 命座（序号 <= 命座数；Modifiers 为空的不处理，如 C2 走状态 OnHit 链）
        if (dm.ConstellationDict.TryGetValue(profile.CharacterID, out var cons))
        {
            foreach (var c in cons)
            {
                if (c.ConstellationIndex > profile.ConstellationLevel) continue;
                if (string.IsNullOrWhiteSpace(c.Modifiers)) continue;
                ApplyModifiers(ctrl, c.Modifiers);
                LogManager.Log(LogCategory.Build, $"命座{c.ConstellationIndex} 生效: {c.Description}");
            }
        }
    }

    // ================= Modifiers 解析 =================

    /// <summary>解析并应用一行 Modifiers 文本（多指令按换行分隔）。</summary>
    static void ApplyModifiers(CharacterBattleController ctrl, string modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers)) return;
        foreach (var line in modifiers.Split('\n'))
        {
            var cmd = line.Trim();
            if (cmd.Length == 0) continue;
            int paren = cmd.IndexOf('(');
            if (paren <= 0 || !cmd.EndsWith(")"))
            {
                LogManager.LogWarning(LogCategory.Build, $"无法解析的指令: {cmd}");
                continue;
            }
            string name = cmd.Substring(0, paren).Trim();
            string args = cmd.Substring(paren + 1, cmd.Length - paren - 2).Trim();
            ApplyCommand(ctrl, name, args);
        }
    }

    static void ApplyCommand(CharacterBattleController ctrl, string name, string args)
    {
        // 配表一致性：除 SkillLevelUp（参数用逗号是既定格式）外，指令参数统一分号分隔（值内部允许逗号）
        if (name != "SkillLevelUp" && !string.IsNullOrEmpty(args) && !args.Contains(';') && args.Contains(','))
            LogManager.LogWarning(LogCategory.Build, $"指令 {name} 的参数使用了逗号分隔，请统一改为分号（值内部的逗号除外）: {args}");

        switch (name)
        {
            case "InitiateStatus": ApplyInitiateStatus(ctrl, args); break;
            case "ModifyEffect": ApplyModifyEffect(ctrl, args); break;
            case "InsertEffect": ApplyInsertEffect(ctrl, args); break;
            case "ModifySkill": ApplyModifySkill(ctrl, args); break;
            case "SkillLevelUp": ApplySkillLevelUp(ctrl, args); break;
            default: LogManager.LogWarning(LogCategory.Build, $"未实现的指令类型: {name}({args})"); break;
        }
    }

    /// <summary>InitiateStatus(状态ID2)：战斗开场施加状态（持续整场，999回合）。</summary>
    static void ApplyInitiateStatus(CharacterBattleController ctrl, string args)
    {
        string statusID2 = args.Trim();
        if (string.IsNullOrEmpty(statusID2)) return;
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusMainDict.TryGetValue(statusID2, out var main))
        {
            LogManager.LogWarning(LogCategory.Build, $"InitiateStatus 状态不存在: {statusID2}");
            return;
        }
        ctrl.Entity.AddStatus(statusID2, ctrl.Entity, 999, 2, 0, main);
        LogManager.Log(LogCategory.Build, $"InitiateStatus: {statusID2} 已施加（开场）");
    }

    /// <summary>ModifyEffect(效果ID2;字段=值...)：克隆效果行后修改字段（如 T1 爆发目标+1）。</summary>
    static void ApplyModifyEffect(CharacterBattleController ctrl, string args)
    {
        var parts = SplitArgs(args);
        if (parts.Count < 2) { LogManager.LogWarning(LogCategory.Build, $"ModifyEffect 参数不足: {args}"); return; }
        string effectID2 = parts[0].Trim();
        var dm = DataManager.Instance;
        if (dm == null || !dm.SkillEffectDict.TryGetValue(effectID2, out var src))
        {
            LogManager.LogWarning(LogCategory.Build, $"ModifyEffect 效果不存在: {effectID2}");
            return;
        }
        var eff = ctrl.CloneEffect(src);
        ApplyFieldAssignments(eff, parts, 1);
        ReplaceEffectInSkills(ctrl, effectID2, eff);
        LogManager.Log(LogCategory.Build, $"ModifyEffect: {effectID2} 已修改");
    }

    /// <summary>InsertEffect(位置;目标效果ID2;字段=值...)：克隆目标效果为模板，覆盖字段后插入其后。
    /// 位置参数（第1段）暂按"插入到目标效果之后"处理（安柏用例效果单行，顺序无歧义）。</summary>
    static void ApplyInsertEffect(CharacterBattleController ctrl, string args)
    {
        var parts = SplitArgs(args);
        if (parts.Count < 3) { LogManager.LogWarning(LogCategory.Build, $"InsertEffect 参数不足: {args}"); return; }
        string afterEffectID2 = parts[1].Trim();
        var dm = DataManager.Instance;
        if (dm == null || !dm.SkillEffectDict.TryGetValue(afterEffectID2, out var anchor))
        {
            LogManager.LogWarning(LogCategory.Build, $"InsertEffect 目标效果不存在: {afterEffectID2}");
            return;
        }
        var eff = ctrl.CloneEffect(anchor);          // 以目标效果为模板（继承 EffectType 等）
        eff.SkillEffectID = 0;                        // 新效果无唯一ID（不被 _effectById 覆盖）
        eff.SkillEffectID2 = "";                      // 等级查询走 %引用路径，不匹配 SkillLevel 行
        eff.TargetOverride = "";                      // 新效果不继承模板的目标覆盖（C6 曾继承 "0,0" 把全队加攻施加到上一条目标=敌人）
        // 2026-08-15：插入效果不继承模板的目标选择参数（TargetType/TargetNumber/TargetConsecutive）——
        // 否则模板带 TargetNumber>0 时插入效果会被误判为需选目标（凯亚 T1 回血/T2 回能插入 SE_Skill_Kaeya1 后
        // 继承了 Enemy,1,1，导致战技除选敌人外还要额外确认两次自己）。目标参数完全由配表显式字段决定。
        eff.TargetType = "";
        eff.TargetNumber = 0;
        eff.TargetConsecutive = 0;
        eff.TargetConsecutiveSet = false;
        //配表格式：InsertEffect(位置;目标效果;效果类型;字段=值...)
        //效果类型（第3段，无"="时）显式写入 EffectType，避免继承模板类型（如 T2 的 ApplyStatus 被模板 Damage 覆盖）
        if (parts.Count > 2 && !parts[2].Contains('='))
        {
            SetField(eff, "EffectType", parts[2].Trim());
            ApplyFieldAssignments(eff, parts, 3);
        }
        else
        {
            ApplyFieldAssignments(eff, parts, 2);
        }
        InsertAfterEffect(ctrl, afterEffectID2, eff);
        LogManager.Log(LogCategory.Build, $"InsertEffect: 新效果({eff.EffectType}) 已插入 {afterEffectID2} 之后");
        //诊断（2026-08-14）：打印目标技能插入后的效果列表内容
        var diagSkill = FindSkillOfEffect(ctrl, afterEffectID2);
        if (diagSkill != null)
        {
            var diagList = ctrl.GetSkillEffects(diagSkill);
            var sb = new System.Text.StringBuilder();
            for (int di = 0; di < diagList.Count; di++)
            {
                if (di > 0) sb.Append(" | ");
                sb.Append(diagList[di].SkillEffectID2 + ":" + diagList[di].EffectType);
            }
            LogManager.Log(LogCategory.Build, $"[诊断] {diagSkill} 效果列表 = {sb}");
        }
    }

    /// <summary>ModifySkill(技能ID2;字段=值...)：克隆改技能字段（C4 兔兔伯爵次数/冷却）。参数统一分号分隔。</summary>
    static void ApplyModifySkill(CharacterBattleController ctrl, string args)
    {
        var parts = SplitArgs(args);
        if (parts.Count < 2) { LogManager.LogWarning(LogCategory.Build, $"ModifySkill 参数不足: {args}"); return; }
        string skillID2 = parts[0].Trim();
        ctrl.ModifySkillData(skillID2, sk => ApplyFieldAssignments(sk, parts, 1));
        LogManager.Log(LogCategory.Build, $"ModifySkill: {skillID2} 已修改");
    }

    /// <summary>SkillLevelUp(技能类型,等级)：技能等级+n（1-15，C3/C5 用）。</summary>
    static void ApplySkillLevelUp(CharacterBattleController ctrl, string args)
    {
        var parts = args.Split(',');
        if (parts.Length < 2) { LogManager.LogWarning(LogCategory.Build, $"SkillLevelUp 参数不足: {args}"); return; }
        if (!int.TryParse(parts[0].Trim(), out int skillType)) return;
        if (!int.TryParse(parts[1].Trim(), out int add)) return;
        if (skillType >= 0 && skillType < ctrl.SkillLevels.Length)
            ctrl.SkillLevels[skillType] = Mathf.Clamp(ctrl.SkillLevels[skillType] + add, 1, 15);
        LogManager.Log(LogCategory.Build, $"SkillLevelUp: 技能{skillType} +{add}级 → {ctrl.SkillLevels[skillType]}");
    }

    // ================= 工具 =================

    /// <summary>按分号切分参数（跳过空段）。</summary>
    static List<string> SplitArgs(string args)
    {
        var list = new List<string>();
        foreach (var p in args.Split(';'))
        {
            var t = p.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    /// <summary>从 startIndex 开始应用 "字段=值" 赋值（int/float/string 字段通用，反射）。</summary>
    static void ApplyFieldAssignments(object obj, List<string> parts, int startIndex)
    {
        for (int i = startIndex; i < parts.Count; i++)
        {
            var kv = parts[i].Split('=');
            if (kv.Length < 2) continue;
            string field = kv[0].Trim();
            string value = string.Join("=", kv.Skip(1)).Trim(); // 值内可能含 '='（如 ScriptHook=Hit(x)）
            SetField(obj, field, value);
        }
    }

    /// <summary>按字段名反射赋值（SkillEffectData/SkillMainData 的 int/float/string 字段）。</summary>
    static void SetField(object obj, string fieldName, string value)
    {
        var fi = obj.GetType().GetField(fieldName);
        if (fi == null) { LogManager.LogWarning(LogCategory.Build, $"字段不存在: {fieldName}"); return; }
        try
        {
            if (fi.FieldType == typeof(int)) fi.SetValue(obj, int.Parse(value));
            else if (fi.FieldType == typeof(float)) fi.SetValue(obj, float.Parse(value));
            else if (fi.FieldType == typeof(string)) fi.SetValue(obj, value);
            else LogManager.LogWarning(LogCategory.Build, $"不支持的字段类型: {fieldName} ({fi.FieldType})");
        }
        catch (System.Exception e) { LogManager.LogWarning(LogCategory.Build, $"字段赋值失败 {fieldName}={value}: {e.Message}"); }
    }

    /// <summary>定位效果ID2所属技能，把修改后的效果替换回效果列表。</summary>
    static void ReplaceEffectInSkills(CharacterBattleController ctrl, string effectID2, SkillEffectData newEff)
    {
        string skillID2 = FindSkillOfEffect(ctrl, effectID2);
        if (skillID2 == null) { LogManager.LogWarning(LogCategory.Build, $"未找到 {effectID2} 所属技能"); return; }
        var list = ctrl.GetSkillEffects(skillID2);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].SkillEffectID2 == effectID2)
            {
                list[i] = newEff;
                ctrl.SetSkillEffects(skillID2, list);
                return;
            }
        }
        LogManager.LogWarning(LogCategory.Build, $"ModifyEffect 未在技能列表中找到: {effectID2}");
    }

    /// <summary>把新效果插入目标效果之后（列表顺序即执行顺序）。</summary>
    static void InsertAfterEffect(CharacterBattleController ctrl, string afterEffectID2, SkillEffectData newEff)
    {
        string skillID2 = FindSkillOfEffect(ctrl, afterEffectID2);
        if (skillID2 == null) { LogManager.LogWarning(LogCategory.Build, $"未找到 {afterEffectID2} 所属技能"); return; }
        var list = ctrl.GetSkillEffects(skillID2);
        int idx = list.FindIndex(e => e.SkillEffectID2 == afterEffectID2);
        if (idx < 0) { LogManager.LogWarning(LogCategory.Build, $"InsertEffect 未找到目标效果位置: {afterEffectID2}"); return; }
        list.Insert(idx + 1, newEff);
        ctrl.SetSkillEffects(skillID2, list);
    }

    /// <summary>遍历角色技能列表，精确匹配效果ID2，返回所属技能ID2。</summary>
    static string FindSkillOfEffect(CharacterBattleController ctrl, string effectID2)
    {
        foreach (var sid in ctrl.GetSkillID2List())
        {
            foreach (var e in ctrl.GetSkillEffects(sid))
                if (e.SkillEffectID2 == effectID2) return sid;
        }
        return null;
    }
}
