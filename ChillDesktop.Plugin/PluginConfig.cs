using BepInEx.Configuration;

namespace ChillDesktop
{
    /// <summary>
    /// 插件配置。由 <see cref="ChillDesktopPlugin"/> 在 Awake 里绑定后放进静态字段，
    /// 供跑在自建 GameObject 上的 <see cref="Runner"/> 读取。
    /// </summary>
    internal sealed class PluginConfig
    {
        public bool Enabled = true;
        public bool ForceEnable;
        public bool HideDesktopIcons = true;
        public bool Diagnostics = true;
        public float SettleSeconds = 8f;
        public float HeartbeatSeconds = 3f;
        public float FastPollSeconds = 0.25f;

        public bool BlockMinimize = true;
        public bool UseOwnerWindow;

        public bool ShowToolbox = true;
        public string ChillGirlPath = "";
        public bool ToggleHotkey = true;

        public bool FullscreenKey = true;
        public string FullscreenToggleKey = "F11";
        public bool HideTaskbarOnStart;

        public float RefWidth = 2560f;
        public float RefHeight = 1600f;
        public float NoteCx = 2454f;
        public float NoteCy = 667f;
        public float NoteR = 51f;
        public float ToolDx = -132f;

        public void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "是否启用 ChillDesktop 壁纸模式").Value;
            ForceEnable = cfg.Bind("General", "ForceEnable", false,
                "即使当前进程名不像游戏也强制启用（调试用）").Value;
            HideDesktopIcons = cfg.Bind("General", "HideDesktopIcons", true,
                "壁纸模式下隐藏桌面图标层（SHELLDLL_DefView）").Value;
            Diagnostics = cfg.Bind("General", "Diagnostics", false,
                "每 3 秒输出一次诊断信息（后台线程探测游戏窗口 + Update 调用次数），排障用").Value;
            SettleSeconds = cfg.Bind("General", "SettleSeconds", 8f,
                "等到游戏窗口出现后，再等多少秒才动手改造窗口（给 Unity 加载场景留时间）").Value;
            HeartbeatSeconds = cfg.Bind("General", "HeartbeatSeconds", 3f,
                "常规自愈心跳间隔（秒）").Value;
            FastPollSeconds = cfg.Bind("General", "FastPollSeconds", 0.25f,
                "Win+D 快速自愈轮询间隔（秒）；设 0 关闭快速轮询").Value;

            BlockMinimize = cfg.Bind("WinD", "BlockMinimize", true,
                "在游戏窗口过程里拦截并吃掉 SC_MINIMIZE，让 Win+D 无法最小化壁纸窗").Value;
            UseOwnerWindow = cfg.Bind("WinD", "UseOwnerWindow", false,
                "给游戏窗口挂一个隐藏属主窗（MinimizeAll 会跳过有属主的窗口）。BlockMinimize 不够用时再打开").Value;

            ShowToolbox = cfg.Bind("Toolbox", "Enabled", true,
                "是否显示「工具箱」圆圈按钮").Value;
            ChillGirlPath = cfg.Bind("Toolbox", "ChillGirlPath", "",
                "工具箱按钮打开的文件夹；留空 = 桌面\\chillgirl").Value;
            ToggleHotkey = cfg.Bind("Toolbox", "ToggleHotkey", true,
                "Ctrl+Alt+D 临时关闭/恢复壁纸模式（还原游戏窗口为普通窗口）").Value;

            FullscreenKey = cfg.Bind("Fullscreen", "Enabled", true,
                "是否启用「全屏模式」热键（隐藏/恢复 Windows 底部任务栏）").Value;
            FullscreenToggleKey = cfg.Bind("Fullscreen", "ToggleKey", "F11",
                "全屏模式热键。支持 F1~F24 / A~Z / 0~9；填 NONE 关闭").Value;
            HideTaskbarOnStart = cfg.Bind("Fullscreen", "HideTaskbarOnStart", false,
                "启动时就直接进入全屏模式（默认不隐藏任务栏，靠热键切换）").Value;

            RefWidth = cfg.Bind("Layout", "RefWidth", 2560f, "按钮坐标的参考分辨率宽").Value;
            RefHeight = cfg.Bind("Layout", "RefHeight", 1600f, "按钮坐标的参考分辨率高").Value;
            NoteCx = cfg.Bind("Layout", "NoteButtonCenterX", 2454f,
                "游戏内「笔记」按钮圆心 X（参考分辨率下）").Value;
            NoteCy = cfg.Bind("Layout", "NoteButtonCenterY", 667f,
                "游戏内「笔记」按钮圆心 Y（参考分辨率下）").Value;
            NoteR = cfg.Bind("Layout", "NoteButtonRadius", 51f,
                "游戏内按钮半径（参考分辨率下）").Value;
            ToolDx = cfg.Bind("Layout", "ToolboxOffsetX", -132f,
                "工具箱圆心相对笔记圆心的 X 偏移（负值=左侧）").Value;
        }
    }
}
