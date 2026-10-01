# UI 运行时配表约定

`BattleField1.csv` 是 BattleField1 界面的唯一正式布局表。PSD 只负责刷新原始几何；Unity 的资源同步与 Scene 生成都只读取发布后的 `Assets/StreamingAssets/Charts/UI/BattleField1.csv`，不会运行时读取仓库 `Charts` 或旧的 `art_assets` 表。

## 严格尺寸规则

`SizeMode` 只能填写以下值：

| 值 | 允许的尺寸变化 |
|---|---|
| `Fixed` | 不允许缩放，保持表内宽高 |
| `Uniform` | 只允许等比缩放，禁止独立拉伸 X/Y |
| `StretchX` | 只允许 X 轴伸缩，Y 轴保持表内高度 |
| `StretchY` | 只允许 Y 轴伸缩，X 轴保持表内宽度 |
| `StretchXY` | 允许 X、Y 两轴分别伸缩 |

`AllowCrop` 必须显式填写 `True` 或 `False`。`True` 表示保持图片比例并用父矩形裁剪溢出部分；它不表示允许拉伸图片。当前只给需要充满框体的背景和头像开启裁剪。

## 资源规则

`AssetMode` 只能填写 `Ignore`、`Group`、`Instance`、`Text`、`Generated`、`Streaming` 或 `DynamicStreaming`。后两种必须填写 `StreamingAssetPath`，路径从 StreamingAssets 根目录开始，例如 `art_assets/UI/BattleField1/UI_components/Cancel.png`。

花括号表示运行时绑定参数：`{CharacterID}`、`{EnemyID}`、`{BattleID}`、`{SkillIcon}`、`{BurstIcon}`、`{ElementIcon}`、`{StatusIconPath}`。资源同步工具会按这些模式从源 `art_assets` 展开并复制候选文件；Scene 只保存模式，战斗数据显示时再解析成具体路径。

## Unity 工具入口与手动修改保护

UI 菜单只保留 `Tools/UI/导入美术资源` 与 `Tools/UI/更新 UI 配置`。前者先用既有同步流程发布源表，再按发布表导入美术资源；后者一键只发布 `BattleField1_animation.csv`，没有差异窗口、逐项勾选或三方合并。程序只读取 `Assets/StreamingAssets/Charts/UI`，不要直接编辑发布副本。

职责严格分离：PSD 初建和 Unity 手调负责位置、尺寸、锚点、旋转、缩放、层级、RGB 颜色、字体和资源引用；动画表只负责时长、动画位移距离、闪烁幅度和状态透明度。更新工具不修改 RectTransform、不创建/删除物体、不保存或重建 Scene。后续手动摆放并保存的位置不会因更新配置复原；只有明确要求重新按 PSD 排版时才进行布局重建。

编辑模式只发布参数，不接触场景；下次运行自动读取。运行模式手动点击更新时，只重载四类动画参数，不重置动画起始坐标。AP 提示以启动时场景中的位置为基准浮动，颜色也从场景文字组件取得；血条启动时不再按表重设填充的颜色或内边距。

统一参数表键：`AP.*`（余额过渡、增减浮动、预扣闪烁）、`Health.TransitionDuration`、`Selection.*`（遮罩淡入和目标切换）、`Normal/Freeze/Cooldown.*Opacity`（0–255，RGB 不变）。时长单位秒、FloatDistance 为设计像素、PulseMinAlpha 为 0–1。

旧布局、AP/血条样式与状态表保留作 PSD 初建/历史参考，不再作为日常外观更新源；旧同步身份组件不再参与更新，也不必为了迁移删除场景组件。

## 字体

文本行可通过 `FontSize` 固定字号、`FontStyle=Bold` 设置粗体。爆发按键提示 `Burst_Key` 统一为 18 号粗体、居中，关闭自动缩字号；模板实例和后续重新生成均沿用。

## 圆角血量填充

按 PSD 矢量形状修正外框：14px 高的底槽采用 7px 圆角，角色使用 3px 深棕外描边和 1px 浅色外描边，敌人使用 3px 深红内描边和 1px 更深红内描边。`PsdHealthBarFrame` 保留原 Image 引用、位置和尺寸，仅替换矩形绘制；原上下光影保持原层级。

爆发按钮保留 PSD 深色内边。四层原始直径 104/96/91/87 统一为偶数 104/96/92/88，爆发图标使用 88×88。元素背景、深色底圆和图标均以浅色 96px 圆的中心对齐，pivot 为 (0.5,0.5)，不改变整个按钮和角色栏的位置。此为一次性 PSD 布局修正，不由“更新 UI 配置”反复执行。

