using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 战斗界面总控（挂 Canvas 上，纯色占位版）。
/// 职责：
///  1. 数据刷新：从 BattleManager/CharacterBattleController 拉取血量/能量/AP/状态栏/按钮绑定，刷到 UI 组件
///  2. 按钮操作：普攻/重击/战技/爆发/换人按钮 → BattleTester 公开操作接口（与键盘同一套战斗逻辑）
/// 图片素材未就位，全部用纯色 Image 占位；图标后续按 SkillID2 命名接入。
/// 场景搭建：按字段把组件拖进 Inspector 对应槽位即可（详见搭建说明）。
/// </summary>
public class UIBattleController : MonoBehaviour
{
    [Header("数据源（拖引用，不拖自动 Find）")]
    public BattleInputController input;

    [Header("我方角色区（4份，从左至右对应位置1-4）")]
    public AllyUISlot[] allySlots = new AllyUISlot[4];

    [Header("敌方信息区（5份，从右至左对应位置1-5）")]
    public EnemyUISlot[] enemySlots = new EnemyUISlot[5];

    [Header("基础信息区")]
    public Image apBar;          // 竖向填充条（Image Type=Filled, FillMethod=Vertical）
    public TMP_Text apText;      // CurrentAP/TotalAP
    public TMP_Text apCostHint;  // 目标选择阶段显示 -20 等消耗提示

    [Header("战斗操作按钮")]
    public Button btnAttack;
    public Button btnHeavy;
    public Button btnSkill;
    public Button btnBurst;      // 爆发
    public Button btnSwitch;     // 换人
    public Button btnEndTurn;    // 结束回合（可选）
    public TMP_Text[] btnSkillNames = new TMP_Text[4]; // 按钮上显示的技能名（0普攻/1重击/2战技/3爆发）
    public TMP_Text[] btnCooldowns = new TMP_Text[4];       // 技能按钮上的冷却数字（0普攻/1重击/2战技/3=能量球）
    public TMP_Text[] allyBurstCooldowns = new TMP_Text[4]; // 各角色能量球上的爆发冷却数字（与 allySlots 对应）
    public RectTransform[] btnChargeDots = new RectTransform[4]; // 技能按钮下方的使用次数蓝点容器（0普攻/1重击/2战技/3=爆发暂不支持）
    public Image[] allyFieldBlocks = new Image[4];   // 我方位置色块（空位淡色，2026-08-14）
    public Image[] enemyFieldBlocks = new Image[5];  // 敌方位置色块（空位淡色，2026-08-14）

    [Header("遮罩与特效")]
    public Image dimOverlay;          // 全屏半透明遮罩（目标选择/换人时变暗，置于 UI 中层）
    public Image[] enemyHighlights;   // 敌方5个槽位被选中高亮（与 enemySlots 对应）
    public Image[] enemyFieldBorders;   // 敌方位置色块的当前目标黄框（边框Image，EnemyField_0~4对应位置1~5）

    [Header("状态图标（纯色占位）")]
    public Color statusColor = new Color(0.85f, 0.6f, 0.15f, 1f);
    //高亮颜色（2026-08-14）：
    //换人界面选中=黄色（与生成默认一致）；出战角色常态/目标选择施放角色=绿色（第二种，区别于敌方目标黄色）
    static readonly Color SwitchHighlightColor = new Color(1f, 0.95f, 0.6f, 0.4f);
    static readonly Color ActiveAllyHighlightColor = new Color(0.4f, 1f, 0.55f, 0.45f);
    //位置色块颜色（2026-08-14）：有人=正常色，空位=淡色（低透明度）
    static readonly Color AllyFieldBlockColor = new Color(0.35f, 0.4f, 0.5f, 0.9f);
    static readonly Color AllyFieldBlockEmptyColor = new Color(0.35f, 0.4f, 0.5f, 0.22f);
    static readonly Color EnemyFieldBlockColor = new Color(0.55f, 0.35f, 0.35f, 0.9f);
    static readonly Color EnemyFieldBlockEmptyColor = new Color(0.55f, 0.35f, 0.35f, 0.22f);

