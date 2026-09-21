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

固定流程：先执行“同步 UI 配表到 StreamingAssets”，再执行“按配表同步所需美术资源”，最后执行“根据 BattleField1 表生成 Scene”。

## 字体

项目 UI 唯一字体源为 `Assets/Fonts/zh-cn.ttf`，TextMesh Pro 使用其动态字体资产 `Assets/Fonts/zh-cn SDF.asset`。所有 UI 生成器和 PSD 导入器必须显式使用该 TMP 字体资产；项目 TMP 默认字体也指向它，不再使用 Liberation Sans 生成新文字。
