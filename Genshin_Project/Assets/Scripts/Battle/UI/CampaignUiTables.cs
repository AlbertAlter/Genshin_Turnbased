using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>正式游戏启动时解析战斗表现配置，独立调参预览仍可主动重读已发布的表。</summary>
internal static class CampaignUiTables
{
    private static Dictionary<Type, Lazy<object>> published;

    internal static T Read<T>(Func<T> read)
    {
        if (published == null) return read();
        if (!published.TryGetValue(typeof(T), out var table))
        {
            table = new Lazy<object>(() => read());
            published.Add(typeof(T), table);
        }
        return (T)table.Value;
    }

    internal static void Preload()
    {
        if (published != null) return;
        published = new Dictionary<Type, Lazy<object>>();
        var failures = new List<string>();
        void Load<T>(Func<T> load)
        {
            try { load(); }
            catch (Exception error) { failures.Add(typeof(T).Name + "：" + error.Message); }
        }

        Load(BattlefieldAnimationParameters.Load);
        Load(ElementColorPalette.Load);
        Load(BattlefieldDamagePopupStyle.Load);
        Load(BattlefieldStatusPopupStyle.Load);
        Load(BattleInfoPopupStyle.Load);
        Load(HitShakeStyle.Load);
        Load(HitEffectTextureStyle.Load);
        Load(UIHitEffectStyle.Load);
        Load(UIShieldStyle.Load);
        Load(UICardOrbitArrowStyle.Load);
        Load(UICardFreezeStyle.Load);
        Load(FreezeAudioSettings.Load);
        Load(UIStatusNamePopupStyle.Load);
        Load(UICardVariantStyle.Load);
        Load(UICardEntranceStyle.Load);
        Load(UIGoldenDissolveStyle.Load);
        Load(CharacterCardSwitchTable.Load);
        Load(EnemyAppearanceTable.Load);
        Load(CharacterVoiceTable.Load);
        Load(BattleTimelineSettings.Load);
        Load(BattleSkillPresentationTemplates.Load);
        Load(BattleAttackAnimationTable.Load);
        Load(BattlePresentationConfiguration.Load);
        if (failures.Count > 0)
            Debug.LogWarning("以下战斗表现配表未能加载：\n" + string.Join("\n", failures));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Clear() => published = null;
}