    [System.Serializable]
    public class AllyUISlot
    {
        public GameObject root;          // 整个槽位的根物体（隐藏空位用）
        public TMP_Text nameText;        // 角色名（暂显示 CharacterID）
        public Image hpBar;              // 血条（Filled Horizontal）
        public TMP_Text hpText;          // CurrentHP/TotalHP（向上取整）
        public Image energyBall;         // 能量球（Filled Radial360）
        public TMP_Text energyText;      // 能量数值
        public Transform statusContainer;// 状态图标容器（一行）
        public Button burstButton;       // 爆发按钮（可=能量球上的 Button）
        public Image highlight;          // 选中高亮（换人界面用）
    }

    [System.Serializable]
    public class EnemyUISlot
    {
        public GameObject root;
        public TMP_Text lvText;   // 等级（独立字段，Lv.XX）
        public TMP_Text nameText;
        public Image hpBar;
        public TMP_Text hpText;
        public Transform statusContainer;
    }

    private bool _switching = false;
    private bool _forcedDeathSwitch = false;
    private int _switchIndex = 0;
    /// <summary>是否处于换人界面（BattleInputController 空格过回合的禁用判断用，2026-08-14）。</summary>


    // 每个槽位的状态图标缓存（数量变化时重建）
    private readonly List<List<Image>> _allyStatusIcons = new List<List<Image>>();
    private readonly List<List<Image>> _enemyStatusIcons = new List<List<Image>>();

    void Start()
    {
        if (input == null) input = FindObjectOfType<BattleInputController>();
        for (int i = 0; i < 4; i++) _allyStatusIcons.Add(new List<Image>());
        for (int i = 0; i < 5; i++) _enemyStatusIcons.Add(new List<Image>());

        if (btnAttack != null) btnAttack.onClick.AddListener(() => input?.UIEnterSelectSkill(0));
        if (btnHeavy != null) btnHeavy.onClick.AddListener(() => input?.UIEnterSelectSkill(1));
        if (btnSkill != null) btnSkill.onClick.AddListener(() => input?.UIEnterSelectSkill(2));
        if (btnBurst != null) btnBurst.onClick.AddListener(() => input?.UIEnterSelectSkill(3));
        if (btnSwitch != null) btnSwitch.onClick.AddListener(OnSwitchButton);
        if (btnEndTurn != null) btnEndTurn.onClick.AddListener(() => input?.UIEndTurn());

        // 能量球作为爆发按钮（未单独拖 burstButton 时自动取能量球上的 Button）
        // 2026-08-15：点击任意角色能量槽 = 该槽位角色爆发（文档：该角色无需出战），不再统一用出战角色
        for (int i = 0; i < allySlots.Length; i++)
        {
            var slot = allySlots[i];
            if (slot == null) continue;
            if (slot.burstButton != null)
            {
                int idx = i;
                slot.burstButton.onClick.AddListener(() => input?.UIEnterBurstBySlot(idx));
            }
            else if (slot.energyBall != null)
            {
                var b = slot.energyBall.GetComponent<Button>();
                if (b != null)
                {
                    int idx = i;
                    b.onClick.AddListener(() => input?.UIEnterBurstBySlot(idx));
                }
            }
        }
    }

