using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 战斗界面一键生成工具（纯色占位版）。
/// 用法：Tools → 战斗界面 → 一键生成战斗UI
/// 生成 1920×1080 设计平面的完整战斗界面（Canvas + 5大区域 + DimOverlay + 按钮），
/// 并自动挂 UIBattleController、填好全部引用。
/// 重复生成会先清理旧生成物（按 "UIGen_" 前缀识别），可作重置。
/// 布局/颜色/字号参数集中在下方常量区，改完重新生成即可。
/// </summary>
public static class UIBuilder
{
    // ============ 布局参数（改这里后重新生成） ============
    const float W = 1920f, H = 1080f;
    const string TAG = "UIGen_";
    static readonly Color SlotBg = new Color(0.13f, 0.13f, 0.16f, 0.85f);
    static readonly Color HpColor = new Color(0.25f, 0.85f, 0.4f, 1f);
    static readonly Color EnemyHpColor = new Color(0.9f, 0.28f, 0.28f, 1f);
    static readonly Color EnergyColor = new Color(0.3f, 0.65f, 1f, 1f);
    static readonly Color DimColor = new Color(0f, 0f, 0f, 0.6f);
    static readonly Color HighlightColor = new Color(1f, 0.95f, 0.6f, 0.4f);
    static readonly Color BtnBg = new Color(0.3f, 0.3f, 0.38f, 1f);
    static readonly Color FieldBg = new Color(0.35f, 0.4f, 0.5f, 0.9f);
    static readonly Color EnemyFieldBg = new Color(0.55f, 0.35f, 0.35f, 0.9f);
    static readonly Color TextColor = Color.white;
    const int FontSize = 26;
    const int SmallFont = 18;

    [MenuItem("Tools/战斗界面/一键生成战斗UI")]
    public static void Build()
    {
        if (TMP_Settings.instance == null)
        {
            EditorUtility.DisplayDialog("缺少TMP资源",
                "请先导入 TMP Essential Resources：\nWindow → TextMeshPro → Import TMP Essential Resources\n然后再生成。", "好");
            return;
        }

        var canvas = EnsureCanvas();
        var ctrl = canvas.GetComponent<UIBattleController>();
        if (ctrl == null) ctrl = canvas.gameObject.AddComponent<UIBattleController>();

        CleanOld(canvas.transform);

        // 纯白背景（最底层）
        BuildBackground(canvas.transform);
        // DimOverlay 遮罩放在背景之后（层级底）：进目标选择时只压暗背景，不遮角色/敌人/按钮
        BuildDimOverlay(canvas.transform, ctrl);

        BuildAllySlots(canvas.transform, ctrl);
        BuildEnemySlots(canvas.transform, ctrl);
        BuildFieldArea(canvas.transform, ctrl);
        BuildAPArea(canvas.transform, ctrl);
        BuildButtons(canvas.transform, ctrl);

        EditorUtility.SetDirty(canvas.gameObject);
        LogManager.Log(LogCategory.Build, "战斗UI生成完成（纯色占位版）");
    }

    // ================= Canvas =================
    static Canvas EnsureCanvas()
    {
        var existing = Object.FindObjectOfType<Canvas>();
        if (existing != null) return existing;

        var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(W, H);
        scaler.matchWidthOrHeight = 1f;
        LogManager.Log(LogCategory.Build, "创建 Canvas（1920×1080, Match=1）");
        return canvas;
    }

    static void CleanOld(Transform parent)
    {
        var olds = new System.Collections.Generic.List<GameObject>();
        foreach (Transform t in parent)
            if (t.name.StartsWith(TAG)) olds.Add(t.gameObject);
        foreach (var o in olds) Object.DestroyImmediate(o);
    }

