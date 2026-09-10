# 第三方组件说明

本仓库自身代码（`ChillDesktop.Plugin/`、`ChillDesktop/`、`tools/`、`poc/`）采用 **MIT**，
见根目录 `LICENSE`。以下第三方组件随发布包分发：

## BepInEx 5（LGPL-2.1）

- 项目：https://github.com/BepInEx/BepInEx
- 版本：`5.4.23.5`（`win_x64` 包）
- 许可证：**GNU Lesser General Public License v2.1**
- 分发方式：**未做任何修改**，以原始二进制形式随发布包提供
  （`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、`changelog.txt`、`BepInEx/`）。
- 源码获取：见上述仓库对应 tag；LGPL-2.1 全文见 https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html
- 依据 LGPL-2.1 第 6 条，随附完整许可证文本与源码出处即可再分发。

BepInEx 内含的其他组件（HarmonyX、Mono.Cecil、MonoMod 等）各自的许可证随
`BepInEx/core/` 一同分发，均与上述仓库发布包保持一致。

## 游戏本体

**本仓库与发布包不包含《Chill with You Lo-Fi Story》的任何原始文件、资源或代码。**
插件在运行时通过公开 Win32 API 操作游戏自己的窗口，不修改、不重打包任何游戏文件。

## 免责声明

本项目为非官方 mod，与游戏开发者 / 发行商无关。使用自负风险。