    void Update()
    {
        // input 兜底：BattleInputController 由 BattleTester.Start 动态挂载，
        // 脚本执行顺序不确定，每帧尝试直到找到（找到后缓存）
        if (input == null) input = FindObjectOfType<BattleInputController>();

        var bm = BattleManager.Instance;
        if (bm == null) return;

        RefreshAllies(bm);
        RefreshEnemies(bm);
        RefreshAP(bm);
        RefreshButtons(bm);
        RefreshOverlay(bm);
        RefreshFieldArea(bm); // 目标选择命中数显示 + 位置色块淡色（2026-08-14）

        // 出战角色若在我方回合开始时已经阵亡，自动进入不可取消的免费换人界面。
        if (!_switching && bm.IsFreeDeathSwitchPending)
            BeginSwitch(true);

        // 按F进入换人（选择阶段内外均可，2026-08-14）；换人界面内F=确认
        if (!_switching && Input.GetKeyDown(KeyCode.F))
        {
            OnSwitchButton();
            return;
        }

        // 换人界面键盘操作（A/D切换，F/回车确认，X/Esc取消）
        if (_switching)
        {
            if (Input.GetKeyDown(KeyCode.A)) MoveSwitchHighlight(-1);
            else if (Input.GetKeyDown(KeyCode.D)) MoveSwitchHighlight(1);
            else if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space)) ConfirmSwitch();
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.X)) CancelSwitch();
        }
    }

    // ================= 目标选择命中数 + 位置色块淡色（2026-08-14） =================
    void RefreshFieldArea(BattleManager bm)
    {
        //位置色块：有人=正常色，空位=淡色（2026-08-14）
        if (bm.Field != null)
        {
            for (int i = 0; i < allyFieldBlocks.Length && i < bm.Field.AllySlots.Count; i++)
            {
                var img = allyFieldBlocks[i];
                if (img == null) continue;
                bool occupied = bm.Field.AllySlots[i] != null && bm.Field.AllySlots[i].IsOccupied;
                img.color = occupied ? AllyFieldBlockColor : AllyFieldBlockEmptyColor;
            }
            for (int i = 0; i < enemyFieldBlocks.Length && i < bm.Field.EnemySlots.Count; i++)
            {
                var img = enemyFieldBlocks[i];
                if (img == null) continue;
                bool occupied = bm.Field.EnemySlots[i] != null && bm.Field.EnemySlots[i].IsOccupied;
                img.color = occupied ? EnemyFieldBlockColor : EnemyFieldBlockEmptyColor;
            }
        }
    }

    // ================= 数据刷新 =================

    void RefreshAllies(BattleManager bm)
    {
        //当前出战角色槽索引（目标选择时高亮施放角色用，2026-08-14）
        CharacterBattleController activeAlly = bm.GetActiveAlly();
        int activeIdx = activeAlly != null && activeAlly.Entity != null
            ? activeAlly.Entity.SlotPosition - 1
            : -1;

        //技能目标方向（2026-08-14）：我方目标技能（治疗/增益单选）时，选中的我方槽位用第一种高亮（黄）
        //2026-08-15 效果级：按"当前待选效果"判定——Self→仅高亮施放者自己；Allies→选中我方槽位也黄；
        //            任意技能选择中施放者槽位恒黄高亮（文档：被选目标及施放技能者此时高亮）
        bool selectDirAlly = false;
        int casterPos = -1;
        if (input != null && input.IsSelecting && input.PendingSkill >= 0)
        {
            var castAlly = input.PendingAlly != null ? input.PendingAlly : bm.GetActiveAlly();
            if (castAlly != null)
            {
                var pend = castAlly.PendingSelectEffect;
                if (pend != null)
                {
                    selectDirAlly = pend.TargetType == "Self"
                        || pend.TargetType == "Allies" || pend.TargetType == "AlliesOnly" || pend.TargetType == "AllyField";
                }
                if (castAlly.Entity != null) casterPos = castAlly.Entity.SlotPosition;
            }
        }

        for (int i = 0; i < allySlots.Length; i++)
        {
            var slot = allySlots[i];
            if (slot == null) continue;
            var ally = bm.GetAllyBySlot(i);
            if (ally == null || ally.Entity == null)
            {
                if (slot.root != null) slot.root.SetActive(false);
                continue;
            }
            if (slot.root != null) slot.root.SetActive(true);
            var ent = ally.Entity;
            if (slot.nameText != null) slot.nameText.text = $"角色{ent.EntityID}";
            float maxHp = ent.TotalHP;
            if (slot.hpBar != null) slot.hpBar.fillAmount = maxHp > 0 ? Mathf.Clamp01(ent.CurrentHP / maxHp) : 0;
            if (slot.hpText != null) slot.hpText.text = $"{Mathf.CeilToInt(ent.CurrentHP)}/{Mathf.CeilToInt(maxHp)}";
            float maxE = ent.MaxEnergy;
            if (slot.energyBall != null) slot.energyBall.fillAmount = maxE > 0 ? Mathf.Clamp01(ent.CurrentEnergy / maxE) : 0;
            if (slot.energyText != null) slot.energyText.text = $"{Mathf.CeilToInt(ent.CurrentEnergy)}/{Mathf.CeilToInt(maxE)}";
            //高亮（2026-08-14）：
            //  - 当前出战角色：常态化【第二种高亮（绿色）】，目标选择阶段=施放角色（同一高亮）
            //  - 换人界面选中角色：黄色高亮（原样式）
            // 目标选择且技能目标为我方：选中的我方槽位=第一种高亮（黄，覆盖出战绿）
            //2026-08-15：选择中施放者槽位恒黄高亮（任何技能方向；1234爆发施放者可为非出战角色）
            bool allySelected = selectDirAlly && input.IsSelecting
                && input.Selector.CurrentSelection != null && input.Selector.CurrentSelection.Contains(i + 1);
            bool isCaster = input != null && input.IsSelecting && casterPos == i + 1;
            if (slot.highlight != null)
            {
                if (_switching)
                {
                    slot.highlight.enabled = _switchIndex == i;
                    slot.highlight.color = SwitchHighlightColor;
                }
                else if (allySelected || isCaster)
                {
                    slot.highlight.enabled = true;
                    slot.highlight.color = SwitchHighlightColor;
                }
                else
                {
                    slot.highlight.enabled = i == activeIdx;
                    slot.highlight.color = ActiveAllyHighlightColor;
                }
            }
            RefreshStatusBar(slot.statusContainer, ent.GetStatusList(), _allyStatusIcons[i]);
        }
    }

    void RefreshEnemies(BattleManager bm)
    {
        // 敌方槽位：从右往左编号1~5，UI槽位从左到右排 → 槽位i对应位置(5-i)，最右槽=位置1
        for (int i = 0; i < enemySlots.Length; i++)
        {
            var slot = enemySlots[i];
            if (slot == null) continue;
            int pos = enemySlots.Length - i;
            EnemyBattleController enemy = null;
            foreach (var e in bm.Enemies)
            {
                if (e != null && e.Entity != null && e.Entity.SlotPosition == pos) { enemy = e; break; }
            }
            if (enemy == null || enemy.Entity == null)
            {
                if (slot.root != null) slot.root.SetActive(false);
                continue;
            }
            if (slot.root != null) slot.root.SetActive(true);
            var ent = enemy.Entity;
            if (slot.nameText != null) slot.nameText.text = $"敌{ent.EntityID}";
            if (slot.lvText != null) slot.lvText.text = $"Lv.{ent.Level}";
            float maxHp = ent.TotalHP;
            if (slot.hpBar != null) slot.hpBar.fillAmount = maxHp > 0 ? Mathf.Clamp01(ent.CurrentHP / maxHp) : 0;
            if (slot.hpText != null) slot.hpText.text = $"{Mathf.CeilToInt(ent.CurrentHP)}/{Mathf.CeilToInt(maxHp)}";
            RefreshStatusBar(slot.statusContainer, ent.GetStatusList(), _enemyStatusIcons[i]);
        }
    }

    void RefreshAP(BattleManager bm)
    {
        var ally = bm.GetActiveAlly();
        if (ally == null) return;
        var ap = ally.APManager;
        if (apText != null) apText.text = $"{ap.CurrentAP}/{ap.MaxAP}";
        if (apBar != null) apBar.fillAmount = ap.MaxAP > 0 ? Mathf.Clamp01((float)ap.CurrentAP / ap.MaxAP) : 0;
        if (apCostHint != null)
        {
            bool selecting = input != null && input.IsSelecting && input.PendingSkill >= 0;
            if (selecting)
            {
                int cost = ally.GetBoundSkillAPCost(input.PendingSkill);
                apCostHint.gameObject.SetActive(true);
                apCostHint.text = $"-{cost}";
                apCostHint.color = ap.CanAfford(cost) ? Color.green : Color.red;
            }
            else apCostHint.gameObject.SetActive(false);
        }
    }

    void RefreshButtons(BattleManager bm)
    {
        var ally = bm.GetActiveAlly();
        if (ally == null) return;
        SetButtonState(btnAttack, ally.CanUseNormalAttack());
        SetButtonState(btnHeavy, ally.CanUseHeavyAttack());
        SetButtonState(btnSkill, ally.CanUseSkill());
        SetButtonState(btnBurst, ally.CanUseBurst());
        SetButtonState(btnSwitch, bm.CanSwitchActiveAlly());
        if (btnSkillNames != null)
        {
            for (int i = 0; i < btnSkillNames.Length && i < 4; i++)
                if (btnSkillNames[i] != null)
                    btnSkillNames[i].text = ally.GetBoundSkillName(i);
        }

        // 冷却数字：普攻/重击/战技按钮（右上角红字，冷却中显示剩余回合）
        if (btnCooldowns != null)
        {
            for (int i = 0; i < btnCooldowns.Length && i < 4; i++)
            {
                if (btnCooldowns[i] == null) continue;
                int cd = ally.GetCooldownRemaining(i);
                btnCooldowns[i].gameObject.SetActive(cd > 0);
                btnCooldowns[i].text = cd.ToString();
            }
        }
        // 爆发冷却：当前出战角色的能量球上显示
        if (allyBurstCooldowns != null)
        {
            int activeIdx = ally.Entity != null ? ally.Entity.SlotPosition - 1 : -1;
            for (int i = 0; i < allyBurstCooldowns.Length; i++)
            {
                if (allyBurstCooldowns[i] == null) continue;
                bool show = i == activeIdx;
                int cd = show ? ally.GetCooldownRemaining(3) : 0;
                allyBurstCooldowns[i].gameObject.SetActive(show && cd > 0);
                if (show) allyBurstCooldowns[i].text = cd.ToString();
            }
        }
        // 使用次数蓝点：MaxCharge>1 时按钮下方显示，用掉一个少一个，恢复时加1
        if (btnChargeDots != null)
        {
            for (int i = 0; i < btnChargeDots.Length && i < 4; i++)
            {
                var container = btnChargeDots[i];
                if (container == null) continue;
                int max = ally.GetBoundSkillMaxCharge(i);
                int cur = ally.GetBoundSkillCurrentCharge(i);
                bool show = max > 1;
                container.gameObject.SetActive(show);
                if (!show) continue;
                int childCount = container.childCount;
                for (int d = 0; d < max; d++)
                {
                    Image dot = null;
                    if (d < childCount) dot = container.GetChild(d).GetComponent<Image>();
                    if (dot == null)
                    {
                        var go = new GameObject("ChargeDot", typeof(RectTransform), typeof(Image));
                        go.transform.SetParent(container, false);
                        dot = go.GetComponent<Image>();
                        dot.color = new Color(0.3f, 0.6f, 1f, 1f); // 小蓝点
                        dot.raycastTarget = false;
                        dot.rectTransform.sizeDelta = new Vector2(12, 12);
                    }
                    dot.gameObject.SetActive(d < cur);
                }
                for (int d = max; d < childCount; d++)
                    container.GetChild(d).gameObject.SetActive(false);
            }
        }
    }

    void RefreshOverlay(BattleManager bm)
    {
        bool selecting = input != null && input.IsSelecting;
        if (dimOverlay != null) dimOverlay.enabled = selecting || _switching;

        // 2026-08-14：技能目标为我方（治疗/增益单选）时，不高亮敌方——我方选中高亮在 RefreshAllies
        // 2026-08-15 效果级：按"当前待选效果"判定——Self/Allies 不高亮敌方，只高亮施放者自己（RefreshAllies）
        bool enemySelecting = false;
        if (selecting && input.PendingSkill >= 0)
        {
            var castAlly = input.PendingAlly != null ? input.PendingAlly : bm.GetActiveAlly();
            if (castAlly != null && castAlly.PendingSelectEffect != null)
            {
                var pend = castAlly.PendingSelectEffect;
                enemySelecting = pend.TargetType == "Enemy" || pend.TargetType == "EnemyField";
            }
        }

        // 目标选择：被选中敌人位置高亮（黄色）
        // 高亮槽位与敌方信息槽位一一对应：槽位i=位置(5-i)，最右槽=位置1
        // 2026-08-14：按实际命中位置高亮——主目标 + 溅射扩展位（有敌人）
        if (enemyHighlights != null)
        {
            var sel = enemySelecting ? GetActualHitPositions(input.PendingSkill, input.Selector.CurrentSelection) : null;
            for (int i = 0; i < enemyHighlights.Length; i++)
            {
                if (enemyHighlights[i] == null) continue;
                bool on = sel != null && sel.Contains(enemyHighlights.Length - i);
                enemyHighlights[i].enabled = on;
                if (on) enemyHighlights[i].color = Color.yellow;
            }
        }

        // 当前目标显示：敌方位置色块黄框（边框 Image）
        if (enemyFieldBorders != null)
        {
            var sel = enemySelecting ? GetActualHitPositions(input.PendingSkill, input.Selector.CurrentSelection) : null;
            for (int i = 0; i < enemyFieldBorders.Length; i++)
            {
                if (enemyFieldBorders[i] == null) continue;
                enemyFieldBorders[i].enabled = sel != null && sel.Contains(i + 1);
            }
        }
    }

    /// <summary>实际命中位置 = 当前选择组合 + 溅射扩展位（有敌人），2026-08-14。</summary>
    List<int> GetActualHitPositions(int skillType, List<int> selected)
    {
        var list = new List<int>();
        if (selected == null) return list;
        list.AddRange(selected);
        CharacterBattleController ally = null;
        if (input != null && input.PendingAlly != null) ally = input.PendingAlly;
        if (ally == null && BattleManager.Instance != null) ally = BattleManager.Instance.GetActiveAlly();
        if (ally == null) return list;
        var splash = ally.GetEffectSplash(ally.PendingSelectEffect); // 2026-08-15 效果级：Splash 从"当前待选效果"读
        if (splash == null) return list;
        var bm = BattleManager.Instance;
        var added = new HashSet<int>(selected);
        foreach (var pos in selected)
        {
            int left = Mathf.Max(1, pos - splash.Value.Left);
            int right = Mathf.Min(5, pos + splash.Value.Right);
            for (int p = left; p <= right; p++)
            {
                if (p == pos || selected.Contains(p)) continue;
                if (bm != null && bm.IsEnemyAliveAt(p) && added.Add(p)) list.Add(p);
            }
        }
        return list;
    }

    /// <summary>状态栏刷新：最新10条（新到旧），纯色占位图标，剩1回合闪烁。</summary>
    void RefreshStatusBar(Transform container, List<StatusInstance> statuses, List<Image> icons)
    {
        if (container == null) return;
        var sorted = new List<StatusInstance>(statuses);
        sorted.Sort((a, b) => b.ApplyOrder.CompareTo(a.ApplyOrder));
        int show = Mathf.Min(sorted.Count, 10);
        while (icons.Count < show) icons.Add(CreateStatusIcon(container));
        for (int i = 0; i < icons.Count; i++)
        {
            if (i < show)
            {
                var inst = sorted[i];
                icons[i].gameObject.SetActive(true);
                var c = statusColor;
                if (inst.RemainingPhaseCount <= 1)
                    c.a = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.time * 6f)); // 剩1回合淡入淡出闪烁
                icons[i].color = c;
            }
            else icons[i].gameObject.SetActive(false);
        }
    }

    Image CreateStatusIcon(Transform parent)
    {
        var go = new GameObject("StatusIcon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = statusColor;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(24, 24);
        return img;
    }

    void SetButtonState(Button b, bool usable)
    {
        if (b != null) b.interactable = usable;
    }

    // ================= 换人界面 =================

    void OnSwitchButton()
    {
        //选择阶段内也允许进入换人（确认时 UISwitchCharacter 会退出目标选择，2026-08-14）
        if (_switching) { ConfirmSwitch(); return; }
        BeginSwitch(false);
    }

    void BeginSwitch(bool forcedDeathSwitch)
    {
        var bm = BattleManager.Instance;
        if (bm == null) return;
        //换人界面为模态界面：停用战斗输入组件（BattleInputController 的 Update 停止执行，2026-08-14）
        if (input != null) input.enabled = false;
        _switching = true;
        _forcedDeathSwitch = forcedDeathSwitch;
        CharacterBattleController active = bm.GetActiveAlly();
        _switchIndex = active != null && active.Entity != null
            ? Mathf.Clamp(active.Entity.SlotPosition - 1, 0, bm.AllySlotCount - 1)
            : 0;

        // 强制换人从编队编号1开始，选择第一个存活角色；前面的角色阵亡或为空就继续向后找。
        if (_forcedDeathSwitch)
        {
            for (int i = 0; i < bm.AllySlotCount; i++)
            {
                if (!bm.CanSwitchActiveAllyTo(i)) continue;
                _switchIndex = i;
                break;
            }
        }
        LogManager.Log(LogCategory.UI,
            $"进入{(_forcedDeathSwitch ? "免费强制" : string.Empty)}换人界面，当前选中 {_switchIndex}");
    }

    void MoveSwitchHighlight(int dir)
    {
        var bm = BattleManager.Instance;
        if (bm == null) return;
        _switchIndex = bm.FindNextLivingAllySlot(_switchIndex, dir);
    }

    void ConfirmSwitch()
    {
        var bm = BattleManager.Instance;
        if (bm == null || input == null) return;
        if (!bm.CanSwitchActiveAllyTo(_switchIndex))
        {
            LogManager.LogWarning(LogCategory.UI, $"切换出战角色失败 -> {_switchIndex}");
            return;
        }

        bool freeDeathSwitch = bm.IsFreeDeathSwitchPending;
        if (!bm.TrySwitchActiveAllyWithAPCost(_switchIndex))
        {
            if (!freeDeathSwitch && (bm.APManager == null || !bm.APManager.CanAfford(BattleManager.SwitchAPCost)))
                LogManager.Log(LogCategory.Action, $"切换角色失败：AP不足（需要{BattleManager.SwitchAPCost}AP）");
            else
                LogManager.LogWarning(LogCategory.UI, $"切换出战角色失败 -> {_switchIndex}");
            return;
        }

        input.UICancelSelection();
        LogManager.Log(LogCategory.UI, $"切换出战角色 -> {_switchIndex}{(freeDeathSwitch ? "（阵亡免费换人）" : string.Empty)}");
        input.enabled = true; //退出换人界面：恢复战斗输入（2026-08-14）
        _switching = false;
        _forcedDeathSwitch = false;
    }

    void CancelSwitch()
    {
        if (_forcedDeathSwitch || (BattleManager.Instance != null && BattleManager.Instance.IsFreeDeathSwitchPending))
            return;
        if (input != null) input.enabled = true; //退出换人界面：恢复战斗输入（2026-08-14）
        _switching = false;
        _forcedDeathSwitch = false;
    }
}
