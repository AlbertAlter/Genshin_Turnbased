using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

// ============================================================
// 天赋/命座/突破初始化器（独立玩法层，2026-08-12）
// 按角色档案（等级/命座/突破）读取 Talent/Constellation 配表，
// 过滤激活条件后应用 Modifiers 指令：
//   InitiateStatus(状态ID2) / ModifyEffect(效果ID2;字段=值)
//   InsertEffect(位置;目标效果ID2;字段=值...) / ModifySkill(技能ID2;字段=值)
// SkillLevelUp由角色实例的自身等级查询解析；这里仅加载查询结果，不执行加级。
// 不污染 BattleManager/CharacterBattleController：
//   所有数据修改走其公开的克隆式接口（GetSkillEffects/SetSkillEffects/ModifySkillData），
//   多次进出战斗不叠加。
// 调用时机：战斗开始前（现在由 BattleTester 调用；将来由章节系统/存档流程接管）。
// ============================================================

public static class CharacterBuildApplier
{
    /// <summary>创建裸面板时读取完整养成输入；注册完整编队后才调用ApplyBuild挂载天赋/装备。</summary>
    public static void InitializePanel(CharacterBattleController ctrl, CharacterProfile profile)
    {
        if (ctrl == null) throw new System.ArgumentNullException(nameof(ctrl));
        if (profile == null) throw new System.ArgumentNullException(nameof(profile));
        int stage = profile.Ascension == -1 ? CharacterAscensionRules.FromLegacyFlag(profile.Level, profile.IsAscended)
            : profile.Ascension;
        CharacterAscensionRules.Validate(profile.Level, stage);
        CharacterSkillLevelInfo skillLevels = profile.GetSkillLevelInfo();
        bool flag = profile.Ascension == -1 ? profile.IsAscended
            : CharacterAscensionRules.HasCurrentAscension(profile.Level, stage);
        if (ctrl.Entity == null || ctrl.CharacterID != profile.CharacterID || ctrl.Level != profile.Level
            || ctrl.Ascension != stage)
        {
            // 这里只构筑裸角色，不用重新Init去覆盖已挂载的武器、状态或本场资源，也不实现战斗内升级。
            if (ctrl.EquippedWeapon != null || ctrl.Entity != null
                && (BattleAttributeInitialization.IsComplete(ctrl.Entity) || ctrl.Entity.GetStatusList().Count > 0))
                throw new System.InvalidOperationException("角色已应用构筑或进入战斗，不能通过养成查询重建面板；请创建新的裸角色。");
            if (profile.Ascension == -1) ctrl.Init(profile.CharacterID, profile.Level, profile.IsAscended);
            else ctrl.Init(profile.CharacterID, profile.Level, stage);
        }
        if (ctrl.Entity == null) throw new System.InvalidOperationException("角色裸面板初始化失败：" + profile.CharacterID);
        ctrl.Entity.IsAscended = flag;
        ctrl.Entity.ConstellationLevel = profile.ConstellationLevel;
        ctrl.SkillLevels = skillLevels.CopyOwnLevels();
    }

    /// <summary>
    /// 对出战角色应用档案对应的天赋/命座/突破及武器效果。
    /// 裸面板先由InitializePanel读取profile的整数突破阶段（旧配置兼容布尔标记），不能事后补字段。
    /// 战斗开始前、双方完整编队已设定SlotPosition并注册后调用；初始化OnApply/BindStatus沿正常目标入口解析。
    /// 所有角色构筑完成后才调用StartBattle，由统一入口完成队伍效果与初始属性收尾。
    /// </summary>
    public static void ApplyBuild(CharacterBattleController ctrl, CharacterProfile profile)
    {
        if (ctrl == null || profile == null) return;
        var dm = DataManager.Instance;
        if (dm == null) return;

        InitializePanel(ctrl, profile);
        BattleAttributeInitialization.BeginBuild(ctrl.Entity);

        // 1. 自身技能等级已由InitializePanel从角色实例侧查询并加载，不再临时执行SkillLevelUp。

        // 2. 天赋：必须完成UnlockAfter指定等级的突破；空白/0默认解锁。
        if (dm.TalentDict.TryGetValue(profile.CharacterID, out var talents))
        {
            foreach (var t in talents)
            {
                if (!CharacterAscensionRules.IsTalentUnlocked(t.UnlockAfter, ctrl.Level, ctrl.Ascension)) continue;
                ApplyModifiers(ctrl, t.Modifiers);
                LogManager.Log(LogCategory.Build, $"天赋{t.TalentIndex} 生效（Lv{ctrl.Level}, Ascension={ctrl.Ascension}, 解锁突破={t.UnlockAfter}）: {t.Description}");
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

        // 4. 武器：面板与 Weapon_Initiate 状态统一走装备生命周期。
        if (profile.Weapon != null && profile.Weapon.WeaponID > 0)
            ctrl.EquipWeapon(profile.Weapon);
    }

    // ================= Modifiers 解析 =================

    /// <summary>多指令支持括号外的分号或换行，括号内分号保留为参数。</summary>
    static void ApplyModifiers(CharacterBattleController ctrl, string modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers)) return;
        List<string> commands;
        try { commands = SplitModifierCommands(modifiers); }
        catch (System.FormatException error)
        {
            LogManager.LogWarning(LogCategory.Build, error.Message);
            return;
        }
        foreach (var line in commands)
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
        switch (name)
        {
            case "InitiateStatus": ApplyInitiateStatus(ctrl, args); break;
            case "ModifyEffect": ApplyModifyEffect(ctrl, args); break;
            case "InsertEffect": ApplyInsertEffect(ctrl, args); break;
            case "ModifySkill": ApplyModifySkill(ctrl, args); break;
            case "SkillLevelUp": break; // 已由角色自身等级查询处理；不能随状态初始化/重建再次加级。
            default: LogManager.LogWarning(LogCategory.Build, $"未实现的指令类型: {name}({args})"); break;
        }
    }