    // ================= 背景（纯白，最底层） =================
    static void BuildBackground(Transform canvas)
    {
        var bg = CreateImage(TAG + "Bg", canvas, Color.white, true, Image.Type.Simple);
        SetRect(bg.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        bg.raycastTarget = false;
    }

    // ================= 角色信息区（底部一行，与技能按钮同高度；左→右1~4） =================
    static void BuildAllySlots(Transform canvas, UIBattleController ctrl)
    {
        float slotW = 275, slotH = 84;
        float[] xs = { -157f, 154f, 468f, 783f }; // 底部右侧，从右往左铺，1~4从左往右（场景手动调后：宽275、y=52）
        if (ctrl.allyBurstCooldowns == null || ctrl.allyBurstCooldowns.Length != 4)
            ctrl.allyBurstCooldowns = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
        {
            var root = NewUI(TAG + "AllySlot_" + i, canvas, SlotBg, true);
            SetRect(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(xs[i], 52), new Vector2(slotW, slotH)); // y=52按场景手动调值

            var slot = new UIBattleController.AllyUISlot();
            slot.root = root.gameObject;

            slot.nameText = CreateText(TAG + "Name", root, "角色" + (i + 1), FontSize, TextAlignmentOptions.Left);
            SetRect(slot.nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10, -6), new Vector2(180, 30));

            // 血条底槽（黑色=损失部分，白色边框包着总血条）——角色血条坐标按场景值(pos 0,6 / 宽-28)
            var hpRoot = CreateImage(TAG + "HpBarRoot", root, Color.black, true, Image.Type.Simple);
            SetRect(hpRoot.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(-28, 14)); // 拉伸：宽=槽宽-28
            hpRoot.raycastTarget = false;
            var hpOutline = hpRoot.gameObject.AddComponent<Outline>();
            hpOutline.effectColor = Color.white;
            hpOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // 血量填充（红色，fillAmount 跟随实际血量收缩，露出黑色底槽=损失部分）
            slot.hpBar = CreateImage(TAG + "HpBar", hpRoot.transform, HpColor, true, Image.Type.Filled);
            slot.hpBar.fillMethod = Image.FillMethod.Horizontal;
            slot.hpBar.fillOrigin = (int)Image.OriginHorizontal.Left;
            SetRect(slot.hpBar.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-2, -2)); // 内边距2px，收缩露出黑底

            slot.hpText = CreateText(TAG + "HpText", root, "100/100", SmallFont, TextAlignmentOptions.Center);
            SetRect(slot.hpText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(160, 20)); // 居中于血条

            slot.energyBall = CreateImage(TAG + "EnergyBall", root, EnergyColor, true, Image.Type.Filled);
            slot.energyBall.fillMethod = Image.FillMethod.Radial360;
            slot.energyBall.raycastTarget = true; // 能量球=爆发按钮，需可点击
            var ballBtn = root.gameObject.AddComponent<Button>();
            ballBtn.targetGraphic = slot.energyBall;
            SetRect(slot.energyBall.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10, -10), new Vector2(46, 46));
            slot.burstButton = ballBtn;

