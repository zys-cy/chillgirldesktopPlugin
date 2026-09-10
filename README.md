# ChillDesktop

> 只需拖入插件即可实现游戏壁纸化。

把 Steam 游戏 **《Chill with You Lo-Fi Story》** 变成**可交互的动态桌面壁纸**。

- 游戏铺在桌面最底层，女孩持续动画、lo-fi BGM 持续播放；
- IDE / 浏览器 / 资源管理器等办公窗口正常浮在她上面，缝隙处能看到她；
- 游戏内原生 UI（笔记 / 待办 / 习惯 / 日历 / 隐藏 UI）**在壁纸状态下可以正常点击**；
- 游戏右侧「笔记」按钮旁边多了一个同款灰色圆圈按钮 **「工具箱」**，点一下打开你自己桌面上的 `chillgirl` 文件夹（往里放快捷方式就行）；
- 点击壁纸层**不抢办公窗口焦点**、**不把游戏抬到办公窗口之上**、**按 Win+D 游戏不会消失**。

> 这是一个 **BepInEx 5 插件**，运行在游戏进程内。安装方式就是「把文件拖进游戏目录」，
> 不需要常驻后台程序、不需要装别的启动器。

---

## 安装

1. **关掉游戏**（如果正在运行）。
2. 在 Steam 里右键游戏 → **管理 → 浏览本地文件**，打开游戏根目录。
3. 把发布压缩包**里面的所有内容**拖进去（即 `winhttp.dll`、`doorstop_config.ini`、
   `.doorstop_version`、`BepInEx\`、`changelog.txt`），提示合并/覆盖一律选「是」。
4. 从 **Steam 正常启动游戏**。启动后她会直接铺在桌面上，不再弹出一个普通窗口。

> 第一次启动 BepInEx 会多花几秒钟生成配置和日志，属正常现象。
> 日志在 `BepInEx\LogOutput.log` 和 `BepInEx\chilldesktop.log`。

## 卸载

删掉下面这几项，游戏立刻恢复纯净 vanilla（插件从不修改任何游戏原始文件）：

```
winhttp.dll
doorstop_config.ini
.doorstop_version
changelog.txt
BepInEx\
```

也可以直接运行仓库里的 `tools\Uninstall-BepInEx.ps1`。

## 配置

安装后配置文件在 **`BepInEx\config\com.chilldesktop.plugin.cfg`**，改完重启游戏生效。

| 配置项 | 默认 | 说明 |
|---|---|---|
| `[General] Enabled` | true | 总开关 |
| `[General] HideDesktopIcons` | true | 壁纸模式下隐藏桌面图标层（**必须开**，否则 Win+D 时桌面层会盖到游戏上面） |
| `[General] SettleSeconds` | 8 | 等游戏窗口出现后再等几秒才动手改造窗口 |
| `[General] FastPollSeconds` | 0.25 | 快速自愈轮询间隔；设 0 关闭 |
| `[WinD] BlockMinimize` | true | 在游戏窗口过程里拦掉最小化，保证 Win+D 不动壁纸 |
| `[WinD] UseOwnerWindow` | false | 备用手段：给窗口挂隐藏属主。`BlockMinimize` 不够用时再打开 |
| `[Toolbox] Enabled` | true | 显示「工具箱」圆圈按钮 |
| `[Toolbox] ChillGirlPath` | 空 | 工具箱打开的文件夹；留空 = `桌面\chillgirl` |
| `[Toolbox] ToggleHotkey` | true | `Ctrl+Alt+D` 临时开关壁纸模式 |
| `[Layout] *` | 2560×1600 标定 | 按钮位置常量，换分辨率/改游戏 UI 时可微调 |

## 快捷键

- **Ctrl+Alt+D**：临时关闭 / 恢复壁纸模式（关闭时把游戏窗口还原成普通窗口，方便你正常玩）。

---

## 它是怎么做到的（技术要点）

游戏窗口保持**独立顶级窗口**，改造为：

```
样式   : WS_POPUP + WS_VISIBLE + WS_CLIPSIBLINGS（去边框、去标题栏）
扩展样 : WS_EX_LAYERED(alpha=255) + WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE
位置   : 铺满主屏 (0,0,ScreenW,ScreenH)，SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE)
```

三件事同时成立：真实鼠标点击照常投递给游戏、点击不抢焦点也不抬升、窗口沉在所有办公窗口之下。
桌面图标用单独隐藏 `SHELLDLL_DefView` 处理；「工具箱」是自有窗口类 + `UpdateLayeredWindow`
逐像素透明的顶级窗口，用「两次沉底」保持 `办公窗口 … 工具箱 → 游戏 → Progman` 的顺序。

**没有**走 Wallpaper Engine 那套 `SetParent(Progman/WorkerW)`：在 Win11 24H2 上，挂进
Progman 的子窗口虽然能当壁纸，但**收不到真实鼠标点击**，游戏原生 UI 会全部失效（已实测）。

### 两个值得记下来的坑

1. **不能把主循环挂在 BepInEx 给插件挂的那个 GameObject 上。**
   本作会在那个对象创建后约 0.3 秒把它销毁（日志里 `OnEnable → OnDisable → OnDestroy` 连着出现），
   于是插件的 `Update()` 和 `StartCoroutine()` 全部静默失效。
   所以插件自己 `new GameObject()` + `DontDestroyOnLoad()` 来跑主循环。
2. **等待必须用 `WaitForSecondsRealtime`。**
   游戏加载阶段 `Time.timeScale` 可能是 0，用缩放时间的 `WaitForSeconds` 协程会永远醒不过来。

---

## 从源码构建

```
dotnet build ChillDesktop.Plugin\ChillDesktop.Plugin.csproj
```

- 目标框架 `net46`，需要本机装有游戏（编译期引用游戏自带的 `UnityEngine*.dll`，**不会**打进产物）。
  游戏不在默认 Steam 路径时用 `-p:GameDir="你的游戏根目录"` 或环境变量 `CHILL_GAME_DIR` 指定。
- 构建后 dll 会自动拷到 `<游戏目录>\BepInEx\plugins\`，`-p:DeployToGame=false` 可关掉。
- 打发布包：`tools\Pack-Release.ps1`（产物在 `dist\`）。

仓库结构：

| 目录 | 说明 |
|---|---|
| `ChillDesktop.Plugin/` | **正式产品**：BepInEx 5 插件（net46） |
| `ChillDesktop/` | 早期外部启动器原型（.NET 8 WinForms），已验证 Win32 配方的来源，保留作参考 |
| `poc/` | 验证/取证脚本（Win+D 现场取证、壁纸可见性客观比对等） |
| `tools/` | 安装 / 卸载 / 打包 / 应急恢复脚本 |

---

## 免责声明

- 这是**非官方 mod**，与游戏开发者无关；使用风险自负。
- 本仓库**不包含任何游戏原始文件或资源**。
- 随包分发的 BepInEx 5 采用 **LGPL-2.1**，许可证见 `BepInEx/` 目录内说明与本仓库
  `THIRD-PARTY-NOTICES.md`。
- 本插件不联网、不收集任何数据。
