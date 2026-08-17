using System;
using UnityEngine;

/// <summary>
/// Hit �ַ�����������
/// </summary>
public static class HitDataParser
{
    /// <summary>
    /// ������ hit �ַ�������
    /// ������ʽ: "1.24*ATK;100;2" �� HitData(1.24, 100, 2)
    /// ��д��ʽ: "0.642" �� HitData(0.642, 0, 0)�����ͺ͸��ź����ɵȼ�1����䣩
    /// </summary>
    public static HitData Parse(string hitString)
    {
        if (string.IsNullOrEmpty(hitString))
            return new HitData(0, 0, 0);

        // ȥ�� *ATK ��׺����Ϊ����ֻ��Ҫϵ��
        string cleanStr = hitString.Replace("*TotalATK", "").Replace("*TotalDEF", "").Replace("*TotalHP", "")
            .Replace("*ATK", "").Replace("*DEF", "").Replace("*MaxHP", "");

                string[] parts = cleanStr.Split(',');

        float multiplier = 0, poise = 0, aura = 0;

        if (parts.Length >= 1 && float.TryParse(parts[0], out float m))
            multiplier = m;

        if (parts.Length >= 2 && float.TryParse(parts[1], out float p))
            poise = p;

        if (parts.Length >= 3 && float.TryParse(parts[2], out float a))
            aura = a;

        return new HitData(multiplier, poise, aura);
    }

    /// <summary>
    /// �ϲ� Level 1 ���������ݺ͸ߵȼ��Ĳ�������
    /// level1Hit: Level 1 ������ Hit����������ȫ��
    /// currentHit: ��ǰ�ȼ��� Hit������ֻ�б��ʣ�
    /// ���غϲ�������� Hit
    /// </summary>
    public static HitData MergeWithLevel1(HitData currentHit, HitData level1Hit)
    {
        float multiplier = currentHit.Multiplier > 0 ? currentHit.Multiplier : level1Hit.Multiplier;
        float poise = currentHit.Poise > 0 ? currentHit.Poise : level1Hit.Poise;
        float aura = currentHit.ElementAura > 0 ? currentHit.ElementAura : level1Hit.ElementAura;
        return new HitData(multiplier, poise, aura);
    }

    /// <summary>
    /// �����������ñ���ʽ���� "%SE_Charge_Amber.Hits1.M*0.2"
    /// ���� (EffectID, HitIndex, FieldType, Scale)
    /// FieldType: 0=M, 1=P, 2=E
    /// </summary>
    public static (string effectId, int hitIndex, int fieldType, float scale) ParseReference(string expression)
    {
        if (!expression.StartsWith("%"))
            return (null, 0, 0, 1f);

        // ȥ�� %
        string inner = expression.Substring(1);

        // �ָ����Ų��֣�%XXX.Hits1.M*0.2
        float scale = 1f;
        string refPart = inner;
        if (inner.Contains("*"))
        {
            string[] scaleSplit = inner.Split('*');
            refPart = scaleSplit[0];
            if (scaleSplit.Length > 1 && float.TryParse(scaleSplit[1], out float s))
                scale = s;
        }

        // ����: SE_Charge_Amber.Hits1.M
        string[] dotParts = refPart.Split('.');
        if (dotParts.Length < 3) return (null, 0, 0, 1f);

        string effectId = dotParts[0];
        int hitIndex = 0;
        int fieldType = 0; // 0=M, 1=P, 2=E

        // Hits1 �� index 0
        if (dotParts[1].StartsWith("Hits") && int.TryParse(dotParts[1].Substring(4), out int hIdx))
            hitIndex = hIdx - 1; // Hits1 �� 0, Hits2 �� 1

        if (dotParts.Length >= 3)
        {
            switch (dotParts[2])
            {
                case "M": fieldType = 0; break;
                case "P": fieldType = 1; break;
                case "E": fieldType = 2; break;
            }
        }

        return (effectId, hitIndex, fieldType, scale);
    }
}