            // 爆发冷却数字（能量球右上角，冷却中显示剩余回合）
            var cdTxt = CreateText(TAG + "BurstCooldown", slot.energyBall.transform, "", SmallFont, TextAlignmentOptions.Center);
            cdTxt.color = Color.red;
            SetRect(cdTxt.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-2, -2), new Vector2(36, 22));
            cdTxt.gameObject.SetActive(false);
            ctrl.allyBurstCooldowns[i] = cdTxt;

            slot.energyText = CreateText(TAG + "EnergyText", root, "0/100", SmallFont, TextAlignmentOptions.Center);
            SetRect(slot.energyText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-33, -56), new Vector2(66, 20));

            slot.statusContainer = NewUI(TAG + "StatusContainer", root, Color.clear, true);
            SetRect(slot.statusContainer as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 8), new Vector2(-20, 24)); // 拉伸：宽=槽宽-20
            var layout = slot.statusContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleLeft;

            slot.highlight = CreateImage(TAG + "Highlight", root, HighlightColor, true, Image.Type.Simple);
            slot.highlight.enabled = false; // 物体激活、组件禁用（运行时 enabled 控制，SetActive(false)会整个隐藏）
            SetRect(slot.highlight.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            slot.highlight.transform.SetAsFirstSibling(); // 高亮垫底（图层逻辑）：选中时槽位背景变黄，内容（名字/血条/能量球）显示在上面

            ctrl.allySlots[i] = slot;
        }
    }

    // ================= 敌方信息区（5份，从右至左=位置1~5） =================
    static void BuildEnemySlots(Transform canvas, UIBattleController ctrl)
    {
        float slotW = 300, slotH = 64;
        float[] xs = { -747f, -397f, -47f, 303f, 653f }; // 敌方信息区：场景手动调后整体左移（左侧一排，宽300）
        for (int i = 0; i < 5; i++)
        {
            var root = NewUI(TAG + "EnemySlot_" + i, canvas, SlotBg, true);
            SetRect(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(xs[i], -47), new Vector2(slotW, slotH));

            var slot = new UIBattleController.EnemyUISlot();
            slot.root = root.gameObject;

            // 等级（独立字段，不拼进名字）：名字左侧
            slot.lvText = CreateText(TAG + "LvText", root, "Lv.1", SmallFont, TextAlignmentOptions.Left);
            SetRect(slot.lvText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10, -4), new Vector2(60, 22));

            slot.nameText = CreateText(TAG + "Name", root, "敌" + (i + 1), FontSize, TextAlignmentOptions.Left);
            SetRect(slot.nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76, -4), new Vector2(110, 26));

            // 血条底槽（黑色=损失部分，白色边框包着总血条）
            var hpRoot = CreateImage(TAG + "HpBarRoot", root, Color.black, true, Image.Type.Simple);
            SetRect(hpRoot.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(-24, 14)); // 拉伸：宽=槽宽-24
            hpRoot.raycastTarget = false;
            var hpOutline = hpRoot.gameObject.AddComponent<Outline>();
            hpOutline.effectColor = Color.white;
            hpOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // 血量填充（红色，fillAmount 跟随实际血量收缩，露出黑色底槽=损失部分）
            slot.hpBar = CreateImage(TAG + "HpBar", hpRoot.transform, EnemyHpColor, true, Image.Type.Filled);
            slot.hpBar.fillMethod = Image.FillMethod.Horizontal;
            slot.hpBar.fillOrigin = (int)Image.OriginHorizontal.Left;
            SetRect(slot.hpBar.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-2, -2)); // 内边距2px，收缩露出黑底

            slot.hpText = CreateText(TAG + "HpText", root, "100/100", SmallFont, TextAlignmentOptions.Center);
            SetRect(slot.hpText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(120, 20)); // 居中于血条

            slot.statusContainer = NewUI(TAG + "StatusContainer", root, Color.clear, true);
            SetRect(slot.statusContainer as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(-20, 24)); // 拉伸：宽=槽宽-20
            var layout = slot.statusContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleLeft;

            ctrl.enemySlots[i] = slot;
        }

        // 命中高亮（与敌方槽位一一对应）
        ctrl.enemyHighlights = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var parent = canvas.Find(TAG + "EnemySlot_" + i);
            var hl = CreateImage(TAG + "EnemyHighlight_" + i, parent, HighlightColor, true, Image.Type.Simple);
            hl.enabled = false; // 物体激活、组件禁用
            SetRect(hl.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            hl.transform.SetAsFirstSibling(); // 高亮垫底（图层逻辑）：选中时槽位背景变黄，信息（名字/Lv/血条/状态）显示在上面
            ctrl.enemyHighlights[i] = hl;
        }
    }

    // ================= 战斗区域（占位色块） =================
    static void BuildFieldArea(Transform canvas, UIBattleController ctrl)
    {
        if (ctrl.enemyFieldBorders == null || ctrl.enemyFieldBorders.Length != 5)
            ctrl.enemyFieldBorders = new Image[5];
        if (ctrl.allyFieldBlocks == null || ctrl.allyFieldBlocks.Length != 4)
            ctrl.allyFieldBlocks = new Image[4];
        if (ctrl.enemyFieldBlocks == null || ctrl.enemyFieldBlocks.Length != 5)
            ctrl.enemyFieldBlocks = new Image[5];

        // 我方4位（场景手动调后的坐标：位置1~4）
        float[] allyXs = { 211f, 342f, 492f, 598f };
        float[] allyYs = { -138f, 122f, -138f, 122f };
        for (int i = 0; i < 4; i++)
        {
            // 位置父物体（目录：与敌方位置同结构）
            var root = NewUI(TAG + "AllyFieldRoot_" + i, canvas, Color.clear, true);
            SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(allyXs[i], allyYs[i]), new Vector2(96, 96));
            root.GetComponent<Image>().raycastTarget = false;

            var img = CreateImage(TAG + "AllyField_" + i, root, FieldBg, true, Image.Type.Simple);
            SetRect(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96));
            img.raycastTarget = false;
            ctrl.allyFieldBlocks[i] = img; // 空位淡色用（2026-08-14）
        }
        // 敌方5位（场景手动调后的坐标：位置1在右~5在左）
        float[] enemyXs = { -162f, -300f, -457f, -644f, -807f };
        float[] enemyYs = { -148f, 165f, -138f, 138f, -148f };
        for (int i = 0; i < 5; i++)
        {
            // 位置父物体（目录：边框+色块都挂在这里，整体移动）
            var root = NewUI(TAG + "EnemyFieldRoot_" + i, canvas, Color.clear, true);
            SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(enemyXs[i], enemyYs[i]), new Vector2(116, 116));
            root.GetComponent<Image>().raycastTarget = false;

            // 黄色边框（子物体，居中，默认关；四周各 10px 黄边）
            var border = CreateImage(TAG + "EnemyFieldBorder_" + i, root, Color.yellow, true, Image.Type.Simple);
            border.enabled = false; // 物体激活、组件禁用（运行时 enabled 控制）
            SetRect(border.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(116, 116));
            border.raycastTarget = false;
            ctrl.enemyFieldBorders[i] = border;

            // 色块本体（子物体，居中，盖在边框上）
            var img = CreateImage(TAG + "EnemyField_" + i, root, EnemyFieldBg, true, Image.Type.Simple);
            SetRect(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96));
            img.raycastTarget = false;
            ctrl.enemyFieldBlocks[i] = img; // 空位淡色用（2026-08-14）
        }
    }

    // ================= 右侧面板（换人按钮 + AP栏，最右侧中间靠上） =================
    static void BuildAPArea(Transform canvas, UIBattleController ctrl)
    {
        // 换人按钮：最右侧（场景手动调后坐标）
        var swImg = CreateImage(TAG + "SwitchBtn", canvas, BtnBg, true, Image.Type.Simple);
        swImg.raycastTarget = true;
        SetRect(swImg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50, -370), new Vector2(190, 64));
        ctrl.btnSwitch = swImg.gameObject.AddComponent<Button>();
        ctrl.btnSwitch.targetGraphic = swImg;
        var swTxt = CreateText(TAG + "SwitchBtnText", swImg.transform, "换人", FontSize, TextAlignmentOptions.Center);
        SetRect(swTxt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // AP栏：换人按钮下面一点
        ctrl.apBar = CreateImage(TAG + "ApBar", canvas, EnergyColor, true, Image.Type.Filled);
        ctrl.apBar.fillMethod = Image.FillMethod.Vertical;
        ctrl.apBar.fillOrigin = (int)Image.OriginVertical.Bottom;
        SetRect(ctrl.apBar.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-100, -450), new Vector2(28, 180));

        ctrl.apText = CreateText(TAG + "ApText", canvas, "100/100", SmallFont, TextAlignmentOptions.Center);
        SetRect(ctrl.apText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-100, -580), new Vector2(140, 26));

        // -20消耗提示：绑定在 AP条上，居中显示（目标选择阶段可见）
        ctrl.apCostHint = CreateText(TAG + "ApCostHint", ctrl.apBar.transform, "-20", FontSize, TextAlignmentOptions.Center);
        ctrl.apCostHint.color = Color.green;
        SetRect(ctrl.apCostHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140, 30));
    }

    // ================= 按钮区（底部） =================
    static void BuildButtons(Transform canvas, UIBattleController ctrl)
    {
        // 普攻/重击/战技 从左下角往右铺（场景手动调后：70×70方块）；结束回合单独在中间偏右上方
        string[] names = { "普攻", "重击", "战技", "结束回合" };
        float[] xs = { -844f, -700f, -558f, 831f };
        float[] ys = { 60f, 58f, 58f, 314f };
        float[] ws = { 70f, 70f, 70f, 170f };
        float[] hs = { 70f, 70f, 70f, 64f };
        var btns = new Button[4];
        for (int i = 0; i < 4; i++)
        {
            var img = CreateImage(TAG + "Btn_" + i, canvas, BtnBg, true, Image.Type.Simple);
            img.raycastTarget = true; // 按钮需可点击
            SetRect(img.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(xs[i], ys[i]), new Vector2(ws[i], hs[i]));
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btns[i] = btn;

            var txt = CreateText(TAG + "BtnText_" + i, img.transform, names[i], FontSize, TextAlignmentOptions.Center);
            SetRect(txt.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // 冷却数字（按钮右上角红字，冷却中显示剩余回合）
            var cdTxt = CreateText(TAG + "BtnCooldown_" + i, img.transform, "", SmallFont, TextAlignmentOptions.Center);
            cdTxt.color = Color.red;
            SetRect(cdTxt.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6, -6), new Vector2(44, 26));
            cdTxt.gameObject.SetActive(false);

            // 使用次数蓝点容器（按钮下方，MaxCharge>1 时由 UIBattleController 生成蓝点）
            var dotContainer = NewUI(TAG + "BtnChargeDots_" + i, img.transform, Color.clear, true);
            SetRect(dotContainer, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(180, 16));
            var hl = dotContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 4f;
            hl.childAlignment = TextAnchor.MiddleCenter;
        }
        ctrl.btnAttack = btns[0];
        ctrl.btnHeavy = btns[1];
        ctrl.btnSkill = btns[2];
        ctrl.btnBurst = null; // 爆发按钮=各角色能量槽，无独立按钮
        ctrl.btnSwitch = null; // 换人按钮在右侧面板
        ctrl.btnEndTurn = btns[3];
        ctrl.btnSkillNames = new TMP_Text[4];
        for (int i = 0; i < 3; i++)
            ctrl.btnSkillNames[i] = canvas.Find(TAG + "Btn_" + i).GetComponentInChildren<TextMeshProUGUI>();
        ctrl.btnSkillNames[3] = null; // 爆发无独立按钮
        ctrl.btnCooldowns = new TMP_Text[4];
        for (int i = 0; i < 3; i++)
        {
            var cdObj = canvas.Find(TAG + "Btn_" + i).Find(TAG + "BtnCooldown_" + i);
            ctrl.btnCooldowns[i] = cdObj != null ? cdObj.GetComponent<TextMeshProUGUI>() : null;
        }
        ctrl.btnCooldowns[3] = null; // 爆发走能量球（allyBurstCooldowns）
        ctrl.btnChargeDots = new RectTransform[4];
        for (int i = 0; i < 3; i++)
        {
            var dotObj = canvas.Find(TAG + "Btn_" + i).Find(TAG + "BtnChargeDots_" + i);
            ctrl.btnChargeDots[i] = dotObj != null ? dotObj.GetComponent<RectTransform>() : null;
        }
        ctrl.btnChargeDots[3] = null; // 爆发（能量球）暂不支持次数点
    }

    // ================= DimOverlay（底层遮罩，只压暗背景，不遮角色/敌人/按钮） =================
    static void BuildDimOverlay(Transform canvas, UIBattleController ctrl)
    {
        ctrl.dimOverlay = CreateImage(TAG + "DimOverlay", canvas, DimColor, true, Image.Type.Simple);
        ctrl.dimOverlay.enabled = false; // 物体激活、组件禁用（运行时 enabled 控制）
        SetRect(ctrl.dimOverlay.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    // ================= 组件工厂 =================
    static RectTransform NewUI(string name, Transform parent, Color color, bool show)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        go.SetActive(show);
        return go.GetComponent<RectTransform>();
    }

    static Image CreateImage(string name, Transform parent, Color color, bool show, Image.Type type)
    {
        var rt = NewUI(name, parent, color, show);
        var img = rt.GetComponent<Image>();
        img.type = type;
        return img;
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, string content, int size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var txt = go.GetComponent<TextMeshProUGUI>();
        txt.font = ProjectUiFont.LoadOrCreate();
        txt.text = content;
        txt.fontSize = size;
        txt.color = TextColor;
        txt.alignment = align;
        txt.raycastTarget = false;
        return txt;
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
