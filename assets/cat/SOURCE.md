# 原猫美术来源与像素处理

本目录使用 **MMmmmoko/Bongo-Cat-Mver 的原始 2D keyboard 模式美术**，固定提交：

[`4da0b9468ad3b6ffaa096eba3f080501d6ab0b5c`](https://github.com/MMmmmoko/Bongo-Cat-Mver/tree/4da0b9468ad3b6ffaa096eba3f080501d6ab0b5c/BongoCatMver/img/keyboard)。

原仓库根目录 [LICENSE](https://github.com/MMmmmoko/Bongo-Cat-Mver/blob/4da0b9468ad3b6ffaa096eba3f080501d6ab0b5c/LICENSE) 为 MIT，Copyright (c) 2020 Han Zhang。完整文本保留在 [LICENSE-MIT.txt](LICENSE-MIT.txt)。原 PNG 下载时逐文件核对了该提交 Git tree 中的 blob SHA；本地 SHA-256 见 [source/SHA256SUMS.txt](source/SHA256SUMS.txt)。

| 本地原文件 | 原仓库相对路径 |
| --- | --- |
| `source/bg.png` | `BongoCatMver/img/keyboard/bg.png` |
| `source/left-up.png` | `BongoCatMver/img/keyboard/lefthand/leftup.png` |
| `source/left-down.png` | `BongoCatMver/img/keyboard/lefthand/0.png` |
| `source/right-up.png` | `BongoCatMver/img/keyboard/righthand/rightup.png` |
| `source/right-down.png` | `BongoCatMver/img/keyboard/righthand/0.png` |

每张原图都是 612 × 354。左右沿用原仓库的命名，即猫的左右，而非屏幕的左右。

派生过程由 [ExtractArtwork.cs](ExtractArtwork.cs) 完整保留：沿原图的斜桌沿拆出身体；按原顺序叠加左右爪；取原透明通道形成 4 张玻璃遮罩；从原像素剥离白色填充，保留原轮廓、眼睛、嘴和粉色爪垫。身体、脸、耳朵、爪子都没有重新绘制。桌沿与原键盘也直接提取原像素，只去除白色桌面背景。

- `body-mask-0..3.png`：381 × 268 的灰度 alpha 遮罩，白色 RGB 仅供查看，运行时读取 alpha。
- `body-ink-0..3.png`：对应遮罩的原线条、五官与爪垫，透明底。
- `desk-ink.png`：612 × 354 的原桌沿和键盘线条。
- `geometry.txt`：完整画布与共同裁切矩形 `126,19,381,268`。
- `pose`：bit 0 为原 left-down，bit 1 为原 right-down；0 表示双爪抬起，3 表示双爪落下。

`CatArtwork.cs` 只读取上述像素资源。玻璃材质、桌面采样和 Windows 呈现是本项目实现；未引入 Bongo Cat 的 SFML/Live2D 运行时，也未使用 Steam 版的程序或素材。

0.1.1 起，实际显示的键盘改为 `CatKeyboard.cs` 绘制的完整紧凑矢量键盘，带浅色键帽、深色字和按键反馈。原 `desk-ink.png` 保留为来源素材，但不再作为运行时的键盘显示层。

显示时会由 `CatInkTreatment.SoftenOuterInk` 对原线条做一次可复现的处理：查找距离 alpha 遮罩外部不超过 6 个原图像素的中性黑灰线，将这些像素的 alpha 乘以 0.22，使玻璃边缘更易辨认。眼睛、嘴位于内部，不会被减弱；彩色粉爪垫明确排除在此处理之外。该处理仅改变缓存中的派生显示位图，以上原 PNG、原始 ink 和 `GetInk()` 返回内容均保留。`assets/GlassBongoCat.ico` 由 [MakeIcon.cs](MakeIcon.cs) 从原 mask/ink 生成，包含透明的 16、32、48、256 像素图层。

如需重建派生 PNG，可用 .NET Framework 的 C# 编译器编译 `ExtractArtwork.cs`，引用 `System.Drawing.dll`，然后传入 `source` 目录与输出目录两个参数。输出目录会同时生成 `original-poses.png` 供逐姿势核对；此联系表不需要嵌入程序。