主场景已在 4 个角色栏和 5 个敌人栏的底槽下挂接 `HealthFill` / `BattlefieldHealthBar`，按阵营和槽位绑定真实血量，角色栏数字直接显示当前 HP。新增填充位于原底槽之上、原柔光和阴影之下，不移动既有物体。填充的颜色、RectTransform 和内边距可以在 Unity 调整，运行时和参数更新都不会复原这些值。满血、掉血和回血使用 `Health.TransitionDuration`；0 血填充为空。空槽继续随整个栏隐藏。盾条和 Boss 没有混接生命值。

源参数表现位于 `Charts/UI/Battlefield1/BattleField1_animation.csv`。同步工具识别此分组目录，发布路径保持为 `Assets/StreamingAssets/Charts/UI/BattleField1_animation.csv`。

`BattleField1_health_styles.csv` 仅保留初建参考。运行时只从统一动画表读取血量过渡时间，颜色和内边距以 Unity 场景为准；血量数值始终由战斗实体提供。

颜色读取自 `BattleField1.psd` 血条组内“矩形 3”的矢量填充属性：角色 RGB(105,216,44) / `#69D82C`，敌人 RGB(222,78,50) / `#DE4E32`，不是光影叠加后的截图取色。

原有底槽、上下光影与其他元素保持不变。新增 `HealthFill` 在底槽之上、原光影之下，四个内边距和过渡时间由此表控制。当前内边距是适配现有底槽的可调实现参数，不冒充 PSD 属性。填充宽度按实际血量比例变化，两端重绘圆角；极低血量时圆角半径自适应缩小，不用最小宽度伪造血量，0 血不画填充。读取 `BattleEntity.CurrentHP/TotalHP`，按阵营和 SlotPosition 绑定。角色血量数字同步更新；空槽仍由原有角色/敌人栏显隐逻辑管理。`ElementalShield`、Boss、多血槽和生命之契不接入此血量组件。

## 新版敌人血条分层（2026-09-27）

敌人血条以 `Battlefield_UI_components.psd/敌人血条` 为准，替代上文旧的程序描边和独立上下光影方案。我方血条不变。

- `tools/export_enemy_health.py` 按 PSD 剪贴、正片叠底和柔光合成投影、底色、两层内阴影、两层内描边及上下光影，导出 `art_assets/UI/BattleField1/UI_components/HP/EnemyHealthFill.png`。不能把两个 800×800 的内阴影图层直接当作 UI 图片叠放。
- 成品为 180×22，实际血条为 172×14；投影留白左 3、下 7、右 5、上 1。水平端帽为左 10、右 12，含各自留白。中段沿 X 伸缩，端帽保持原尺寸；极低血量时端帽一起缩小，不设置虚假的最小血量宽度，0 血不绘制。
- PSD 颜色与混合结果已烘入 PNG，不再乘一次红色。旧敌人上下光影对象保留但停用，底槽只画暗色空槽，不重复画描边。五个敌人栏的整体位置不变。
- 源表 `BattleField1.csv` 的敌人 `ValueBar` 记录该资源的 `StreamingAssetPath`，由现有“导入美术资源”工具发布；程序只从 StreamingAssets 加载。请导入美术资源后查看效果，不需要重建 Scene。“更新 UI 配置”仍只更新动画参数，不移动组件。

## AP 显示与动画

AP 正文和增减提示按 PSD 使用 29.16667 号字，关闭自动缩字；`TextAlignment` 分别为 `MidlineLeft`、`MidlineRight`。增减提示共用扣除提示的位置，互斥显示。PSD 描边为 3 像素外描边、`#716758`、100% 不透明，通过 `OutlinePixels` 和 `OutlineColor` 配置，以每个文本独立的 TMP SDF 材质实现，不修改共享字体。

AP 使用同一个 `zh-cn.ttf` 生成的 `zh-cn Outline SDF.asset`，图集留白为 32，避免外描边溢出字形图集造成矩形色块；其他文字继续使用原字体资产。

`BattleField1_ap_styles.csv` 仅保留原始样式参考，运行时不再读取。PSD 的 AP 正文颜色是 `#DDD2C1`，扣除为 `#FF6969`，增加为 `#6FDA32`；场景可以手动调整这三种颜色，程序不从表覆盖。固定遵循扣除提示、增加提示各自颜色，不按余额是否足够换色。

目标选择时显示当前施放者技能的预扣值并轻微闪动，总 AP 仍为真实余额；取消立即隐藏，不修改 AP。实际扣减或增加后，总数默认在 0.3 秒内过渡，增减提示在 0.8 秒内上浮 24 个设计像素并淡出。使用非缩放时间。多段效果已经支付后不再次预扣。时长、动画距离和闪烁幅度在统一动画表调整，颜色留在场景中调整。

项目 UI 唯一字体源为 `Assets/Fonts/zh-cn.ttf`，TextMesh Pro 使用其动态字体资产 `Assets/Fonts/zh-cn SDF.asset`。所有 UI 生成器和 PSD 导入器必须显式使用该 TMP 字体资产；项目 TMP 默认字体也指向它，不再使用 Liberation Sans 生成新文字。
