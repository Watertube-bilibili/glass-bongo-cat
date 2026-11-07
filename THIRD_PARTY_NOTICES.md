# 第三方素材与许可证

## Bongo Cat 原画与动作素材

本项目的猫身体轮廓、眼睛、嘴、粉色爪垫、左右爪姿势、斜桌沿及键盘来自 [MMmmmoko/Bongo-Cat-Mver](https://github.com/MMmmmoko/Bongo-Cat-Mver)，固定提交 [`4da0b9468ad3b6ffaa096eba3f080501d6ab0b5c`](https://github.com/MMmmmoko/Bongo-Cat-Mver/tree/4da0b9468ad3b6ffaa096eba3f080501d6ab0b5c)。具体使用 `BongoCatMver/img/keyboard/bg.png`、`lefthand/leftup.png`、`lefthand/0.png`、`righthand/rightup.png`、`righthand/0.png`。

从 0.1.1 开始，运行时的完整紧凑键盘由本项目 `CatKeyboard.cs` 重新绘制，替换了只有透明底线稿的桌沿/按键显示层；原桌沿和简化按键素材仍保留在源码中用于来源记录。猫身体和四种爪部姿态继续使用以上原版美术。

0.1.2 的 `CatKeyboard.cs`、`CatMouse.cs` 与 `KeyboardGlass.cs` 为本项目编写的矢量配件与实时材质。键盘朝猫、鼠标位于观众左侧的布局参考上游 keyboard/standard 模式；新增鼠标并未复制 standard 模式的鼠标 PNG 或程序代码。

原 PNG、逐文件校验记录与可复现的派生过程保留在 [assets/cat](assets/cat/SOURCE.md)。本项目从原像素提取 alpha 和线条，替换白色填充为实时玻璃；程序图标也是这些原像素的缩放和着色派生物。来源仓库在上述提交的 [LICENSE](https://github.com/MMmmmoko/Bongo-Cat-Mver/blob/4da0b9468ad3b6ffaa096eba3f080501d6ab0b5c/LICENSE) 为以下 MIT 文本，原版权保留：

为显出玻璃厚度，运行时只将原遮罩外缘 6 像素内的中性黑灰描边 alpha 调低至 22%；原眼睛、嘴与彩色粉爪垫保持原样。处理方法保留在 `CatInkTreatment.cs`，原始素材及原始线条资源没有覆盖。

```text
MIT License

Copyright (c) 2020  Han Zhang

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Mver 是 [kuroni/bongocat-osu](https://github.com/kuroni/bongocat-osu) 的分支。为保留原项目沿革中的版权信息，同时列出其 [MIT 许可证](https://github.com/kuroni/bongocat-osu/blob/master/LICENSE)：

```text
MIT License

Copyright (c) 2018 Đặng Đoàn Đức Trung

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 原角色来源致谢

原猫绘画由 [StrayRogue](https://twitter.com/StrayRogue) 创作，Bongo Cat 原迷因由 [DitzyFlama](https://twitter.com/DitzyFlama) 基于该绘画制作。这一来源由原互动网站 [Externalizable/bongo.cat 的作者说明](https://github.com/Externalizable/bongo.cat#authors) 记录。

本项目实际使用的是上述 Mver 提交中的 PNG，并保留其 MIT 版权通知。对原角色作者的致谢不表示本项目拥有原角色，也不表示 StrayRogue、DitzyFlama、Mver、kuroni 或其他 Bongo Cat 项目为本项目背书。bongo.cat 仓库记录的、专门给予其网站的原作者许可没有被当成本项目的一份新增授权；本项目未复制该网站的图片、音频或网页代码。

## 本项目实现范围

玻璃材质、桌面采样、Windows 原生透明窗口、输入映射、设置界面和资源转换工具由本项目实现，采用根目录 [LICENSE](LICENSE)。Bongo Cat 原图及派生图继续保留上列来源与版权。

本项目未分发 SFML、Live2D/Cubism、SDL、vladelaina/BongoCat 的 AGPL 程序、Steam Bongo Cat 程序或第三方音频。Windows 与 .NET Framework 4.8 为用户系统组件，不包含在便携包中。