    /// <summary>InitiateStatus(状态ID2)：开场施加；月曜/始基力标记永久，其他状态保留原999回合约定。</summary>
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
        int duration = LunarReactionConversion.IsTalentMarker(statusID2) || ArkheStatuses.IsMarker(statusID2) ? 0 : 999;
        StatusInstance status = ctrl.Entity.AddStatus(statusID2, ctrl.Entity, duration, 2, 0, main);
        ctrl.RecordCharacterInitializationStatus(status, duration);
        LogManager.Log(LogCategory.Build, $"InitiateStatus: {statusID2} 已施加（开场）");
    }

    /// <summary>ModifyEffect(效果ID2;字段=值...)：克隆效果行后修改字段（如 T1 爆发目标+1）。</summary>
    static void ApplyModifyEffect(CharacterBattleController ctrl, string args)
    {
        var parts = SplitArgs(args);
        if (parts.Count < 2) { LogManager.LogWarning(LogCategory.Build, $"ModifyEffect 参数不足: {args}"); return; }
        string effectID2 = parts[0].Trim();
        if (!TryFindEffect(ctrl, effectID2, out string skillID2,
                out List<SkillEffectData> effects, out int effectIndex))
        {
            LogManager.LogWarning(LogCategory.Build, $"ModifyEffect 未在角色技能中找到: {effectID2}");
            return;
        }

        // 必须基于控制器当前副本继续修改，保证多条天赋/命座修改可以叠加。
        SkillEffectData eff = ctrl.CloneEffect(effects[effectIndex]);
        ApplyFieldAssignments(eff, parts, 1);
        effects[effectIndex] = eff;
        ctrl.SetSkillEffects(skillID2, effects);
        LogManager.Log(LogCategory.Build, $"ModifyEffect: {effectID2} 已修改");
    }

    /// <summary>InsertEffect(位置;目标效果ID2;字段=值...)：克隆目标效果为模板，覆盖字段后插入其后。
    /// 位置参数（第1段）暂按"插入到目标效果之后"处理（安柏用例效果单行，顺序无歧义）。</summary>
    static void ApplyInsertEffect(CharacterBattleController ctrl, string args)
    {
        var parts = SplitArgs(args);
        if (parts.Count < 3) { LogManager.LogWarning(LogCategory.Build, $"InsertEffect 参数不足: {args}"); return; }
        string afterEffectID2 = parts[1].Trim();
        if (!TryFindEffect(ctrl, afterEffectID2, out string skillID2,
                out List<SkillEffectData> effects, out int anchorIndex))
        {
            LogManager.LogWarning(LogCategory.Build, $"InsertEffect 未在角色技能中找到目标效果: {afterEffectID2}");
            return;
        }
        // 基于控制器当前锚点副本克隆，保留此前已应用的构筑修改。
        var eff = ctrl.CloneEffect(effects[anchorIndex]);
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
        effects.Insert(anchorIndex + 1, eff);
        ctrl.SetSkillEffects(skillID2, effects);
        LogManager.Log(LogCategory.Build, $"InsertEffect: 新效果({eff.EffectType}) 已插入 {afterEffectID2} 之后");

        var sb = new System.Text.StringBuilder();
        for (int index = 0; index < effects.Count; index++)
        {
            if (index > 0) sb.Append(" | ");
            sb.Append(effects[index].SkillEffectID2 + ":" + effects[index].EffectType);
        }
        LogManager.Log(LogCategory.Build, $"[诊断] {skillID2} 效果列表 = {sb}");
    }

    /// <summary>ModifySkill(技能ID2;字段=值...)：克隆改技能字段（C4 兔兔伯爵次数/冷却）。参数统一分号分隔。</summary>
    static void ApplyModifySkill(CharacterBattleController ctrl, string args)
    {
        var parts = SplitArgs(args);
        if (parts.Count < 2) { LogManager.LogWarning(LogCategory.Build, $"ModifySkill 参数不足: {args}"); return; }
        string skillID2 = parts[0].Trim();
        if (!ctrl.GetSkillID2List().Contains(skillID2))
        {
            LogManager.LogWarning(LogCategory.Build, $"ModifySkill 技能不存在: {skillID2}");
            return;
        }
        ctrl.ModifySkillData(skillID2, sk => ApplyFieldAssignments(sk, parts, 1));
        LogManager.Log(LogCategory.Build, $"ModifySkill: {skillID2} 已修改");
    }

    // ================= 工具 =================

    /// <summary>按分号切分参数（跳过空段）。</summary>
    static List<string> SplitArgs(string args)
    {
        var list = new List<string>();
        int start = 0, depth = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '(') depth++;
            else if (args[i] == ')') depth--;
            if (depth != 0 || (args[i] != ';' && args[i] != ',')) continue;
            bool nextAssignment = System.Text.RegularExpressions.Regex.IsMatch(args.Substring(i + 1), @"^\s*[A-Za-z_]\w*\s*=");
            bool positional = args[i] == ';' && !args.Substring(start, i - start).Contains("=");
            if (!nextAssignment && !positional) continue;
            string part = args.Substring(start, i - start).Trim();
            if (part.Length > 0) list.Add(part);
            start = i + 1;
        }
        string last = args.Substring(start).Trim();
        if (last.Length > 0) list.Add(last);
        return list;
    }

    public static List<string> SplitModifierCommands(string text)
        => CharacterModifierParser.SplitCommands(text);

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

    /// <summary>按字段名反射赋值（SkillEffectData/SkillMainData 的 int/float/bool/string 字段）。</summary>
    static void SetField(object obj, string fieldName, string value)
    {
        if (obj == null)
        {
            LogManager.LogWarning(LogCategory.Build, $"字段赋值目标为空: {fieldName}={value}");
            return;
        }

        var fi = obj.GetType().GetField(fieldName);
        if (fi == null) { LogManager.LogWarning(LogCategory.Build, $"字段不存在: {fieldName}"); return; }
        try
        {
            if (fi.FieldType == typeof(int))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    LogManager.LogWarning(LogCategory.Build, $"字段赋值失败 {fieldName}={value}: 不是有效整数");
                    return;
                }
                fi.SetValue(obj, parsed);
            }
            else if (fi.FieldType == typeof(float))
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    LogManager.LogWarning(LogCategory.Build, $"字段赋值失败 {fieldName}={value}: 不是有效小数");
                    return;
                }
                fi.SetValue(obj, parsed);
            }
            else if (fi.FieldType == typeof(bool))
            {
                if (!bool.TryParse(value, out bool parsed))
                {
                    LogManager.LogWarning(LogCategory.Build, $"字段赋值失败 {fieldName}={value}: 不是有效布尔值");
                    return;
                }
                fi.SetValue(obj, parsed);
            }
            else if (fi.FieldType == typeof(string)) fi.SetValue(obj, value);
            else LogManager.LogWarning(LogCategory.Build, $"不支持的字段类型: {fieldName} ({fi.FieldType})");
            if (obj is SkillEffectData effect) effect.AuthoredFields?.Add(fieldName);
        }
        catch (System.Exception exception)
        {
            LogManager.LogWarning(LogCategory.Build, $"字段赋值失败 {fieldName}={value}: {exception.Message}");
        }
    }

    /// <summary>在控制器自己的克隆效果表中精确定位效果，避免回写或重读 DataManager 全局行。</summary>
    static bool TryFindEffect(
        CharacterBattleController ctrl,
        string effectID2,
        out string skillID2,
        out List<SkillEffectData> effects,
        out int effectIndex)
    {
        skillID2 = null;
        effects = null;
        effectIndex = -1;
        if (ctrl == null || string.IsNullOrEmpty(effectID2)) return false;

        foreach (string candidateSkillID in ctrl.GetSkillID2List())
        {
            List<SkillEffectData> candidateEffects = ctrl.GetSkillEffects(candidateSkillID);
            int candidateIndex = candidateEffects.FindIndex(
                effect => effect != null && effect.SkillEffectID2 == effectID2);
            if (candidateIndex < 0) continue;

            skillID2 = candidateSkillID;
            effects = candidateEffects;
            effectIndex = candidateIndex;
            return true;
        }
        return false;
    }
}
