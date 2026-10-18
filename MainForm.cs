using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using QRCoder;

namespace LocalCloud
{
    public partial class MainForm : Form
    {
        // --- 成员变量 ---
        private HttpListener listener;
        private bool isRunning = false;
        private System.Threading.Thread listenThread;
        private const int DefaultPort = 8080;

        // --- 服务运行状态 / 待机监听相关字段 ---
        private bool isServiceRunning = false;
        private DateTime lastActivityTime = DateTime.Now;
        private string _lastClientIP = "";
        private System.Net.Sockets.TcpListener tcpListener = null;
        private System.Threading.Thread waitThread = null;
        private bool listenForFirstConnection = false;
        private bool _stopWaiting = false;
        private System.Timers.Timer inactivityTimer = null;

        // 共享文件夹路径：程序运行目录下的 "Shared" 文件夹
        private readonly string SharedFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Shared");

        //Private SharedFolderPath As String = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)

        // 动态生成的控件引用
        private Label lblTitle;
        private TextBox txtIp;
        private TextBox txtPort;
        private Button btnStart;
        private Button btnStop;
        private Button btnOpenFolder;
        private Label lblStatus;
        private PictureBox pbQrCode;
        private PictureBox pbQrCodeLan;   // 局域网二维码框
        private PictureBox pbQrCodeWan;   // 外网二维码框
        private Label lblLanHint;         // 局域网二维码提示
        private Label lblWanHint;         // 外网二维码提示
        private Label lblHint;
        private RichTextBox txtLog;
        private ComboBox cmbLogKeepDays;                     // 日志保留时长下拉框
        private int _logKeepDays = DEFAULT_LOG_KEEP_DAYS;    // 当前保留天数（0 = 长期）
        private bool _logHasUnsavedRequests = false;         // 当前日志是否出现过请求（决定是否落盘保留）

        // --- 底部四标签页 ---
        private TabControl tabMain;
        private TabPage tabHome;      // 首页
        private TabPage tabFiles;     // 文件
        private TabPage tabShare;     // 共享
        private TabPage tabMine;      // 我的

        // --- 文件页控件 ---
        private ListView lstFiles;          // 文件列表
        private TextBox txtFilePath;        // 当前路径
        private Button btnFileUp;           // 返回上级
        private Button btnFileRefresh;      // 刷新
        private ComboBox cmbFileGroup;      // 分组筛选
        private ImageList imgListFiles;     // 文件图标列表
        private ContextMenuStrip cmFileMenu; // 文件右键菜单
        private Label lblFileInfo;          // 文件统计信息

        // --- 共享页控件 ---
        private ListView lstTransfer;        // 传输记录列表
        private Button btnTransferDelete;    // 删除选中传输记录
        private Button btnTransferRefresh;   // 刷新传输记录
        private ComboBox cmbTransferPageSize;    // 每页条数
        private Button btnTransferPrev;          // 上一页
        private Button btnTransferNext;          // 下一页
        private Label lblTransferPageInfo;       // 分页信息
        private int _transferPageIndex = 0;  // 当前页码（0 起）
        private int _transferPageSize = 10;  // 每页条数

        // --- 我的页控件 ---
        private PictureBox pbAvatar;         // 头像
        private Label lblNickname;           // 昵称显示
        private Button btnChangeAvatar;      // 修改头像
        private TextBox txtNickname;         // 昵称输入
        private Button btnSaveNickname;      // 保存昵称
        private TextBox txtUsername;         // 账号输入
        private TextBox txtPassword;         // 密码输入
        private Button btnSaveAccount;       // 保存账号密码
        private Button btnResetSettings;     // 重置密码
        private CheckBox chkShowPassword;    // 显示密码
        private const string AVATAR_FILE = "avatar.png";   // 头像文件名

        // --- ngrok 相关成员变量 ---
        private Process ngrokProcess;
        private string ngrokUrl = "";
        private bool isNgrokRunning = false;
        private const string NGROK_EXE_NAME = "ngrok.exe";
        private Button btnNgrok;  //开启按钮

        // --- 外网自动监听时间段相关 ---
        private DateTimePicker txtNgrokStart;   // 外网自动监听开始时间
        private DateTimePicker txtNgrokEnd;     // 外网自动监听结束时间
        private CheckBox chkAutoNgrok;          // 是否启用外网自动监听
        private Label lblNgrokCopy;             // 复制外网地址按钮
        private Label lblNgrokStatus;           // 外网自动监听状态提示
        private ToolTip tooltipNgrok;           // 外网地址悬浮提示
        private bool _loadingNgrokConfig = false;  // 正在加载配置标志（防止加载时触发保存）
        private bool _isStoppingNgrok = false;      // 主动停止 ngrok 标志（区分主动停止与意外退出）
        private System.Timers.Timer ngrokAutoTimer;   // 定期检查时间段的定时器
        private bool autoNgrokActive = false;     // 自动监听是否已启动 ngrok
        private const int MAX_DAILY_NGROK_MINUTES = 480;  // 每日免费额度上限（分钟，默认 8 小时）
        private const string NGROK_CONFIG_FILE = "ngrok_auto_config.txt";  // 配置文件名

        // --- 运行时配置文件统一存放目录 ---
        private const string CONFIG_FOLDER = "config";

        // --- 日志文件保留策略 ---
        private const string LOG_KEEP_CONFIG_FILE = "log_keep_days.txt";
        private const int DEFAULT_LOG_KEEP_DAYS = 30;   // 0 表示长期保留（不清理）

        // 判定“日志中是否出现过请求”的关键词（不区分大小写）
        // 注意：不要使用过于宽泛的词，否则会被非请求的日志误命中：
        //   · “访问”会出现在待机日志「等待外部设备访问以自动启动服务」中
        //   · “http”会被 ngrok 公网地址（https://...）误命中
        private static readonly string[] RequestLogKeywords =
        {
            "request", "post", "get",
            "请求", "访问首页", "下载", "上传", "登录"
        };

        // --- 最近浏览记录（首页显示） ---
        private readonly object _recentLock = new object();
        private readonly List<RecentFileItem> _recentFiles = new List<RecentFileItem>();
        private const int MAX_RECENT_COUNT = 30;
        private const string RECENT_CONFIG_FILE = "recent_files.txt";

        private class RecentFileItem
        {
            public string Path;
            public string Name;
            public bool IsFolder;
            public long Size;
            public string Category;
            public DateTime LastViewed;
        }

        // --- 文件剪贴板（复制/剪切/粘贴） ---
        private string _clipboardPath = "";
        private bool _clipboardIsCut = false;
        private readonly object _clipboardLock = new object();

        // --- 用户设置 ---
        private const string SETTINGS_FILE = "user_settings.txt";
        private const string DEFAULT_USERNAME = "admin";
        private const string DEFAULT_PASSWORD = "admin";
        private const string DEFAULT_NICKNAME = "管理员";
        private const string RESET_PIN = "2580";

        // --- 传输历史记录（共享页显示） ---
        private readonly object _transferLock = new object();
        private readonly List<TransferRecord> _transferHistory = new List<TransferRecord>();
        private long _transferVersion = 0;   // 传输记录版本号（每次增删 +1，用于两端同步）
        private const int MAX_TRANSFER_COUNT = 200;
        private const string TRANSFER_CONFIG_FILE = "transfer_history.txt";

        private class TransferRecord
        {
            public string Id;          // 唯一标识（用于删除）
            public string FileName;
            public string Direction;   // "upload" / "download"
            public long Size;
            public DateTime Time;
            public string ClientIP;
            public string FilePath;    // 文件完整路径（用于判断是否仍存在）
        }

        /// <summary>
        /// 构造函数：必须调用 InitializeComponent()，否则不会订阅 Load 事件，
        /// MainForm_Load 永远不触发，界面控件也都不会被创建（表现为空白窗体）。
        /// （由 VB 的 Handles MyBase.Load 移植到 C# 时容易漏掉此调用）
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
        }

        // 1. P/Invoke 声明：导入 user32.dll 中的 MessageBoxTimeoutW (Unicode版本)
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern int MessageBoxTimeout(IntPtr hWnd, string lpText, string lpCaption,
                                          uint uType, ushort wLanguageId, int dwMilliseconds);

        // Shell 文件信息结构（用于获取系统关联图标）
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        // 从 Shell32.dll 获取文件图标
        [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath,
                                          uint dwFileAttributes,
                                          ref SHFILEINFO psfi,
                                          uint cbFileInfo,
                                          uint uFlags);

        // 释放由 Shell 返回的图标句柄
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // 弹出 Windows「打开方式」对话框
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENASINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcszFile;       // 文件完整路径
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pcszClass;      // 可选，默认程序 ProgID（Nothing 表示系统自动选择）
            public uint oaifInFlags;  // 标志（OAIF_ALLOW_REGISTRATION 等）
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHOpenWithDialog(IntPtr hwnd, ref OPENASINFO poainfo);

        // 2. 自动弹窗方法
        /// <summary>
        /// 显示一个会在指定时间后自动关闭的消息框
        /// </summary>
        /// <param name="text">消息内容</param>
        /// <param name="timeoutSeconds">自动关闭的秒数，默认1秒</param>
        /// <param name="title">标题，默认"提示"</param>
        public static void AutoWindow(string text, decimal timeoutSeconds = 1, string title = "提示")
        {
            // 构建显示文本，包含倒计时提示
            string displayText = $"{text}{Environment.NewLine}{Environment.NewLine}将会在 {timeoutSeconds} 秒后自动关闭";

            // 调用 API
            // hWnd: IntPtr.Zero 表示没有拥有者窗口（或可以使用 Me.Handle）
            // uType: 0 表示只有确定按钮 (MB_OK)，无图标。如需图标可组合值，如 64 (信息图标)
            // wLanguageId: 0 表示使用系统默认语言
            // dwMilliseconds: 超时时间（毫秒）
            int result = MessageBoxTimeout(IntPtr.Zero, displayText, title, 64, 0, (int)(timeoutSeconds * 1000));
        }

        // --- 初始化与界面构建 ---
        /// <summary>获取配置文件完整路径（统一存放在 config 目录下，自动创建目录）</summary>
        private string GetConfigPath(string fileName)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FOLDER);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, fileName);
        }

        /// <summary>
        /// 获取 ngrok.exe 的可用路径：优先 config 目录，
        /// 兼容旧版本把 ngrok.exe 直接放在程序根目录的情况。
        /// 都不存在时返回 config 下的预期路径，便于提示用户放置位置。
        /// </summary>
        private string GetNgrokExePath()
        {
            string inConfig = GetConfigPath(NGROK_EXE_NAME);
            if (File.Exists(inConfig)) return inConfig;

            string inRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NGROK_EXE_NAME);
            if (File.Exists(inRoot)) return inRoot;

            return inConfig;
        }

        /// <summary>迁移旧版根目录下的配置文件到 config 目录（兼容旧版本升级）</summary>
        private void MigrateConfigFiles()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string configDir = Path.Combine(baseDir, CONFIG_FOLDER);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
                string[] oldFiles = {
                    "user_settings.txt", "recent_files.txt", "transfer_history.txt",
                    "ngrok_auto_config.txt", "ngrok_token.txt", "crash_log.txt",
                    "avatar.png", "ngrok.exe"
                };
                foreach (string f in oldFiles)
                {
                    string oldPath = Path.Combine(baseDir, f);
                    string newPath = Path.Combine(configDir, f);
                    if (File.Exists(oldPath) && !File.Exists(newPath))
                    {
                        File.Move(oldPath, newPath);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"迁移配置文件失败: {ex.Message}");
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 迁移旧版根目录下的配置文件到 config 目录
            MigrateConfigFiles();

            this.Text = "私有云盘";
            this.Size = new Size(620, 760);
            this.Location = new Point(500, 0);

            // 【修改点2】确保共享文件夹存在
            if (!Directory.Exists(SharedFolderPath))
            {
                Directory.CreateDirectory(SharedFolderPath);
            }

            // 动态创建所有控件（保证窗体有内容，避免空白窗体）
            CreateControls();

            // 配置防火墙规则（进程已确保以管理员权限运行）
            if (!ConfigureFirewallRule(DefaultPort))
            {
                MessageBox.Show("防火墙规则配置失败，服务可能无法被外部访问。" + Environment.NewLine +
                              "请手动在防火墙中允许端口 " + DefaultPort.ToString() + " 的入站连接。",
                              "警告",
                              MessageBoxButtons.OK,
                              MessageBoxIcon.Warning);
            }

            // 初始化外网自动监听时间段设置
            LoadNgrokAutoConfig();

            // 加载最近浏览记录与用户设置
            LoadRecentConfig();
            LoadTransferConfig();
            LoadLogKeepConfig();
            CleanupOldLogs();
            string autoIp = GetLocalIpAddress();
            txtIp.Text = !string.IsNullOrEmpty(autoIp) ? autoIp : "127.0.0.1";
            txtPort.Text = DefaultPort.ToString();

            // 传输记录已加载，刷新共享页列表
            RefreshTransferList();

            // 加载「我的」页数据（头像、昵称、账号）
            LoadMinePageData();

            UpdateUIState(false);

            // 初始化托盘
            InitializeTrayIcon();

            // 启动自动监听（等待外部连接）
            StartListeningForFirstConnection();

            // 启动外网自动监听调度定时器
            StartNgrokAutoScheduler();
        }
        /// <summary>
        /// 动态创建界面控件（底部四标签结构：首页 / 文件 / 共享 / 我的）
        /// </summary>
        private void CreateControls()
        {
            // ---- 通用局部工厂函数（把控件添加到指定父容器）----
            Func<Control, string, string, int, int, Label> NewLabel = (parent, name, text, x, y) =>
            {
                Label l = new Label() { Text = text, Location = new Point(x, y), AutoSize = true };
                if (!string.IsNullOrEmpty(name)) l.Name = name;
                parent.Controls.Add(l);
                return l;
            };

            Func<Control, string, int, int, int, TextBox> NewTextBox = (parent, name, x, y, width) =>
            {
                TextBox t = new TextBox() { Location = new Point(x, y), Width = width };
                if (!string.IsNullOrEmpty(name)) t.Name = name;
                parent.Controls.Add(t);
                return t;
            };

            Func<Control, string, string, int, int, int, Button> NewButton = (parent, name, text, x, y, width) =>
            {
                Button b = new Button() { Text = text, Location = new Point(x, y), Width = width };
                if (!string.IsNullOrEmpty(name)) b.Name = name;
                parent.Controls.Add(b);
                return b;
            };

            Func<Control, string, int, int, int, int, PictureBox> NewPictureBox = (parent, name, x, y, w, h) =>
            {
                PictureBox p = new PictureBox() { Location = new Point(x, y), Size = new Size(w, h), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
                if (!string.IsNullOrEmpty(name)) p.Name = name;
                parent.Controls.Add(p);
                return p;
            };

            Func<Control, string, int, int, int, int, RichTextBox> NewRichTextBox = (parent, name, x, y, w, h) =>
            {
                RichTextBox r = new RichTextBox() { Location = new Point(x, y), Size = new Size(w, h), ReadOnly = true, BackColor = Color.WhiteSmoke, Font = new Font("Consolas", 8), ScrollBars = RichTextBoxScrollBars.Vertical };
                if (!string.IsNullOrEmpty(name)) r.Name = name;
                parent.Controls.Add(r);
                return r;
            };

            Func<Control, string, string, int, int, CheckBox> NewCheckBox = (parent, name, text, x, y) =>
            {
                CheckBox c = new CheckBox() { Text = text, Location = new Point(x, y), AutoSize = true };
                if (!string.IsNullOrEmpty(name)) c.Name = name;
                parent.Controls.Add(c);
                return c;
            };

            Func<Control, string, int, int, int, string, DateTimePicker> NewDateTimePicker = (parent, name, x, y, width, format) =>
            {
                DateTimePicker d = new DateTimePicker() { Location = new Point(x, y), Width = width, Format = DateTimePickerFormat.Custom, CustomFormat = format, ShowUpDown = true };
                if (!string.IsNullOrEmpty(name)) d.Name = name;
                parent.Controls.Add(d);
                return d;
            };

            // ---- 底部标签控件 ----
            tabMain = new TabControl() { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei", 9) };
            this.Controls.Add(tabMain);

            tabHome = new TabPage("首页");
            tabFiles = new TabPage("文件");
            tabShare = new TabPage("共享");
            tabMine = new TabPage("我的");
            tabMain.TabPages.Add(tabHome);
            tabMain.TabPages.Add(tabFiles);
            tabMain.TabPages.Add(tabShare);
            tabMain.TabPages.Add(tabMine);

            // ================== 首页：服务控制 + 日志 ==================
            BuildHomePage(tabHome, NewLabel, NewTextBox, NewButton, NewCheckBox, NewDateTimePicker, NewRichTextBox);

            // ================== 文件：共享文件夹浏览器 ==================
            BuildFilesPage(tabFiles, NewLabel, NewTextBox, NewButton);

            // ================== 共享：二维码等信息 ==================
            BuildSharePage(tabShare, NewLabel, NewButton, NewPictureBox);

            // ================== 我的：头像 / 昵称 / 设置 ==================
            BuildMinePage(tabMine, NewLabel, NewTextBox, NewButton, NewPictureBox, NewCheckBox);
        }

        /// <summary>构建「首页」：服务控制按钮 + 日志</summary>
        private void BuildHomePage(TabPage page,
                                  Func<Control, string, string, int, int, Label> NewLabel,
                                  Func<Control, string, int, int, int, TextBox> NewTextBox,
                                  Func<Control, string, string, int, int, int, Button> NewButton,
                                  Func<Control, string, string, int, int, CheckBox> NewCheckBox,
                                  Func<Control, string, int, int, int, string, DateTimePicker> NewDateTimePicker,
                                  Func<Control, string, int, int, int, int, RichTextBox> NewRichTextBox)
        {
            // 标题
            lblTitle = NewLabel(page, "", "☁️ 私有云盘", 20, 20);
            lblTitle.Font = new Font("Microsoft YaHei", 14, FontStyle.Bold);

            // IP / Port
            NewLabel(page, "", "本机 IP:", 20, 60);
            txtIp = NewTextBox(page, "", 90, 55, 150);
            NewLabel(page, "", "端口:", 250, 60);
            txtPort = NewTextBox(page, "", 290, 55, 60);

            // 按钮区域
            btnStart = NewButton(page, "", "启动服务", 20, 100, 100);
            btnStart.BackColor = Color.FromArgb(0, 123, 255);
            btnStart.ForeColor = Color.White;
            btnStart.Click += BtnStart_Click;

            btnStop = NewButton(page, "", "停止服务", 130, 100, 100);
            btnStop.Enabled = false;
            btnStop.Click += BtnStop_Click;

            btnOpenFolder = NewButton(page, "", "打开私有云盘", 240, 100, 140);
            btnOpenFolder.Click += BtnOpenFolder_Click;

            btnNgrok = NewButton(page, "", "🌐 开启外网", 390, 100, 100);
            btnNgrok.BackColor = Color.FromArgb(52, 199, 89);
            btnNgrok.ForeColor = Color.White;
            btnNgrok.Enabled = false;
            btnNgrok.Click += BtnNgrok_Click;

            // 状态
            lblStatus = NewLabel(page, "", "服务未运行", 20, 140);
            lblStatus.ForeColor = Color.Gray;

            // 提示文字（二维码在「共享」页展示）
            Label lblHomeHint = NewLabel(page, "", "提示:请在「共享」页查看登录二维码", 20, 165);
            lblHomeHint.ForeColor = Color.Gray;

            // 外网自动监听时间段设置
            NewLabel(page, "", "外网自动监听:", 20, 195).Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
            chkAutoNgrok = NewCheckBox(page, "chkAutoNgrok", "启用", 120, 193);
            chkAutoNgrok.CheckedChanged += ChkAutoNgrok_CheckedChanged;

            txtNgrokStart = NewDateTimePicker(page, "", 170, 193, 60, "HH:mm");
            txtNgrokStart.ValueChanged += NgrokTime_ValueChanged;
            NewLabel(page, "", "至", 235, 195);
            txtNgrokEnd = NewDateTimePicker(page, "", 255, 193, 60, "HH:mm");
            txtNgrokEnd.ValueChanged += NgrokTime_ValueChanged;
            lblNgrokStatus = NewLabel(page, "", "", 325, 195);
            lblNgrokStatus.ForeColor = Color.Gray;
            lblNgrokStatus.Font = new Font("Microsoft YaHei", 8);

            // 公网地址
            Label lblPublicUrl = NewLabel(page, "lblPublicUrl", "", 20, 225);
            lblPublicUrl.ForeColor = Color.Blue;
            lblPublicUrl.Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
            lblPublicUrl.AutoSize = false;
            lblPublicUrl.Size = new Size(430, 20);
            lblPublicUrl.TextAlign = ContentAlignment.MiddleLeft;

            // 复制外网地址按钮
            lblNgrokCopy = NewLabel(page, "lblNgrokCopy", "📋 复制", 470, 225);
            lblNgrokCopy.ForeColor = Color.FromArgb(0, 122, 255);
            lblNgrokCopy.BackColor = Color.FromArgb(230, 240, 255);
            lblNgrokCopy.Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
            lblNgrokCopy.AutoSize = false;
            lblNgrokCopy.Size = new Size(70, 22);
            lblNgrokCopy.TextAlign = ContentAlignment.MiddleCenter;
            lblNgrokCopy.BorderStyle = BorderStyle.FixedSingle;
            lblNgrokCopy.Cursor = Cursors.Hand;
            lblNgrokCopy.Visible = false;
            lblNgrokCopy.Click += LblNgrokCopy_Click;

            Label lblDownloadStatus = NewLabel(page, "lblDownloadStatus", "", 20, 255);
            lblDownloadStatus.ForeColor = Color.FromArgb(255, 149, 0);
            lblDownloadStatus.Font = new Font("Microsoft YaHei", 9);

            // 操作记录区
            NewLabel(page, "", "操作记录", 20, 285).Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
            Button btnClearLog = NewButton(page, "", "清除日志", 450, 280, 90);
            btnClearLog.Click += BtnClearLog_Click;
            txtLog = NewRichTextBox(page, "", 20, 305, 520, 320);

            // 日志保留时长（默认 30 天，可选 10-60 天，最后一项为长期）
            NewLabel(page, "", "日志保留:", 20, 635);
            cmbLogKeepDays = new ComboBox() { Location = new Point(90, 630), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbLogKeepDays.Items.AddRange(new object[] { "10天", "20天", "30天", "40天", "50天", "60天", "长期" });
            cmbLogKeepDays.SelectedIndex = LogKeepIndexFromDays(DEFAULT_LOG_KEEP_DAYS);
            page.Controls.Add(cmbLogKeepDays);
            cmbLogKeepDays.SelectedIndexChanged += CmbLogKeepDays_Changed;
        }

        /// <summary>构建「共享」：二维码 + 传输记录</summary>
        private void BuildSharePage(TabPage page,
                                   Func<Control, string, string, int, int, Label> NewLabel,
                                   Func<Control, string, string, int, int, int, Button> NewButton,
                                   Func<Control, string, int, int, int, int, PictureBox> NewPictureBox)
        {
            // 提示文字：放顶部，预留足够高度避免遮挡二维码
            lblHint = NewLabel(page, "", "提示:启动服务后将会显示登录二维码", 20, 10);
            lblHint.AutoSize = false;
            lblHint.Size = new Size(540, 42);
            lblHint.ForeColor = Color.Gray;

            // 局域网二维码框（左），下移避开提示文字
            pbQrCodeLan = NewPictureBox(page, "", 20, 58, 240, 210);
            lblLanHint = NewLabel(page, "", "局域网二维码", 20, 270);
            lblLanHint.ForeColor = Color.Gray;
            lblLanHint.AutoSize = false;
            lblLanHint.Size = new Size(240, 16);
            lblLanHint.TextAlign = ContentAlignment.MiddleCenter;

            // 外网二维码框（右），初始隐藏
            pbQrCodeWan = NewPictureBox(page, "", 280, 58, 240, 210);
            pbQrCodeWan.Visible = false;
            lblWanHint = NewLabel(page, "", "外网二维码（未开启）", 280, 270);
            lblWanHint.ForeColor = Color.Gray;
            lblWanHint.AutoSize = false;
            lblWanHint.Size = new Size(240, 16);
            lblWanHint.TextAlign = ContentAlignment.MiddleCenter;
            lblWanHint.Visible = false;

            // 兼容旧代码对 pbQrCode 的引用，保留为局域网二维码框的别名
            pbQrCode = pbQrCodeLan;

            // 传输记录标题与操作按钮
            NewLabel(page, "", "传输记录", 20, 300).Font = new Font("Microsoft YaHei", 9, FontStyle.Bold);
            btnTransferRefresh = NewButton(page, "", "刷新", 440, 295, 70);
            btnTransferRefresh.Click += BtnTransferRefresh_Click;
            btnTransferDelete = NewButton(page, "", "删除选中", 520, 295, 70);
            btnTransferDelete.ForeColor = Color.Red;
            btnTransferDelete.Enabled = false;    // 初始未勾选时禁用
            btnTransferDelete.Click += BtnTransferDelete_Click;

            // 传输记录列表（编号、文件名、类型、时间），带勾选框支持多选
            lstTransfer = new ListView() { Location = new Point(20, 325), Size = new Size(570, 270), View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = true, CheckBoxes = true };
            lstTransfer.Columns.Add("编号", 40);
            lstTransfer.Columns.Add("文件名", 240);
            lstTransfer.Columns.Add("类型", 60);
            lstTransfer.Columns.Add("时间", 140);
            page.Controls.Add(lstTransfer);
            lstTransfer.ItemChecked += LstTransfer_ItemChecked;

            // 分页控件
            NewLabel(page, "", "每页", 20, 602);
            cmbTransferPageSize = new ComboBox() { Location = new Point(55, 597), Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            for (int i = 10; i <= 100; i += 10)
            {
                cmbTransferPageSize.Items.Add(i.ToString());
            }
            cmbTransferPageSize.SelectedIndex = 0;
            page.Controls.Add(cmbTransferPageSize);
            cmbTransferPageSize.SelectedIndexChanged += CmbTransferPageSize_Changed;

            btnTransferPrev = NewButton(page, "", "上一页", 130, 595, 70);
            btnTransferPrev.Click += BtnTransferPrev_Click;
            btnTransferNext = NewButton(page, "", "下一页", 210, 595, 70);
            btnTransferNext.Click += BtnTransferNext_Click;
            lblTransferPageInfo = NewLabel(page, "", "", 290, 600);
            lblTransferPageInfo.ForeColor = Color.Gray;
            lblTransferPageInfo.Font = new Font("Microsoft YaHei", 8);

            // 初始加载传输记录
            RefreshTransferList();
        }

        /// <summary>构建「我的」：头像 / 昵称 / 设置</summary>
        private void BuildMinePage(TabPage page,
                                  Func<Control, string, string, int, int, Label> NewLabel,
                                  Func<Control, string, int, int, int, TextBox> NewTextBox,
                                  Func<Control, string, string, int, int, int, Button> NewButton,
                                  Func<Control, string, int, int, int, int, PictureBox> NewPictureBox,
                                  Func<Control, string, string, int, int, CheckBox> NewCheckBox)
        {
            // 头像
            pbAvatar = NewPictureBox(page, "", 20, 30, 100, 100);
            pbAvatar.BackColor = Color.WhiteSmoke;
            pbAvatar.SizeMode = PictureBoxSizeMode.Zoom;

            btnChangeAvatar = NewButton(page, "", "修改头像", 130, 30, 100);
            btnChangeAvatar.Click += BtnChangeAvatar_Click;

            lblNickname = NewLabel(page, "", "管理员", 130, 75);
            lblNickname.Font = new Font("Microsoft YaHei", 12, FontStyle.Bold);

            // 昵称设置
            NewLabel(page, "", "昵称:", 20, 155);
            txtNickname = NewTextBox(page, "", 80, 150, 160);
            btnSaveNickname = NewButton(page, "", "保存昵称", 250, 148, 90);
            btnSaveNickname.Click += BtnSaveNickname_Click;

            // 账号密码设置
            NewLabel(page, "", "账号:", 20, 200);
            txtUsername = NewTextBox(page, "", 80, 195, 160);
            txtUsername.ReadOnly = true;      // 账号不可修改
            NewLabel(page, "", "密码:", 20, 235);
            txtPassword = NewTextBox(page, "", 80, 230, 160);
            txtPassword.UseSystemPasswordChar = true;
            btnSaveAccount = NewButton(page, "", "保存账号密码", 250, 210, 100);
            btnSaveAccount.Click += BtnSaveAccount_Click;

            // 显示密码复选框（仅用于账号密码输入框）
            chkShowPassword = NewCheckBox(page, "", "显示密码", 250, 250);
            chkShowPassword.CheckedChanged += ChkShowPassword_Changed;

            // 重置密码（弹窗流程，含 PIN 校验、随机生成/手动输入、二次确认）
            btnResetSettings = NewButton(page, "", "重置密码", 20, 280, 120);
            btnResetSettings.BackColor = Color.FromArgb(255, 82, 82);
            btnResetSettings.ForeColor = Color.White;
            btnResetSettings.Click += BtnResetSettings_Click;
        }

        /// <summary>构建「文件」：共享文件夹浏览器（Win11 风格大图标视图）</summary>
        private void BuildFilesPage(TabPage page,
                                   Func<Control, string, string, int, int, Label> NewLabel,
                                   Func<Control, string, int, int, int, TextBox> NewTextBox,
                                   Func<Control, string, string, int, int, int, Button> NewButton)
        {
            // 工具栏：路径 + 返回上级 + 刷新 + 新建文件夹 + 打开 + 分组
            txtFilePath = NewTextBox(page, "", 20, 20, 270);
            txtFilePath.ReadOnly = true;

            btnFileUp = NewButton(page, "", "⬆ 上级", 300, 18, 70);
            btnFileUp.Click += BtnFileUp_Click;

            btnFileRefresh = NewButton(page, "", "🔄 刷新(F5)", 380, 18, 90);
            btnFileRefresh.Click += BtnFileRefresh_Click;

            NewLabel(page, "", "分组:", 480, 23);
            cmbFileGroup = new ComboBox() { Location = new Point(520, 18), Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFileGroup.Items.AddRange(new string[] { "全部", "文件夹", "图片", "视频", "音频", "文档", "压缩包", "其他" });
            cmbFileGroup.SelectedIndex = 0;
            page.Controls.Add(cmbFileGroup);
            cmbFileGroup.SelectedIndexChanged += CmbFileGroup_Changed;

            // 文件图标 ImageList（系统真实图标 / 图片缩略图）
            imgListFiles = new ImageList() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(48, 48) };

            // 文件列表（Tile 视图：大图标 + 子项）
            lstFiles = new ListView() { Location = new Point(20, 55), Size = new Size(560, 430), View = View.Tile, FullRowSelect = true, HideSelection = false, MultiSelect = true, LabelEdit = false, LargeImageList = imgListFiles, TileSize = new Size(200, 90) };
            page.Controls.Add(lstFiles);
            lstFiles.DoubleClick += LstFiles_DoubleClick;
            // 鼠标右键：显示菜单
            lstFiles.MouseClick += LstFiles_MouseClick;
            // 键盘快捷键
            lstFiles.KeyDown += LstFiles_KeyDown;
            // 允许 ListView 接收键盘焦点
            lstFiles.TabStop = true;
            lstFiles.HideSelection = false;

            // 右键菜单
            cmFileMenu = new ContextMenuStrip();
            AddFileMenuItem("打开", "Enter", BtnFileOpen_Click);
            // 打开方式子菜单
            ToolStripMenuItem openWithMenu = new ToolStripMenuItem("打开方式");
            AddOpenWithItem(openWithMenu, "记事本", "notepad.exe", "");
            AddOpenWithItem(openWithMenu, "默认浏览器", "", "browser");
            AddOpenWithItem(openWithMenu, "图片查看器", "", "image");
            openWithMenu.DropDownItems.Add(new ToolStripSeparator());
            AddOpenWithItem(openWithMenu, "选择其他程序...", "", "choose");
            cmFileMenu.Items.Add(openWithMenu);
            cmFileMenu.Items.Add(new ToolStripSeparator());
            AddFileMenuItem("复制", "Ctrl+C", BtnFileCopy_Click);
            AddFileMenuItem("剪切", "Ctrl+X", BtnFileCut_Click);
            AddFileMenuItem("粘贴", "Ctrl+V", BtnFilePaste_Click);
            cmFileMenu.Items.Add(new ToolStripSeparator());
            AddFileMenuItem("重命名", "F2", BtnFileRename_Click);
            AddFileMenuItem("删除", "Del", BtnFileDelete_Click);
            cmFileMenu.Items.Add(new ToolStripSeparator());
            AddFileMenuItem("新建文件夹", "", BtnFileNewFolder_Click);
            AddFileMenuItem("刷新", "F5", BtnFileRefresh_Click);
            AddFileMenuItem("属性", "", BtnFileProperties_Click);
            lstFiles.ContextMenuStrip = cmFileMenu;

            lblFileInfo = NewLabel(page, "", "", 20, 495);
            lblFileInfo.ForeColor = Color.Gray;
            lblFileInfo.Font = new Font("Microsoft YaHei", 8);

            // 初始加载
            LoadFileList(SharedFolderPath);
        }

        /// <summary>向右键菜单添加项（带快捷键显示）</summary>
        private void AddFileMenuItem(string text, string shortcut, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            if (!string.IsNullOrEmpty(shortcut)) item.Text += $"  ({shortcut})";
            item.Click += handler;
            cmFileMenu.Items.Add(item);
        }

        /// <summary>向"打开方式"子菜单添加项</summary>
        private void AddOpenWithItem(ToolStripMenuItem parent, string text, string program, string mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { Tag = mode };
            item.Click += (sender, e) =>
            {
                OpenWithSelected(sender, program, mode);
            };
            parent.DropDownItems.Add(item);
        }

        /// <summary>用指定方式打开选中文件</summary>
        private void OpenWithSelected(object sender, string program, string mode)
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("请先选择文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                switch (mode)
                {
                    case "browser":
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        break;
                    case "image":
                        // 用系统默认图片查看器（rundll32 或直接 shell 打开）
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        break;
                    case "choose":
                        // 调用 Windows 系统"打开方式"对话框（SHOpenWithDialog）
                        ShowOpenWithDialog(path);
                        break;
                    default:
                        // 指定程序打开
                        Process.Start(new ProcessStartInfo(program)
                        {
                            Arguments = $"\"{path}\"",
                            UseShellExecute = false
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>弹出 Windows「打开方式」对话框</summary>
        private void ShowOpenWithDialog(string filePath)
        {
            try
            {
                OPENASINFO info = new OPENASINFO()
                {
                    pcszFile = filePath,
                    pcszClass = null,
                    oaifInFlags = 0
                };
                int hr = SHOpenWithDialog(this.Handle, ref info);
                // hr = 0 表示成功；负数表示失败。若失败则回退到 rundll32
                if (hr < 0)
                {
                    throw new InvalidOperationException($"SHOpenWithDialog 返回 HRESULT: 0x{hr:X8}");
                }
            }
            catch (Exception)
            {
                // 兜底：打开 Windows 设置的「默认应用」页面，让用户手动设置
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
                }
                catch (Exception ex2)
                {
                    MessageBox.Show("打开方式对话框调用失败: " + ex2.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ================== 文件浏览器逻辑 ==================

        /// <summary>当前文件浏览器所在的目录</summary>
        private string _fileCurrentDir = "";

        /// <summary>加载指定目录到文件列表</summary>
        private void LoadFileList(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                dirPath = SharedFolderPath;
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }
            }
            _fileCurrentDir = dirPath;
            txtFilePath.Text = dirPath;
            lstFiles.Items.Clear();
            // 重建图标缓存（避免目录切换后图标陈旧）
            imgListFiles.Images.Clear();
            _iconCache.Clear();

            try
            {
                // 文件夹图标（使用系统"目录"图标）
                int folderIconIdx = AddSystemIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows), false);

                // 文件夹在前，文件在后
                foreach (string d in Directory.GetDirectories(dirPath).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                {
                    DirectoryInfo di = new DirectoryInfo(d);
                    ListViewItem item = new ListViewItem(Path.GetFileName(d))
                    {
                        Tag = d,
                        ImageIndex = folderIconIdx
                    };
                    // Tile 视图：SubItems(0)=名称已显示，再加 2 行子文本（类型 + 大小）
                    item.SubItems.Add("文件夹");
                    item.SubItems.Add("");
                    lstFiles.Items.Add(item);
                }

                foreach (string f in Directory.GetFiles(dirPath).OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                {
                    FileInfo fi = new FileInfo(f);
                    ListViewItem item = new ListViewItem(Path.GetFileName(f))
                    {
                        Tag = f,
                        ImageIndex = GetFileIconIndex(f)
                    };
                    item.SubItems.Add(GetFileCategory(f));
                    item.SubItems.Add(FormatFileSize(fi.Length));
                    lstFiles.Items.Add(item);
                }

                ApplyFileGroupFilter();
            }
            catch (Exception ex)
            {
                AddLog($"加载文件列表失败: {ex.Message}");
            }
        }

        /// <summary>图标缓存（扩展名 → ImageList 索引）</summary>
        private readonly Dictionary<string, int> _iconCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>获取/添加文件的系统图标，返回 ImageList 索引（图片也走系统图标，避免大量解码卡死）</summary>
        private int GetFileIconIndex(string filePath)
        {
            // 所有文件（含图片）均按扩展名缓存系统图标，避免逐个解码大图导致 UI 卡死
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            int cached;
            if (_iconCache.TryGetValue(ext, out cached)) return cached;
            int idx = AddSystemIcon(filePath, true);
            _iconCache[ext] = idx;
            return idx;
        }

        /// <summary>从系统（Shell32）提取指定路径的关联图标，加入 ImageList</summary>
        private int AddSystemIcon(string filePath, bool useShellApi)
        {
            try
            {
                Icon icon;
                if (useShellApi)
                {
                    // 用 Shell API 提取高质量图标（含各类文件关联图标）
                    icon = GetShellIcon(filePath);
                    if (icon == null)
                    {
                        icon = Icon.ExtractAssociatedIcon(filePath);
                    }
                }
                else
                {
                    // 文件夹：用系统目录图标
                    icon = GetDirectoryIcon(filePath);
                }
                if (icon == null) return -1;
                imgListFiles.Images.Add(icon);
                return imgListFiles.Images.Count - 1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>通过 Shell API 获取文件/文件夹真实关联图标（不使用文件属性推断）</summary>
        private Icon GetShellIcon(string filePath)
        {
            try
            {
                SHFILEINFO shInfo = new SHFILEINFO();
                // SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0（大图标）
                // 去掉 SHGFI_USEFILEATTRIBUTES，直接读取真实文件的关联图标
                IntPtr result = SHGetFileInfo(filePath, 0, ref shInfo, (uint)Marshal.SizeOf(shInfo),
                                           0x100 | 0x0);
                if (result != IntPtr.Zero && shInfo.hIcon != IntPtr.Zero)
                {
                    // 复制图标（因为 shInfo.hIcon 由系统管理，必须复制）
                    Icon iconCopy = (Icon)Icon.FromHandle(shInfo.hIcon).Clone();
                    // 释放系统句柄
                    DestroyIcon(shInfo.hIcon);
                    return iconCopy;
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>获取系统目录图标</summary>
        private Icon GetDirectoryIcon(string folderPath)
        {
            try
            {
                SHFILEINFO shInfo = new SHFILEINFO();
                // SHGFI_ICON = 0x100, SHGFI_OPENICON = 0x2
                int flags = 0x100 | 0x2;
                IntPtr result = SHGetFileInfo(folderPath, 0, ref shInfo, (uint)Marshal.SizeOf(shInfo), (uint)flags);
                if (result != IntPtr.Zero && shInfo.hIcon != IntPtr.Zero)
                {
                    Icon iconCopy = (Icon)Icon.FromHandle(shInfo.hIcon).Clone();
                    DestroyIcon(shInfo.hIcon);
                    return iconCopy;
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>根据分组筛选当前列表</summary>
        private void ApplyFileGroupFilter()
        {
            if (cmbFileGroup == null || lstFiles == null) return;
            string group = cmbFileGroup.SelectedItem?.ToString();
            int folderCount = 0;
            int fileCount = 0;
            long totalSize = 0;

            // 倒序遍历以安全移除不符合的项
            for (int i = lstFiles.Items.Count - 1; i >= 0; i--)
            {
                ListViewItem item = lstFiles.Items[i];
                string tag = item.Tag?.ToString();
                bool isFolder = item.SubItems[1].Text == "文件夹";
                bool show = true;

                if (group != null && group != "全部")
                {
                    show = isFolder ? group == "文件夹" : group == GetFileCategory(tag);
                }
                if (!show)
                {
                    lstFiles.Items.RemoveAt(i);
                    continue;
                }
                if (isFolder) folderCount++; else fileCount++;
                if (!isFolder && !string.IsNullOrEmpty(tag) && File.Exists(tag))
                {
                    totalSize += new FileInfo(tag).Length;
                }
            }

            lblFileInfo.Text = $"文件夹 {folderCount} 个 · 文件 {fileCount} 个 · 共 {FormatFileSize(totalSize)}";
        }

        /// <summary>获取当前选中的第一个路径</summary>
        private string GetSelectedPath()
        {
            if (lstFiles.SelectedItems.Count == 0) return null;
            return lstFiles.SelectedItems[0].Tag?.ToString();
        }

        /// <summary>获取所有选中路径</summary>
        private List<string> GetSelectedPaths()
        {
            List<string> result = new List<string>();
            foreach (ListViewItem item in lstFiles.SelectedItems)
            {
                if (item.Tag != null) result.Add(item.Tag.ToString());
            }
            return result;
        }

        private void BtnFileUp_Click(object sender, EventArgs e)
        {
            DirectoryInfo parent = Directory.GetParent(_fileCurrentDir);
            if (parent != null)
            {
                LoadFileList(parent.FullName);
            }
        }

        private void BtnFileRefresh_Click(object sender, EventArgs e)
        {
            LoadFileList(_fileCurrentDir);
        }

        private void CmbFileGroup_Changed(object sender, EventArgs e)
        {
            // 重新加载以应用筛选
            LoadFileList(_fileCurrentDir);
        }

        private void LstFiles_DoubleClick(object sender, EventArgs e)
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path)) return;
            if (Directory.Exists(path))
            {
                LoadFileList(path);
            }
            else
            {
                BtnFileOpen_Click(sender, e);
            }
        }

        /// <summary>ListView 右键点击：选中所指项并显示菜单</summary>
        private void LstFiles_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewHitTestInfo hit = lstFiles.HitTest(e.Location);
            if (hit.Item != null)
            {
                // 选中右键所指项（若未在已选中集合中）
                if (!hit.Item.Selected) hit.Item.Selected = true;
                hit.Item.Focused = true;
            }
        }

        /// <summary>ListView 键盘快捷键</summary>
        private void LstFiles_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                // Ctrl+C / Ctrl+X / Ctrl+V：复制/剪切/粘贴
                case Keys.C:
                    if (e.Control)
                    {
                        BtnFileCopy_Click(sender, e);
                        e.Handled = true;
                    }
                    break;
                case Keys.X:
                    if (e.Control)
                    {
                        BtnFileCut_Click(sender, e);
                        e.Handled = true;
                    }
                    break;
                case Keys.V:
                    if (e.Control)
                    {
                        BtnFilePaste_Click(sender, e);
                        e.Handled = true;
                    }
                    break;
                // Enter：打开文件/进入文件夹
                case Keys.Enter:
                    LstFiles_DoubleClick(sender, e);
                    e.Handled = true;
                    break;
                // F2：重命名
                case Keys.F2:
                    BtnFileRename_Click(sender, e);
                    e.Handled = true;
                    break;
                // Delete：删除
                case Keys.Delete:
                    BtnFileDelete_Click(sender, e);
                    e.Handled = true;
                    break;
                // F5：刷新
                case Keys.F5:
                    BtnFileRefresh_Click(sender, e);
                    e.Handled = true;
                    break;
                // Backspace：返回上级
                case Keys.Back:
                    BtnFileUp_Click(sender, e);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>查看属性</summary>
        private void BtnFileProperties_Click(object sender, EventArgs e)
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("请先选择文件或文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowPropertiesWindow(path);
        }

        /// <summary>弹出属性窗口，显示文件/文件夹的详细属性</summary>
        private void ShowPropertiesWindow(string path)
        {
            try
            {
                List<(string Label, string Value)> props = new List<(string Label, string Value)>();
                bool isDir = Directory.Exists(path);

                if (isDir)
                {
                    DirectoryInfo di = new DirectoryInfo(path);
                    props.Add(("名称", di.Name));
                    props.Add(("类型", "文件夹"));
                    props.Add(("位置", di.Parent?.FullName));
                    props.Add(("完整路径", di.FullName));
                    // 统计包含的文件与文件夹数量
                    try
                    {
                        int fileCount = Directory.GetFiles(path).Length;
                        int dirCount = Directory.GetDirectories(path).Length;
                        props.Add(("包含", $"{fileCount} 个文件，{dirCount} 个子文件夹"));
                    }
                    catch
                    {
                    }
                    props.Add(("创建时间", di.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));
                    props.Add(("修改时间", di.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
                }
                else if (File.Exists(path))
                {
                    FileInfo fi = new FileInfo(path);
                    props.Add(("名称", fi.Name));
                    props.Add(("类型", GetFileCategory(path)));
                    props.Add(("位置", fi.DirectoryName));
                    props.Add(("完整路径", fi.FullName));
                    props.Add(("大小", $"{FormatFileSize(fi.Length)}（{fi.Length:N0} 字节）"));
                    props.Add(("创建时间", fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")));
                    props.Add(("修改时间", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
                    props.Add(("访问时间", fi.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss")));
                    props.Add(("只读", fi.IsReadOnly ? "是" : "否"));
                }
                else
                {
                    MessageBox.Show("目标不存在", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 构建属性窗口
                Form frm = new Form()
                {
                    Text = "属性",
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ClientSize = new Size(520, 46 + (props.Count * 30) + 60)
                };

                int y = 16;
                foreach (var p in props)
                {
                    Label lblLabel = new Label()
                    {
                        Text = p.Label + ":",
                        Location = new Point(20, y),
                        AutoSize = true,
                        ForeColor = Color.Gray,
                        Font = new Font("Microsoft YaHei", 9)
                    };
                    frm.Controls.Add(lblLabel);

                    Label lblValue = new Label()
                    {
                        Text = p.Value,
                        Location = new Point(120, y),
                        AutoSize = false,
                        Size = new Size(380, 20),
                        ForeColor = Color.Black,
                        Font = new Font("Microsoft YaHei", 9)
                    };
                    frm.Controls.Add(lblValue);
                    y += 30;
                }

                Button btnClose = new Button() { Text = "关闭", Location = new Point(220, y + 10), Size = new Size(80, 30) };
                btnClose.Click += (s, ev) => frm.Close();
                frm.Controls.Add(btnClose);
                frm.AcceptButton = btnClose;
                frm.CancelButton = btnClose;

                frm.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show("获取属性失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnFileOpen_Click(object sender, EventArgs e)
        {
            string path = GetSelectedPath();
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("请先选择文件或文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo() { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnFileNewFolder_Click(object sender, EventArgs e)
        {
            string name = InputBox("请输入文件夹名称:", "新建文件夹", "新建文件夹");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                string newDir = Path.Combine(_fileCurrentDir, name);
                if (Directory.Exists(newDir) || File.Exists(newDir))
                {
                    MessageBox.Show("同名文件或文件夹已存在", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Directory.CreateDirectory(newDir);
                LoadFileList(_fileCurrentDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnFileRename_Click(object sender, EventArgs e)
        {
            string target = GetSelectedPath();
            if (string.IsNullOrEmpty(target))
            {
                MessageBox.Show("请先选择要重命名的文件或文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string oldName = Path.GetFileName(target);
            string newName = InputBox("请输入新名称:", "重命名", oldName);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
            try
            {
                string newPath = Path.Combine(Path.GetDirectoryName(target), newName);
                if (Directory.Exists(target))
                {
                    Directory.Move(target, newPath);
                }
                else
                {
                    File.Move(target, newPath);
                }
                LoadFileList(_fileCurrentDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("重命名失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnFileDelete_Click(object sender, EventArgs e)
        {
            List<string> paths = GetSelectedPaths();
            if (paths.Count == 0)
            {
                MessageBox.Show("请先选择要删除的文件或文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show($"确定删除选中的 {paths.Count} 个项目吗？此操作不可恢复。", "确认删除",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                foreach (string p in paths)
                {
                    if (Directory.Exists(p))
                    {
                        Directory.Delete(p, true);
                    }
                    else if (File.Exists(p))
                    {
                        File.Delete(p);
                    }
                }
                AddLog($"删除 {paths.Count} 个项目");
                LoadFileList(_fileCurrentDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnFileCopy_Click(object sender, EventArgs e)
        {
            List<string> paths = GetSelectedPaths();
            if (paths.Count == 0)
            {
                MessageBox.Show("请先选择要复制的文件或文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _fileClipboardSource = paths;
            _fileClipboardIsCut = false;
            AddLog($"已复制 {paths.Count} 个项目到剪贴板");
        }

        private void BtnFileCut_Click(object sender, EventArgs e)
        {
            List<string> paths = GetSelectedPaths();
            if (paths.Count == 0)
            {
                MessageBox.Show("请先选择要剪切的文件或文件夹", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _fileClipboardSource = paths;
            _fileClipboardIsCut = true;
            AddLog($"已剪切 {paths.Count} 个项目到剪贴板");
        }

        private void BtnFilePaste_Click(object sender, EventArgs e)
        {
            if (_fileClipboardSource == null || _fileClipboardSource.Count == 0)
            {
                MessageBox.Show("剪贴板为空，请先复制或剪切", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                foreach (string src in _fileClipboardSource)
                {
                    string name = Path.GetFileName(src);
                    string dest = Path.Combine(_fileCurrentDir, name);
                    if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    // 目标存在则自动重命名
                    dest = GetUniquePath(dest);
                    if (Directory.Exists(src))
                    {
                        CopyDirectory(src, dest);
                    }
                    else if (File.Exists(src))
                    {
                        File.Copy(src, dest);
                    }
                }
                // 剪切操作：粘贴完成后删除源文件
                if (_fileClipboardIsCut)
                {
                    foreach (string src in _fileClipboardSource)
                    {
                        try
                        {
                            if (Directory.Exists(src))
                            {
                                Directory.Delete(src, true);
                            }
                            else if (File.Exists(src))
                            {
                                File.Delete(src);
                            }
                        }
                        catch (Exception ex)
                        {
                            AddLog($"删除源文件失败: {ex.Message}");
                        }
                    }
                    _fileClipboardSource = null;
                    _fileClipboardIsCut = false;
                }
                AddLog("粘贴完成");
                LoadFileList(_fileCurrentDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("粘贴失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>文件浏览器剪贴板（本地文件操作）</summary>
        private List<string> _fileClipboardSource = null;
        private bool _fileClipboardIsCut = false;

        // ================== 我的页：头像 / 昵称 / 设置 ==================

        /// <summary>修改头像</summary>
        private void BtnChangeAvatar_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog()
            {
                Title = "选择头像图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    // 保存到 config 目录，统一命名为 avatar.png
                    string src = dlg.FileName;
                    string dest = GetConfigPath(AVATAR_FILE);
                    // 统一转成 PNG 保存
                    using (Image img = Image.FromFile(src))
                    {
                        using (Bitmap bmp = new Bitmap(img))
                        {
                            bmp.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    LoadAvatar();
                    AddLog("头像已更新");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("设置头像失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>加载头像（无头像时显示默认占位）</summary>
        private void LoadAvatar()
        {
            try
            {
                string avatarPath = GetConfigPath(AVATAR_FILE);
                if (File.Exists(avatarPath))
                {
                    using (Image img = Image.FromFile(avatarPath))
                    {
                        pbAvatar.Image = new Bitmap(img);
                    }
                }
                else
                {
                    // 生成默认占位头像（圆形背景 + 首字母）
                    pbAvatar.Image = CreateDefaultAvatar();
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载头像失败: {ex.Message}");
            }
        }

        /// <summary>生成默认占位头像</summary>
        private Image CreateDefaultAvatar()
        {
            int size = 100;
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.WhiteSmoke);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 123, 255)))
                {
                    g.FillEllipse(brush, 0, 0, size, size);
                }
                UserSettings s = ReadUserSettings();
                string firstChar = !string.IsNullOrEmpty(s.Nickname) ? s.Nickname.Substring(0, 1) : "管";
                using (Font f = new Font("Microsoft YaHei", 40, FontStyle.Bold))
                {
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    {
                        StringFormat sf = new StringFormat() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(firstChar, f, brush, new RectangleF(0, 0, size, size), sf);
                    }
                }
            }
            return bmp;
        }

        /// <summary>加载「我的」页数据（昵称、账号、头像）</summary>
        private void LoadMinePageData()
        {
            UserSettings s = ReadUserSettings();
            lblNickname.Text = s.Nickname;
            txtNickname.Text = s.Nickname;
            txtUsername.Text = s.Username;
            txtPassword.Text = "";   // 出于安全，不回显密码
            LoadAvatar();
        }

        /// <summary>保存昵称</summary>
        private void BtnSaveNickname_Click(object sender, EventArgs e)
        {
            string nickname = txtNickname.Text.Trim();
            if (string.IsNullOrEmpty(nickname))
            {
                MessageBox.Show("昵称不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            UserSettings s = ReadUserSettings();
            SaveUserSettings(nickname, s.Username, s.Password);
            lblNickname.Text = nickname;
            LoadAvatar();
            AddLog($"昵称已更新: {nickname}");
            MessageBox.Show("昵称已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>校验密码强度：数字、字母、符号至少包含两种</summary>
        private bool ValidatePassword(string password)
        {
            bool hasDigit = false;
            bool hasLetter = false;
            bool hasSymbol = false;
            foreach (char c in password)
            {
                if (char.IsDigit(c))
                {
                    hasDigit = true;
                }
                else if (char.IsLetter(c))
                {
                    hasLetter = true;
                }
                else
                {
                    hasSymbol = true;
                }
            }
            int kinds = (hasDigit ? 1 : 0) + (hasLetter ? 1 : 0) + (hasSymbol ? 1 : 0);
            return kinds >= 2;
        }

        /// <summary>保存账号密码（密码强度校验）</summary>
        private void BtnSaveAccount_Click(object sender, EventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Text;
            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("账号不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("密码不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (password.Length < 6)
            {
                MessageBox.Show("密码长度至少 6 位", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ValidatePassword(password))
            {
                MessageBox.Show("密码需包含数字、字母、符号中的至少两种", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            UserSettings s = ReadUserSettings();
            SaveUserSettings(s.Nickname, username, password);
            txtPassword.Text = "";
            AddLog($"账号密码已更新: 账号={username}");
            MessageBox.Show("账号密码已保存", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>显示/隐藏密码</summary>
        private void ChkShowPassword_Changed(object sender, EventArgs e)
        {
            txtPassword.UseSystemPasswordChar = !chkShowPassword.Checked;
        }

        /// <summary>生成随机密码（数字+字母+符号混合，满足至少两种类型）</summary>
        private string GenerateRandomPassword(int length)
        {
            const string digits = "0123456789";
            const string letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string symbols = "!@#$%^&*()-_=+[]{};:,.<>?";
            string all = digits + letters + symbols;
            Random rng = new Random();
            StringBuilder sb = new StringBuilder();
            // 先保证至少包含字母和数字各一个
            sb.Append(letters[rng.Next(letters.Length)]);
            sb.Append(digits[rng.Next(digits.Length)]);
            for (int i = 0; i <= length - 3; i++)
            {
                sb.Append(all[rng.Next(all.Length)]);
            }
            // 打乱顺序
            char[] arr = sb.ToString().ToCharArray();
            for (int i = arr.Length - 1; i >= 1; i--)
            {
                int j = rng.Next(i + 1);
                char tmp = arr[i];
                arr[i] = arr[j];
                arr[j] = tmp;
            }
            return new string(arr);
        }

        /// <summary>重置密码（需电脑 PIN 码，支持随机生成或人工设置）</summary>
        private void BtnResetSettings_Click(object sender, EventArgs e)
        {
            using (Form frm = new Form()
            {
                Text = "重置密码",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(380, 300)
            })
            {
                int y = 16;

                // PIN 码
                Label lblPin = new Label() { Text = "电脑登录 PIN 码:", Location = new Point(20, y), AutoSize = true };
                frm.Controls.Add(lblPin);
                TextBox txtPin = new TextBox() { Location = new Point(140, y - 3), Width = 160, UseSystemPasswordChar = true };
                frm.Controls.Add(txtPin);
                y += 40;

                // 新密码
                Label lblNew = new Label() { Text = "新密码:", Location = new Point(20, y), AutoSize = true };
                frm.Controls.Add(lblNew);
                TextBox txtNew = new TextBox() { Location = new Point(140, y - 3), Width = 160, UseSystemPasswordChar = true };
                frm.Controls.Add(txtNew);
                y += 40;

                // 确认密码
                Label lblConfirm = new Label() { Text = "确认新密码:", Location = new Point(20, y), AutoSize = true };
                frm.Controls.Add(lblConfirm);
                TextBox txtConfirm = new TextBox() { Location = new Point(140, y - 3), Width = 160, UseSystemPasswordChar = true };
                frm.Controls.Add(txtConfirm);
                y += 40;

                // 随机生成按钮（仅 PIN 正确时启用）
                Button btnRandom = new Button() { Text = "随机生成", Location = new Point(140, y - 3), Width = 90, Enabled = false };
                frm.Controls.Add(btnRandom);
                btnRandom.Click += (sender, e) =>
                {
                    string pwd = GenerateRandomPassword(10);
                    txtNew.Text = pwd;
                    txtConfirm.Text = pwd;
                };
                y += 44;

                // PIN 实时校验：正确则启用随机生成按钮
                txtPin.TextChanged += (sender, e) =>
                {
                    btnRandom.Enabled = txtPin.Text.Trim() == RESET_PIN;
                };

                // 显示密码
                CheckBox chkShow = new CheckBox() { Text = "显示密码", Location = new Point(20, y), AutoSize = true };
                frm.Controls.Add(chkShow);
                chkShow.CheckedChanged += (sender, e) =>
                {
                    txtNew.UseSystemPasswordChar = !chkShow.Checked;
                    txtConfirm.UseSystemPasswordChar = !chkShow.Checked;
                };
                y += 36;

                // 确定 / 取消
                Button btnOk = new Button() { Text = "确认重置", Location = new Point(120, y), Width = 100 };
                Button btnCancel = new Button() { Text = "取消", Location = new Point(230, y), Width = 80 };
                frm.Controls.Add(btnOk);
                frm.Controls.Add(btnCancel);
                btnCancel.Click += (sender, e) => frm.DialogResult = DialogResult.Cancel;

                // 确定按钮逻辑
                string newPassword = "";
                btnOk.Click += (sender, e) =>
                {
                    string pin = txtPin.Text.Trim();
                    if (string.IsNullOrEmpty(pin))
                    {
                        MessageBox.Show("请输入电脑登录 PIN 码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (pin != RESET_PIN)
                    {
                        MessageBox.Show("PIN 码错误", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    string pwd = txtNew.Text;
                    string confirm = txtConfirm.Text;
                    if (string.IsNullOrEmpty(pwd))
                    {
                        MessageBox.Show("请输入新密码，或点击「随机生成」", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (pwd.Length < 6)
                    {
                        MessageBox.Show("密码长度至少 6 位", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (!ValidatePassword(pwd))
                    {
                        MessageBox.Show("密码需包含数字、字母、符号中的至少两种", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (pwd != confirm)
                    {
                        MessageBox.Show("两次输入的密码不一致", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    newPassword = pwd;
                    frm.DialogResult = DialogResult.OK;
                };

                if (frm.ShowDialog(this) == DialogResult.OK)
                {
                    UserSettings s = ReadUserSettings();
                    SaveUserSettings(s.Nickname, s.Username, newPassword);
                    txtPassword.Text = "";
                    LoadMinePageData();
                    AddLog($"密码已重置: 账号={s.Username}");
                    MessageBox.Show($"新密码: {newPassword}", "密码已重置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        /// <summary>刷新传输记录列表</summary>
        private void RefreshTransferList()
        {
            if (lstTransfer == null) return;
            lstTransfer.Items.Clear();

            int total;
            lock (_transferLock)
            {
                total = _transferHistory.Count;
            }

            // 计算分页范围
            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)_transferPageSize));
            if (_transferPageIndex >= totalPages) _transferPageIndex = totalPages - 1;
            if (_transferPageIndex < 0) _transferPageIndex = 0;
            int start = _transferPageIndex * _transferPageSize;
            int endIndex = Math.Min(start + _transferPageSize, total);

            lock (_transferLock)
            {
                for (int i = start; i < endIndex; i++)
                {
                    TransferRecord item = _transferHistory[i];
                    ListViewItem lv = new ListViewItem((i + 1).ToString()) { Tag = item.Id };
                    lv.SubItems.Add(item.FileName);
                    lv.SubItems.Add(item.Direction == "upload" ? "上传" : "下载");
                    lv.SubItems.Add(item.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                    lstTransfer.Items.Add(lv);
                }
            }

            // 更新分页信息与按钮状态
            if (lblTransferPageInfo != null)
            {
                lblTransferPageInfo.Text = $"共 {total} 条 · 第 {_transferPageIndex + 1}/{totalPages} 页";
            }
            if (btnTransferPrev != null) btnTransferPrev.Enabled = _transferPageIndex > 0;
            if (btnTransferNext != null) btnTransferNext.Enabled = _transferPageIndex < totalPages - 1;
            if (btnTransferDelete != null) btnTransferDelete.Enabled = false;
        }

        /// <summary>刷新传输记录按钮</summary>
        private void BtnTransferRefresh_Click(object sender, EventArgs e)
        {
            _transferPageIndex = 0;
            RefreshTransferList();
        }

        /// <summary>勾选状态变化时更新删除按钮启用状态</summary>
        private void LstTransfer_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            int checkedCount = 0;
            foreach (ListViewItem lv in lstTransfer.Items)
            {
                if (lv.Checked) checkedCount++;
            }
            if (btnTransferDelete != null) btnTransferDelete.Enabled = checkedCount > 0;
        }

        /// <summary>每页条数变化</summary>
        private void CmbTransferPageSize_Changed(object sender, EventArgs e)
        {
            if (cmbTransferPageSize.SelectedItem != null)
            {
                int v = 10;
                if (int.TryParse(cmbTransferPageSize.SelectedItem.ToString(), out v))
                {
                    _transferPageSize = v;
                    _transferPageIndex = 0;
                    RefreshTransferList();
                }
            }
        }

        /// <summary>上一页</summary>
        private void BtnTransferPrev_Click(object sender, EventArgs e)
        {
            if (_transferPageIndex > 0)
            {
                _transferPageIndex -= 1;
                RefreshTransferList();
            }
        }

        /// <summary>下一页</summary>
        private void BtnTransferNext_Click(object sender, EventArgs e)
        {
            int total;
            lock (_transferLock)
            {
                total = _transferHistory.Count;
            }
            int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)_transferPageSize));
            if (_transferPageIndex < totalPages - 1)
            {
                _transferPageIndex += 1;
                RefreshTransferList();
            }
        }

        /// <summary>删除勾选的传输记录</summary>
        private void BtnTransferDelete_Click(object sender, EventArgs e)
        {
            // 收集勾选的记录 ID
            List<string> ids = new List<string>();
            foreach (ListViewItem lv in lstTransfer.Items)
            {
                if (lv.Checked && lv.Tag != null)
                {
                    ids.Add(lv.Tag.ToString());
                }
            }
            if (ids.Count == 0)
            {
                MessageBox.Show("请先勾选要删除的记录", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show($"确定删除勾选的 {ids.Count} 条记录吗？", "确认删除",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int removed = 0;
            lock (_transferLock)
            {
                foreach (string id in ids)
                {
                    int idx = _transferHistory.FindIndex(t => t.Id == id);
                    if (idx >= 0)
                    {
                        _transferHistory.RemoveAt(idx);
                        removed++;
                    }
                }
            }
            if (removed > 0)
            {
                SaveTransferConfig();
                AddLog($"[传输记录] 删除 {removed} 条记录");
            }
            RefreshTransferList();
        }

        /// <summary>
        /// 线程安全的日志记录
        /// </summary>
        private void AddLog(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AddLog(message)));
                return;
            }
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText($"[{timeStr}] {message}" + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();

            // 只有出现请求类日志才标记为“需要保留”，纯待机/启停日志不落盘
            if (IsRequestLogMessage(message))
            {
                _logHasUnsavedRequests = true;
            }
        }

        /// <summary>
        /// 清除日志按钮点击事件
        /// </summary>
        private void BtnClearLog_Click(object sender, EventArgs e)
        {
            // 清除前先把当前日志保存到文件，避免丢失排查信息
            SaveLogToFile();
            txtLog.Clear();
            _logHasUnsavedRequests = false;
        }
        /// <summary>
        /// 判断一条日志是否属于“请求类”（request / post 等），用于决定是否保留日志
        /// </summary>
        private static bool IsRequestLogMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            string lower = message.ToLowerInvariant();
            foreach (string kw in RequestLogKeywords)
            {
                if (lower.Contains(kw)) return true;
            }
            return false;
        }

        /// <summary>
        /// 保存日志到文件（程序目录下 Log 文件夹，文件名含时间戳）。
        /// 仅当本次日志中出现过请求（request / post 等）时才保留；
        /// 已落盘且之后没有新请求时不重复写文件。
        /// </summary>
        /// <returns>是否实际写入了文件</returns>
        private bool SaveLogToFile()
        {
            try
            {
                if (txtLog == null) return false;
                string logContent = txtLog.Text;
                if (string.IsNullOrWhiteSpace(logContent)) return false;

                // 条件：只有日志中包含 request / post 等请求时才保留
                if (!_logHasUnsavedRequests) return false;

                string fileName = "Log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                string logDir = Path.Combine(Application.StartupPath, "Log");
                //自动创建目录
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                string filePath = Path.Combine(logDir, fileName);
                File.WriteAllText(filePath, logContent, Encoding.UTF8);

                // 已落盘，重置标记，避免同一段日志被重复保存
                _logHasUnsavedRequests = false;

                // 可选：在界面上短暂提示
                AutoWindow($"日志已保存到：Log\\{fileName}", 0.5m, "日志保存");
                //保存日志后清空日志内容，避免重复保存
                txtLog.Clear();
                return true;
            }
            catch (Exception ex)
            {
                // 保存失败不影响主流程；此处可能在窗体关闭过程中执行，
                // 写 UI 日志有可能再次失败，故做静默保护，避免退出时崩溃
                try { AddLog($"保存日志失败: {ex.Message}"); } catch { }
                return false;
            }
        }

        /// <summary>下拉框索引 → 保留天数（索引 6 为“长期”，返回 0）</summary>
        private static int LogKeepDaysFromIndex(int index)
        {
            if (index < 0) return DEFAULT_LOG_KEEP_DAYS;
            if (index >= 6) return 0;              // 长期
            return 10 * (index + 1);
        }

        /// <summary>保留天数 → 下拉框索引（0 表示长期）</summary>
        private static int LogKeepIndexFromDays(int days)
        {
            if (days <= 0) return 6;               // 长期
            int idx = (days / 10) - 1;
            if (idx < 0) idx = 0;
            if (idx > 5) idx = 5;
            return idx;
        }

        /// <summary>加载日志保留天数配置</summary>
        private void LoadLogKeepConfig()
        {
            try
            {
                int days = DEFAULT_LOG_KEEP_DAYS;
                string cfgPath = GetConfigPath(LOG_KEEP_CONFIG_FILE);
                if (File.Exists(cfgPath))
                {
                    string txt = File.ReadAllText(cfgPath, Encoding.UTF8).Trim();
                    int v;
                    if (int.TryParse(txt, out v) && (v == 0 || (v >= 10 && v <= 60)))
                    {
                        days = v;
                    }
                }
                _logKeepDays = days;
                if (cmbLogKeepDays != null)
                {
                    cmbLogKeepDays.SelectedIndex = LogKeepIndexFromDays(days);
                }
                // 落盘持久化（索引未变化时 SelectedIndexChanged 不会触发，这里显式保存）
                SaveLogKeepConfig();
            }
            catch (Exception ex)
            {
                AddLog($"加载日志保留配置失败: {ex.Message}");
            }
        }

        /// <summary>保存日志保留天数配置</summary>
        private void SaveLogKeepConfig()
        {
            try
            {
                File.WriteAllText(GetConfigPath(LOG_KEEP_CONFIG_FILE), _logKeepDays.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddLog($"保存日志保留配置失败: {ex.Message}");
            }
        }

        /// <summary>清理超过保留天数的日志文件（选择“长期”时不清理）</summary>
        private void CleanupOldLogs()
        {
            try
            {
                if (_logKeepDays <= 0) return;   // 长期保留
                string logDir = Path.Combine(Application.StartupPath, "Log");
                if (!Directory.Exists(logDir)) return;

                DateTime deadline = DateTime.Now.AddDays(-_logKeepDays);
                int removed = 0;
                foreach (string file in Directory.GetFiles(logDir, "Log_*.txt"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < deadline)
                        {
                            File.Delete(file);
                            removed++;
                        }
                    }
                    catch
                    {
                        // 单个文件删除失败（被占用等）不影响其它文件
                    }
                }
                if (removed > 0)
                {
                    AddLog($"已清理 {removed} 个超过 {_logKeepDays} 天的日志文件");
                }
            }
            catch (Exception ex)
            {
                AddLog($"清理日志文件失败: {ex.Message}");
            }
        }

        /// <summary>日志保留时长下拉框选择变化</summary>
        private void CmbLogKeepDays_Changed(object sender, EventArgs e)
        {
            if (cmbLogKeepDays == null) return;
            _logKeepDays = LogKeepDaysFromIndex(cmbLogKeepDays.SelectedIndex);
            SaveLogKeepConfig();
            CleanupOldLogs();
        }
        /// <summary>
        /// 配置防火墙规则（Windows防火墙）
        /// </summary>
        private bool ConfigureFirewallRule(int port)
        {
            try
            {
                string ruleName = $"VB_FileTransfer_Port_{port}";
                string command = $"netsh advfirewall firewall show rule name=\"{ruleName}\" >nul 2>&1";

                // 检查规则是否已存在
                Process checkProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {command}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                checkProcess.Start();
                checkProcess.WaitForExit();

                // 如果规则不存在，则创建
                if (checkProcess.ExitCode != 0)
                {
                    string createCommand = $"netsh advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}";

                    Process createProcess = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c {createCommand}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            Verb = "runas" // 以管理员身份运行
                        }
                    };
                    createProcess.Start();
                    createProcess.WaitForExit();

                    if (createProcess.ExitCode == 0)
                    {
                        AutoWindow($"已自动添加防火墙规则，允许端口 {port} 的入站连接", 2, "防火墙配置");
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    // 规则已存在
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("防火墙配置失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 更新UI状态（启用/禁用控件）
        /// </summary>
        private void UpdateUIState(bool running)
        {
            isRunning = running;
            btnStart.Enabled = !running;
            btnStop.Enabled = running;
            txtIp.Enabled = !running;
            txtPort.Enabled = !running;

            // ngrok 按钮：服务运行时可用；或 ngrok 已在运行（自动监听）时可手动关闭
            if (btnNgrok != null)
            {
                btnNgrok.Enabled = running || isNgrokRunning;
            }

            if (running)
            {
                lblStatus.Text = "✅ 服务运行中";
                lblStatus.ForeColor = Color.Green;
            }
            else
            {
                lblStatus.Text = "⏹ 服务未运行";
                lblStatus.ForeColor = Color.Gray;
                if (pbQrCode != null && pbQrCode.Image != null)
                {
                    pbQrCode.Image.Dispose();
                    pbQrCode.Image = null;
                }
                if (lblLanHint != null)
                {
                    lblLanHint.ForeColor = Color.Gray;
                    lblLanHint.Text = "局域网二维码（未启动）";
                }
            }
        }

        // --- 业务逻辑 ---

        private void BtnStart_Click(object sender, EventArgs e)
        {
            // 如果当前正在等待第一个连接，则停止监听线程
            if (listenForFirstConnection)
            {
                _stopWaiting = true;
                if (tcpListener != null)
                {
                    tcpListener.Stop();
                    tcpListener = null;
                }
                if (waitThread != null && waitThread.IsAlive)
                {
                    waitThread.Join(2000);
                    waitThread = null;
                }
                listenForFirstConnection = false;
            }

            // 如果服务已经在运行，则不做任何事
            if (isRunning) return;

            // 手动启动服务
            string ip = txtIp.Text.Trim();
            string portStr = txtPort.Text.Trim();
            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(portStr))
            {
                MessageBox.Show("IP和端口不能为空", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int port;
            if (!int.TryParse(portStr, out port))
            {
                MessageBox.Show("端口必须是数字", "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                if (!ConfigureFirewallRule(port))
                {
                    MessageBox.Show("防火墙规则配置失败，服务可能无法被外部访问。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                listener = new HttpListener();
                listener.Prefixes.Add($"http://*:{port}/");
                listener.Start();

                isRunning = true;
                isServiceRunning = true;
                UpdateUIState(true);

                listenThread = new System.Threading.Thread(ListenLoop) { IsBackground = true };
                listenThread.Start();
                AddLog($"服务手动启动，监听端口 {port}，共享文件夹: {SharedFolderPath}");
                GenerateQrCode(ip, port);

                // 启动空闲检测
                lastActivityTime = DateTime.Now;
                StartInactivityTimer();
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateUIState(false);
            }
        }

        // 修改原有 btnStop_Click，支持保持监听模式
        private void BtnStop_Click(object sender, EventArgs e)
        {
            // 停止服务时：处于外网自动监听时间段内则不关闭 ngrok（保持外网隧道监听），
            // 时间段外才关闭 ngrok
            if (isNgrokRunning && !IsInNgrokTimeWindow())
            {
                StopNgrok();
            }

            StopService();

            // 停止服务时保留本次日志
            SaveLogToFile();

            // 停止后自动进入待机监听
            StartListeningForFirstConnection();
        }

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            // 【修改点5】打开程序目录下的 Shared 文件夹
            if (Directory.Exists(SharedFolderPath))
            {
                Process.Start("explorer.exe", SharedFolderPath);
            }
            else
            {
                MessageBox.Show("共享文件夹不存在，请先启动服务并上传一个文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// 监听循环
        /// </summary>
        private void ListenLoop()
        {
            while (isRunning)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    // 每个请求新开线程处理，避免阻塞
                    System.Threading.Thread t = new System.Threading.Thread(() => HandleRequest(context)) { IsBackground = true };
                    t.Start();
                }
                catch (Exception)
                {
                    // 停止时会抛出异常，退出循环
                    break;
                }
            }
        }

        // ================== 文件夹管理、登录、上传目标路径相关 ==================
        // token -> 过期时间（3 分钟后自动注销）
        private static readonly Dictionary<string, DateTime> validTokens = new Dictionary<string, DateTime>();
        private static readonly object tokenLocker = new object();
        private const int TOKEN_TIMEOUT_MINUTES = 3;

        /// <summary>获取所有子文件夹列表（相对路径）</summary>
        private void GetFoldersList(HttpListenerResponse res)
        {
            try
            {
                List<string> folders = new List<string>() { "" };   // 根目录
                if (Directory.Exists(SharedFolderPath))
                {
                    CollectSubFolders(SharedFolderPath, "", folders);
                }
                string json = JsonArrayToString(folders);

                AddLog($"GetFoldersList 返回: {json}");

                SendJsonResponse(res, json, 200);

                AddLog("文件夹列表获取成功，共 " + (folders.Count - 1) + " 个子文件夹");
            }
            catch (Exception ex)
            {
                AddLog($"GetFoldersList 异常: {ex}");

                SendJsonError(res, "获取文件夹列表失败: " + ex.Message, 500);
            }
        }

        private void CollectSubFolders(string parentPath, string relativeParent, List<string> list)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(parentPath))
                {
                    string dirName = Path.GetFileName(dir);
                    string relativePath = string.IsNullOrEmpty(relativeParent) ? dirName : relativeParent + "/" + dirName;
                    list.Add(relativePath);
                    CollectSubFolders(dir, relativePath, list);
                }
            }
            catch (Exception)
            {
                // 忽略无法访问的文件夹
            }
        }

        private string JsonArrayToString(List<string> arr)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < arr.Count; i++)
            {
                sb.Append("\"" + EscapeJsonString(arr[i]) + "\"");
                if (i < arr.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>登录验证</summary>
        private void HandleLogin(HttpListenerRequest req, HttpListenerResponse res)
        {
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                AddLog($"收到登录请求，原始数据: {body}");   // 注意：不要记录密码明文，这里仅用于调试
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string username = parameters.GetValueOrDefault("username", "");
                string password = parameters.GetValueOrDefault("password", "");
                AddLog($"登录尝试: 用户名={username}");
                // 使用可配置的账号密码（默认 admin/admin）
                UserSettings s = ReadUserSettings();
                if (username == s.Username && password == s.Password)
                {
                    string token = Guid.NewGuid().ToString();
                    lock (tokenLocker)
                    {
                        validTokens[token] = DateTime.Now.AddMinutes(TOKEN_TIMEOUT_MINUTES);
                    }
                    string json = "{\"success\":true,\"token\":\"" + token + "\",\"nickname\":\"" + EscapeJsonString(s.Nickname) + "\"}";
                    SendJsonResponse(res, json, 200);
                    AddLog($"用户 {username} 登录成功 (token {TOKEN_TIMEOUT_MINUTES} 分钟后过期)");
                }
                else
                {
                    AddLog($"用户 {username} 登录失败：密码错误");
                    SendJsonError(res, "用户名或密码错误", 401);
                }
            }
            catch (Exception ex)
            {
                AddLog($"HandleLogin 异常: {ex}");
                SendJsonError(res, "登录处理异常: " + ex.Message, 500);
            }
        }

        /// <summary>注销</summary>
        private void HandleLogout(HttpListenerRequest req, HttpListenerResponse res)
        {
            string token = req.Headers["X-Auth-Token"];
            if (!string.IsNullOrEmpty(token))
            {
                lock (tokenLocker)
                {
                    validTokens.Remove(token);
                }
            }
            SendJsonResponse(res, "{\"success\":true}", 200);
        }
        private void HandleCreateFolder(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                AddLog("新建文件夹请求未通过身份验证");
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                AddLog($"收到新建文件夹请求，原始数据: {body}");
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string parentPath = parameters.GetValueOrDefault("parentPath", "");
                string folderName = parameters.GetValueOrDefault("folderName", "");

                if (string.IsNullOrEmpty(folderName))
                {
                    SendJsonError(res, "文件夹名称不能为空", 400);
                    return;
                }
                // 安全检查
                if (folderName.Contains("..") || folderName.Contains("\\") || folderName.Contains("/"))
                {
                    SendJsonError(res, "无效的文件夹名称", 400);
                    return;
                }

                // 计算目标路径
                string targetDir;
                if (string.IsNullOrEmpty(parentPath))
                {
                    targetDir = SharedFolderPath;
                }
                else
                {
                    // 将前端传来的相对路径（使用正斜杠）转换为 Windows 路径
                    string cleanParent = parentPath.Replace("/", "\\").TrimEnd('\\');
                    targetDir = Path.Combine(SharedFolderPath, cleanParent);
                }
                string newDir = Path.Combine(targetDir, folderName);

                AddLog($"目标父文件夹: {targetDir}");
                AddLog($"新文件夹完整路径: {newDir}");

                // 确保父文件夹存在（理论上应该存在）
                if (!Directory.Exists(targetDir))
                {
                    AddLog($"父文件夹不存在，尝试创建: {targetDir}");
                    Directory.CreateDirectory(targetDir);
                }

                if (Directory.Exists(newDir))
                {
                    SendJsonError(res, "文件夹已存在", 400);
                    return;
                }

                Directory.CreateDirectory(newDir);
                AddLog($"文件夹创建成功: {newDir}");

                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleCreateFolder 异常: {ex}");
                SendJsonError(res, "创建文件夹失败: " + ex.Message, 500);
            }
        }

        /// <summary>删除文件或文件夹（需管理员认证）</summary>
        private void HandleDelete(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                AddLog("删除请求未通过身份验证");
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string targetPath = parameters.GetValueOrDefault("path", "");
                bool isAdmin = parameters.GetValueOrDefault("isAdmin", "false") == "true";

                if (string.IsNullOrEmpty(targetPath))
                {
                    SendJsonError(res, "路径不能为空", 400);
                    return;
                }
                // 安全检查：禁止路径遍历
                if (targetPath.Contains("..") || targetPath.Contains("::"))
                {
                    SendJsonError(res, "非法路径", 403);
                    return;
                }

                // 计算实际删除路径
                string fullPath;
                if (isAdmin)
                {
                    // 管理员全盘浏览：使用绝对路径
                    fullPath = Uri.UnescapeDataString(targetPath);
                }
                else
                {
                    // 普通浏览：相对于共享文件夹
                    string cleanPath = targetPath.Replace("/", "\\").TrimStart('\\');
                    fullPath = Path.Combine(SharedFolderPath, cleanPath);
                }
                fullPath = Path.GetFullPath(fullPath);

                // 防止删除共享目录本身或越界
                string sharedRoot = Path.GetFullPath(SharedFolderPath).TrimEnd('\\') + "\\";
                if (!isAdmin && !fullPath.StartsWith(sharedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    SendJsonError(res, "越界路径", 403);
                    return;
                }

                AddLog($"删除请求: {fullPath} (isAdmin={isAdmin})");

                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    AddLog($"文件夹已删除: {fullPath}");
                }
                else if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    AddLog($"文件已删除: {fullPath}");
                }
                else
                {
                    SendJsonError(res, "目标不存在", 404);
                    return;
                }

                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleDelete 异常: {ex}");
                SendJsonError(res, "删除失败: " + ex.Message, 500);
            }
        }

        // ================== 文件管理（重命名/复制/剪切/粘贴） ==================

        /// <summary>重命名文件或文件夹（需认证）</summary>
        private void HandleRename(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string targetPath = parameters.GetValueOrDefault("path", "");
                string newName = parameters.GetValueOrDefault("newName", "");
                bool isAdmin = parameters.GetValueOrDefault("isAdmin", "false") == "true";

                if (string.IsNullOrEmpty(targetPath) || string.IsNullOrEmpty(newName))
                {
                    SendJsonError(res, "参数不完整", 400);
                    return;
                }
                if (newName.Contains("\\") || newName.Contains("/") || newName.Contains(".."))
                {
                    SendJsonError(res, "非法名称", 400);
                    return;
                }
                if (targetPath.Contains(".."))
                {
                    SendJsonError(res, "非法路径", 403);
                    return;
                }

                string fullPath = isAdmin ? Uri.UnescapeDataString(targetPath) : Path.Combine(SharedFolderPath, targetPath.Replace("/", "\\").TrimStart('\\'));
                fullPath = Path.GetFullPath(fullPath);

                string dir = Path.GetDirectoryName(fullPath);
                string newFullPath = Path.Combine(dir, newName);

                if (File.Exists(fullPath))
                {
                    File.Move(fullPath, newFullPath);
                    AddLog($"文件重命名: {fullPath} -> {newName}");
                }
                else if (Directory.Exists(fullPath))
                {
                    Directory.Move(fullPath, newFullPath);
                    AddLog($"文件夹重命名: {fullPath} -> {newName}");
                }
                else
                {
                    SendJsonError(res, "目标不存在", 404);
                    return;
                }
                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleRename 异常: {ex}");
                SendJsonError(res, "重命名失败: " + ex.Message, 500);
            }
        }

        /// <summary>复制或剪切（记录到剪贴板）</summary>
        private void HandleCopyMove(HttpListenerRequest req, HttpListenerResponse res, bool isCut)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string targetPath = parameters.GetValueOrDefault("path", "");
                bool isAdmin = parameters.GetValueOrDefault("isAdmin", "false") == "true";

                if (string.IsNullOrEmpty(targetPath))
                {
                    SendJsonError(res, "路径不能为空", 400);
                    return;
                }

                string fullPath = isAdmin ? Uri.UnescapeDataString(targetPath) : Path.Combine(SharedFolderPath, targetPath.Replace("/", "\\").TrimStart('\\'));
                fullPath = Path.GetFullPath(fullPath);

                lock (_clipboardLock)
                {
                    _clipboardPath = fullPath;
                    _clipboardIsCut = isCut;
                }

                AddLog($"{(isCut ? "剪切" : "复制")}: {fullPath}");
                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleCopyMove 异常: {ex}");
                SendJsonError(res, "操作失败: " + ex.Message, 500);
            }
        }

        /// <summary>粘贴（将剪贴板内容复制/移动到目标目录）</summary>
        private void HandlePaste(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string destDir = parameters.GetValueOrDefault("destDir", "");
                bool isAdmin = parameters.GetValueOrDefault("isAdmin", "false") == "true";

                string srcPath;
                bool isCut;
                lock (_clipboardLock)
                {
                    srcPath = _clipboardPath;
                    isCut = _clipboardIsCut;
                }

                if (string.IsNullOrEmpty(srcPath))
                {
                    SendJsonError(res, "剪贴板为空，请先复制或剪切", 400);
                    return;
                }

                // 目标目录
                string fullDest = isAdmin ? Uri.UnescapeDataString(destDir) : Path.Combine(SharedFolderPath, destDir.Replace("/", "\\").TrimStart('\\'));
                fullDest = Path.GetFullPath(fullDest);
                if (!Directory.Exists(fullDest))
                {
                    SendJsonError(res, "目标目录不存在", 404);
                    return;
                }

                string srcName = Path.GetFileName(srcPath);
                string destFull = Path.Combine(fullDest, srcName);

                // 避免复制/移动到自身
                if (string.Equals(srcPath, destFull, StringComparison.OrdinalIgnoreCase))
                {
                    SendJsonError(res, "不能粘贴到原位置", 400);
                    return;
                }

                if (isCut)
                {
                    if (File.Exists(srcPath))
                    {
                        if (File.Exists(destFull)) File.Delete(destFull);
                        File.Move(srcPath, destFull);
                    }
                    else if (Directory.Exists(srcPath))
                    {
                        if (Directory.Exists(destFull)) Directory.Delete(destFull, true);
                        Directory.Move(srcPath, destFull);
                    }
                    lock (_clipboardLock)
                    {
                        _clipboardPath = "";
                        _clipboardIsCut = false;
                    }
                    AddLog($"剪切粘贴: {srcPath} -> {destFull}");
                }
                else
                {
                    if (File.Exists(srcPath))
                    {
                        if (File.Exists(destFull))
                        {
                            // 同名文件，追加 "(1)"
                            destFull = GetUniquePath(destFull);
                        }
                        File.Copy(srcPath, destFull);
                    }
                    else if (Directory.Exists(srcPath))
                    {
                        if (Directory.Exists(destFull))
                        {
                            destFull = GetUniquePath(destFull);
                        }
                        CopyDirectory(srcPath, destFull);
                    }
                    AddLog($"复制粘贴: {srcPath} -> {destFull}");
                }

                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandlePaste 异常: {ex}");
                SendJsonError(res, "粘贴失败: " + ex.Message, 500);
            }
        }

        /// <summary>生成不重复的目标路径（追加 (1)、(2)...）</summary>
        private string GetUniquePath(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            string nameNoExt = Path.GetFileNameWithoutExtension(fullPath);
            string ext = Path.GetExtension(fullPath);
            int counter = 1;
            string candidate = fullPath;
            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{nameNoExt} ({counter}){ext}");
                counter += 1;
            }
            return candidate;
        }

        /// <summary>递归复制目录</summary>
        private void CopyDirectory(string srcDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string srcFile in Directory.GetFiles(srcDir))
            {
                File.Copy(srcFile, Path.Combine(destDir, Path.GetFileName(srcFile)));
            }
            foreach (string dir in Directory.GetDirectories(srcDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }

        // ================== 最近浏览记录 ==================

        /// <summary>记录一次浏览行为</summary>
        private void RecordRecentView(string path, string name, bool isFolder, long size, string category)
        {
            lock (_recentLock)
            {
                // 移除已存在的相同路径
                _recentFiles.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
                _recentFiles.Insert(0, new RecentFileItem
                {
                    Path = path,
                    Name = name,
                    IsFolder = isFolder,
                    Size = size,
                    Category = category,
                    LastViewed = DateTime.Now
                });
                // 限制数量
                while (_recentFiles.Count > MAX_RECENT_COUNT)
                {
                    _recentFiles.RemoveAt(_recentFiles.Count - 1);
                }
            }
            SaveRecentConfig();
        }

        /// <summary>保存最近浏览记录</summary>
        private void SaveRecentConfig()
        {
            try
            {
                string cfgPath = GetConfigPath(RECENT_CONFIG_FILE);
                List<string> lines = new List<string>();
                lock (_recentLock)
                {
                    foreach (RecentFileItem item in _recentFiles)
                    {
                        lines.Add($"{item.Path}|{item.Name}|{(item.IsFolder ? 1 : 0)}|{item.Size}|{item.Category}|{item.LastViewed:yyyy-MM-dd HH:mm:ss}");
                    }
                }
                File.WriteAllLines(cfgPath, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddLog($"保存最近浏览记录失败: {ex.Message}");
            }
        }

        /// <summary>加载最近浏览记录</summary>
        private void LoadRecentConfig()
        {
            try
            {
                string cfgPath = GetConfigPath(RECENT_CONFIG_FILE);
                if (!File.Exists(cfgPath)) return;
                string[] lines = File.ReadAllLines(cfgPath, Encoding.UTF8);
                lock (_recentLock)
                {
                    _recentFiles.Clear();
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length >= 6)
                        {
                            bool isFolder = parts[2] == "1";
                            long size = 0;
                            long.TryParse(parts[3], out size);
                            DateTime viewed = DateTime.Now;
                            DateTime.TryParse(parts[5], out viewed);
                            _recentFiles.Add(new RecentFileItem
                            {
                                Path = parts[0],
                                Name = parts[1],
                                IsFolder = isFolder,
                                Size = size,
                                Category = parts[4],
                                LastViewed = viewed
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载最近浏览记录失败: {ex.Message}");
            }
        }

        /// <summary>获取最近浏览记录</summary>
        private void HandleRecentFiles(HttpListenerResponse res)
        {
            try
            {
                List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                lock (_recentLock)
                {
                    foreach (RecentFileItem item in _recentFiles)
                    {
                        // 跳过已不存在的文件
                        if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) continue;
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["name"] = item.Name;
                        d["path"] = item.Path;
                        d["type"] = item.IsFolder ? "folder" : "file";
                        d["size"] = item.Size;
                        d["category"] = item.Category;
                        d["lastViewed"] = item.LastViewed.ToString("yyyy-MM-dd HH:mm:ss");
                        items.Add(d);
                    }
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("{\"items\":[");
                for (int i = 0; i < items.Count; i++)
                {
                    Dictionary<string, object> d = items[i];
                    sb.Append("{");
                    sb.Append("\"name\":\"" + EscapeJsonString(d["name"].ToString()) + "\",");
                    sb.Append("\"path\":\"" + EscapeJsonString(d["path"].ToString()) + "\",");
                    sb.Append("\"type\":\"" + d["type"].ToString() + "\",");
                    sb.Append("\"size\":" + d["size"].ToString() + ",");
                    sb.Append("\"category\":\"" + EscapeJsonString(d["category"].ToString()) + "\",");
                    sb.Append("\"lastViewed\":\"" + EscapeJsonString(d["lastViewed"].ToString()) + "\"");
                    sb.Append("}");
                    if (i < items.Count - 1) sb.Append(",");
                }
                sb.Append("]}");
                SendJsonResponse(res, sb.ToString(), 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleRecentFiles 异常: {ex}");
                SendJsonError(res, "获取最近浏览失败: " + ex.Message, 500);
            }
        }

        // ================== 传输历史记录 ==================

        /// <summary>记录一次传输事件（上传/下载）</summary>
        private void RecordTransfer(string fileName, string direction, long size, string clientIP, string filePath)
        {
            lock (_transferLock)
            {
                _transferHistory.Insert(0, new TransferRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FileName = fileName,
                    Direction = direction,
                    Size = size,
                    Time = DateTime.Now,
                    ClientIP = clientIP,
                    FilePath = filePath
                });
                while (_transferHistory.Count > MAX_TRANSFER_COUNT)
                {
                    _transferHistory.RemoveAt(_transferHistory.Count - 1);
                }
                _transferVersion += 1;
            }
            SaveTransferConfig();
        }

        /// <summary>保存传输历史记录</summary>
        private void SaveTransferConfig()
        {
            try
            {
                string cfgPath = GetConfigPath(TRANSFER_CONFIG_FILE);
                List<string> lines = new List<string>();
                lock (_transferLock)
                {
                    foreach (TransferRecord item in _transferHistory)
                    {
                        lines.Add($"{item.Id}|{item.FileName}|{item.Direction}|{item.Size}|{item.Time:yyyy-MM-dd HH:mm:ss}|{item.ClientIP}|{item.FilePath}");
                    }
                }
                File.WriteAllLines(cfgPath, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddLog($"保存传输历史失败: {ex.Message}");
            }
        }

        /// <summary>加载传输历史记录</summary>
        private void LoadTransferConfig()
        {
            try
            {
                string cfgPath = GetConfigPath(TRANSFER_CONFIG_FILE);
                if (!File.Exists(cfgPath)) return;
                string[] lines = File.ReadAllLines(cfgPath, Encoding.UTF8);
                lock (_transferLock)
                {
                    _transferHistory.Clear();
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length >= 7)
                        {
                            long size = 0;
                            long.TryParse(parts[3], out size);
                            DateTime t = DateTime.Now;
                            DateTime.TryParse(parts[4], out t);
                            _transferHistory.Add(new TransferRecord
                            {
                                Id = parts[0],
                                FileName = parts[1],
                                Direction = parts[2],
                                Size = size,
                                Time = t,
                                ClientIP = parts[5],
                                FilePath = parts[6]
                            });
                        }
                        else if (parts.Length == 6)
                        {
                            // 兼容 6 段格式（无 FilePath）
                            long size = 0;
                            long.TryParse(parts[3], out size);
                            DateTime t = DateTime.Now;
                            DateTime.TryParse(parts[4], out t);
                            _transferHistory.Add(new TransferRecord
                            {
                                Id = parts[0],
                                FileName = parts[1],
                                Direction = parts[2],
                                Size = size,
                                Time = t,
                                ClientIP = parts[5]
                            });
                        }
                        else if (parts.Length == 5)
                        {
                            // 兼容旧格式（无 Id 字段），自动生成新 Id
                            long size = 0;
                            long.TryParse(parts[2], out size);
                            DateTime t = DateTime.Now;
                            DateTime.TryParse(parts[3], out t);
                            _transferHistory.Add(new TransferRecord
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                FileName = parts[0],
                                Direction = parts[1],
                                Size = size,
                                Time = t,
                                ClientIP = parts[4]
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载传输历史失败: {ex.Message}");
            }
        }

        /// <summary>获取传输历史记录（共享页）</summary>
        private void HandleTransferHistory(HttpListenerResponse res)
        {
            try
            {
                List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                long version;
                lock (_transferLock)
                {
                    version = _transferVersion;
                    foreach (TransferRecord item in _transferHistory)
                    {
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["id"] = item.Id;
                        d["fileName"] = item.FileName;
                        d["direction"] = item.Direction;
                        d["size"] = item.Size;
                        d["time"] = item.Time.ToString("yyyy-MM-dd HH:mm:ss");
                        d["clientIP"] = item.ClientIP;
                        // 判断文件是否仍存在
                        bool exists = false;
                        if (!string.IsNullOrEmpty(item.FilePath))
                        {
                            exists = File.Exists(item.FilePath) || Directory.Exists(item.FilePath);
                        }
                        else
                        {
                            // 无路径记录时，回退检查共享文件夹根目录
                            string probe = Path.Combine(SharedFolderPath, item.FileName);
                            exists = File.Exists(probe) || Directory.Exists(probe);
                        }
                        d["exists"] = exists;
                        items.Add(d);
                    }
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("{\"version\":" + version + ",\"items\":[");
                for (int i = 0; i < items.Count; i++)
                {
                    Dictionary<string, object> d = items[i];
                    sb.Append("{");
                    sb.Append("\"id\":\"" + EscapeJsonString(d["id"].ToString()) + "\",");
                    sb.Append("\"fileName\":\"" + EscapeJsonString(d["fileName"].ToString()) + "\",");
                    sb.Append("\"direction\":\"" + d["direction"].ToString() + "\",");
                    sb.Append("\"size\":" + d["size"].ToString() + ",");
                    sb.Append("\"time\":\"" + EscapeJsonString(d["time"].ToString()) + "\",");
                    sb.Append("\"clientIP\":\"" + EscapeJsonString(d["clientIP"].ToString()) + "\",");
                    sb.Append("\"exists\":" + (Convert.ToBoolean(d["exists"]) ? "true" : "false"));
                    sb.Append("}");
                    if (i < items.Count - 1) sb.Append(",");
                }
                sb.Append("]}");
                SendJsonResponse(res, sb.ToString(), 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleTransferHistory 异常: {ex}");
                SendJsonError(res, "获取传输历史失败: " + ex.Message, 500);
            }
        }

        /// <summary>删除传输历史记录（支持单个或多个）</summary>
        private void HandleTransferDelete(HttpListenerRequest req, HttpListenerResponse res)
        {
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                // 支持 JSON 数组或表单 ids=id1,id2
                List<string> ids = new List<string>();
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string idsParam = parameters.GetValueOrDefault("ids", "");
                if (!string.IsNullOrEmpty(idsParam))
                {
                    foreach (string id in idsParam.Split(','))
                    {
                        if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
                    }
                }

                if (ids.Count == 0)
                {
                    SendJsonError(res, "缺少要删除的记录 ID", 400);
                    return;
                }

                int removed = 0;
                lock (_transferLock)
                {
                    foreach (string id in ids)
                    {
                        int idx = _transferHistory.FindIndex(t => t.Id == id);
                        if (idx >= 0)
                        {
                            _transferHistory.RemoveAt(idx);
                            removed++;
                        }
                    }
                    if (removed > 0) _transferVersion += 1;
                }

                if (removed > 0)
                {
                    SaveTransferConfig();
                    AddLog($"[传输记录] 删除 {removed} 条记录");
                    // 同步刷新主窗体共享页列表（网页端删除时立即同步，无需定时器）
                    try
                    {
                        if (this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                if (lstTransfer != null)
                                {
                                    RefreshTransferList();
                                }
                            }));
                        }
                    }
                    catch
                    {
                    }
                }

                SendJsonResponse(res, "{\"success\":true,\"removed\":" + removed + "}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleTransferDelete 异常: {ex}");
                SendJsonError(res, "删除传输记录失败: " + ex.Message, 500);
            }
        }

        // ================== 服务控制接口 ==================

        /// <summary>获取服务状态</summary>
        private void HandleServiceStatus(HttpListenerResponse res)
        {
            try
            {
                string ip = GetLocalIpAddress();
                int port = DefaultPort;
                if (txtPort != null && !string.IsNullOrEmpty(txtPort.Text))
                {
                    int.TryParse(txtPort.Text.Trim(), out port);
                }
                string lanUrl = $"http://{ip}:{port}/";

                StringBuilder sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"running\":" + (isRunning ? "true" : "false") + ",");
                sb.Append("\"ngrokRunning\":" + (isNgrokRunning ? "true" : "false") + ",");
                sb.Append("\"ip\":\"" + EscapeJsonString(ip) + "\",");
                sb.Append("\"port\":" + port + ",");
                sb.Append("\"lanUrl\":\"" + EscapeJsonString(lanUrl) + "\",");
                sb.Append("\"ngrokUrl\":\"" + EscapeJsonString(ngrokUrl) + "\"");
                sb.Append("}");
                SendJsonResponse(res, sb.ToString(), 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleServiceStatus 异常: {ex}");
                SendJsonError(res, "获取服务状态失败: " + ex.Message, 500);
            }
        }

        /// <summary>启动服务</summary>
        private void HandleServiceStart(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                if (isRunning)
                {
                    SendJsonResponse(res, "{\"success\":true,\"message\":\"服务已在运行\"}", 200);
                    return;
                }
                // 在 UI 线程执行启动
                this.Invoke(new Action(() => BtnStart_Click(null, null)));
                SendJsonResponse(res, "{\"success\":true}", 200);
                AddLog("通过网页启动服务");
            }
            catch (Exception ex)
            {
                AddLog($"HandleServiceStart 异常: {ex}");
                SendJsonError(res, "启动服务失败: " + ex.Message, 500);
            }
        }

        /// <summary>停止服务</summary>
        private void HandleServiceStop(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                if (!isRunning)
                {
                    SendJsonResponse(res, "{\"success\":true,\"message\":\"服务未运行\"}", 200);
                    return;
                }
                this.Invoke(new Action(() => BtnStop_Click(null, null)));
                SendJsonResponse(res, "{\"success\":true}", 200);
                AddLog("通过网页停止服务");
            }
            catch (Exception ex)
            {
                AddLog($"HandleServiceStop 异常: {ex}");
                SendJsonError(res, "停止服务失败: " + ex.Message, 500);
            }
        }

        /// <summary>开启外网（ngrok）</summary>
        private void HandleNgrokStart(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                if (isNgrokRunning)
                {
                    SendJsonResponse(res, "{\"success\":true,\"message\":\"外网已开启\"}", 200);
                    return;
                }
                this.Invoke(new Action(() => BtnNgrok_Click(null, null)));
                SendJsonResponse(res, "{\"success\":true}", 200);
                AddLog("通过网页开启外网");
            }
            catch (Exception ex)
            {
                AddLog($"HandleNgrokStart 异常: {ex}");
                SendJsonError(res, "开启外网失败: " + ex.Message, 500);
            }
        }

        /// <summary>关闭外网（ngrok）</summary>
        private void HandleNgrokStop(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                if (!isNgrokRunning)
                {
                    SendJsonResponse(res, "{\"success\":true,\"message\":\"外网未开启\"}", 200);
                    return;
                }
                this.Invoke(new Action(() => BtnNgrok_Click(null, null)));
                SendJsonResponse(res, "{\"success\":true}", 200);
                AddLog("通过网页关闭外网");
            }
            catch (Exception ex)
            {
                AddLog($"HandleNgrokStop 异常: {ex}");
                SendJsonError(res, "关闭外网失败: " + ex.Message, 500);
            }
        }

        // ================== 用户设置 ==================

        /// <summary>读取用户设置（账号密码、昵称）</summary>
        private UserSettings ReadUserSettings()
        {
            string nickname = DEFAULT_NICKNAME;
            string username = DEFAULT_USERNAME;
            string password = DEFAULT_PASSWORD;
            try
            {
                string cfgPath = GetConfigPath(SETTINGS_FILE);
                if (File.Exists(cfgPath))
                {
                    string[] parts = File.ReadAllText(cfgPath, Encoding.UTF8).Trim().Split('|');
                    if (parts.Length >= 3)
                    {
                        nickname = parts[0];
                        username = parts[1];
                        password = parts[2];
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"读取用户设置失败: {ex.Message}");
            }
            return new UserSettings { Nickname = nickname, Username = username, Password = password };
        }

        /// <summary>保存用户设置</summary>
        private void SaveUserSettings(string nickname, string username, string password)
        {
            try
            {
                string cfgPath = GetConfigPath(SETTINGS_FILE);
                File.WriteAllText(cfgPath, $"{nickname}|{username}|{password}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddLog($"保存用户设置失败: {ex.Message}");
            }
        }

        /// <summary>获取用户设置（不返回密码明文）</summary>
        private void HandleSettingsGet(HttpListenerResponse res)
        {
            try
            {
                UserSettings s = ReadUserSettings();
                StringBuilder sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"nickname\":\"" + EscapeJsonString(s.Nickname) + "\",");
                sb.Append("\"username\":\"" + EscapeJsonString(s.Username) + "\",");
                sb.Append("\"hasPassword\":" + (string.IsNullOrEmpty(s.Password) ? "false" : "true"));
                sb.Append("}");
                SendJsonResponse(res, sb.ToString(), 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleSettingsGet 异常: {ex}");
                SendJsonError(res, "获取设置失败: " + ex.Message, 500);
            }
        }

        /// <summary>保存用户设置（仅昵称；账号不可修改，密码单独走 changepassword 接口）</summary>
        private void HandleSettingsSave(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string nickname = parameters.GetValueOrDefault("nickname", "");

                if (string.IsNullOrWhiteSpace(nickname))
                {
                    SendJsonError(res, "昵称不能为空", 400);
                    return;
                }

                UserSettings s = ReadUserSettings();
                SaveUserSettings(nickname.Trim(), s.Username, s.Password);
                AddLog($"昵称已保存: {nickname}");
                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleSettingsSave 异常: {ex}");
                SendJsonError(res, "保存设置失败: " + ex.Message, 500);
            }
        }

        /// <summary>修改密码（需验证原密码）</summary>
        private void HandleChangePassword(HttpListenerRequest req, HttpListenerResponse res)
        {
            if (!IsAuthenticated(req))
            {
                SendJsonError(res, "未登录或登录已过期", 401);
                return;
            }
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string oldPassword = parameters.GetValueOrDefault("oldPassword", "");
                string newPassword = parameters.GetValueOrDefault("newPassword", "");

                if (string.IsNullOrEmpty(newPassword))
                {
                    SendJsonError(res, "新密码不能为空", 400);
                    return;
                }

                UserSettings s = ReadUserSettings();
                if (s.Password != oldPassword)
                {
                    SendJsonError(res, "原密码错误", 403);
                    return;
                }

                SaveUserSettings(s.Nickname, s.Username, newPassword);
                AddLog($"密码已修改: 账号={s.Username}");
                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleChangePassword 异常: {ex}");
                SendJsonError(res, "修改密码失败: " + ex.Message, 500);
            }
        }

        /// <summary>重置设置（需 PIN 码）</summary>
        private void HandleSettingsReset(HttpListenerRequest req, HttpListenerResponse res)
        {
            try
            {
                string body;
                using (StreamReader reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    body = reader.ReadToEnd();
                }
                Dictionary<string, string> parameters = ParseFormUrlEncoded(body);
                string pin = parameters.GetValueOrDefault("pin", "");

                // 重置需要输入电脑 PIN 码，若无法获取则使用默认 2580
                string expectedPin = RESET_PIN;
                // 尝试获取 Windows PIN（通过检查是否存在相关机制，这里简化为固定值）
                if (string.IsNullOrEmpty(pin) || pin != expectedPin)
                {
                    SendJsonError(res, "PIN 码错误", 403);
                    return;
                }

                // 重置为默认设置
                SaveUserSettings(DEFAULT_NICKNAME, DEFAULT_USERNAME, DEFAULT_PASSWORD);
                AddLog("用户设置已重置为默认值");
                SendJsonResponse(res, "{\"success\":true}", 200);
            }
            catch (Exception ex)
            {
                AddLog($"HandleSettingsReset 异常: {ex}");
                SendJsonError(res, "重置失败: " + ex.Message, 500);
            }
        }

        /// <summary>解析 application/x-www-form-urlencoded 字符串</summary>
        private Dictionary<string, string> ParseFormUrlEncoded(string data)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(data)) return dict;
            foreach (string pair in data.Split('&'))
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2)
                {
                    string key = Uri.UnescapeDataString(kv[0]);
                    string value = Uri.UnescapeDataString(kv[1]);
                    dict[key] = value;
                }
            }
            return dict;
        }

        private bool IsAuthenticated(HttpListenerRequest req)
        {
            string token = req.Headers["X-Auth-Token"];
            if (string.IsNullOrEmpty(token)) return false;
            lock (tokenLocker)
            {
                DateTime expiry;
                if (validTokens.TryGetValue(token, out expiry))
                {
                    // 检查是否过期，过期则移除
                    if (expiry > DateTime.Now)
                    {
                        return true;
                    }
                    else
                    {
                        validTokens.Remove(token);
                        return false;
                    }
                }
                return false;
            }
        }


        private void SendJsonResponse(HttpListenerResponse res, string json, int statusCode)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(json);   // json 应该是正确格式，如 {"success":true}
                res.ContentType = "application/json; charset=utf-8";
                res.StatusCode = statusCode;
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                // 客户端断开连接（如反复刷新）或响应已提交：属正常网络情况，
                // 必须静默处理，否则异常上抛会触发连锁崩溃（进程退出）
                AddLog($"[HTTP] 发送响应失败(客户端可能已断开): {ex.Message}");
            }
        }

        // ================== 用户设置辅助类型 ==================
        private class UserSettings
        {
            public string Nickname;
            public string Username;
            public string Password;
        }

        private void SendJsonError(HttpListenerResponse res, string message, int statusCode)
        {
            string json = "{\"error\":\"" + EscapeJsonString(message) + "\"}";
            SendJsonResponse(res, json, statusCode);
        }

        /// <summary>
        /// 处理单个 HTTP 请求
        /// </summary>
        private void HandleRequest(HttpListenerContext context)
        {
            // 更新最后活动时间
            lastActivityTime = DateTime.Now;

            HttpListenerRequest req = context.Request;
            HttpListenerResponse res = context.Response;

            try
            {
                string path = req.Url.AbsolutePath;
                // 获取客户端信息
                string clientIP = "";
                string userAgent = "";
                try
                {
                    IPEndPoint remoteEndPoint = (IPEndPoint)req.RemoteEndPoint;
                    if (remoteEndPoint != null)
                    {
                        clientIP = remoteEndPoint.Address.ToString();
                    }
                }
                catch
                {
                    clientIP = "unknown";
                }
                userAgent = req.UserAgent;
                _lastClientIP = clientIP;

                if (req.HttpMethod == "GET")
                {
                    if (path == "/" || path == "/index.html")
                    {
                        AddLog($"IP:{clientIP} UA:{userAgent} 访问首页");
                        SendHtml(res, clientIP, userAgent);
                    }
                    else if (path.StartsWith("/download/"))
                    {
                        string fileName = Uri.UnescapeDataString(path.Substring("/download/".Length));
                        AddLog($"IP:{clientIP} UA:{userAgent} 请求下载: {fileName}");
                        SendFile(res, fileName);
                    }
                    else if (path == "/list")
                    {
                        // 新增：返回文件列表
                        SendFileList(res);
                    }
                    else if (path.StartsWith("/info/"))
                    {
                        // 新增：返回文件信息
                        string fileName = Uri.UnescapeDataString(path.Substring("/info/".Length));
                        SendFileInfo(res, fileName);
                    }
                    else if (path == "/getFolders")
                    {
                        GetFoldersList(res);
                        //浏览文件夹：
                    }
                    else if (path.StartsWith("/browse/"))
                    {
                        // 浏览文件夹内容
                        string folderPath = Uri.UnescapeDataString(path.Substring("/browse/".Length));
                        AddLog($"IP:{clientIP} 浏览文件夹: {folderPath}");
                        BrowseFolder(res, folderPath);

                    }
                    else if (path == "/logout")
                    {
                        HandleLogout(req, res);

                    }
                    else if (path == "/getDrives" && IsAuthenticated(req))
                    {
                        GetDrivesList(res);
                    }
                    else if (path.StartsWith("/browseAll/") && IsAuthenticated(req))
                    {
                        string targetPath = Uri.UnescapeDataString(path.Substring("/browseAll/".Length));
                        BrowseAll(res, targetPath);
                    }
                    else if (path.StartsWith("/downloadAll/") && IsAuthenticated(req))
                    {
                        string filePath = Uri.UnescapeDataString(path.Substring("/downloadAll/".Length));
                        DownloadAll(res, filePath);
                    }
                    else if (path == "/recent")
                    {
                        HandleRecentFiles(res);
                    }
                    else if (path == "/transfer/history")
                    {
                        HandleTransferHistory(res);
                    }
                    else if (path == "/service/status")
                    {
                        HandleServiceStatus(res);
                    }
                    else if (path == "/settings/get")
                    {
                        HandleSettingsGet(res);
                    }
                    else if (path == "/archive/list")
                    {
                        HandleArchiveList(req, res);
                    }
                    else if (path.StartsWith("/libs/"))
                    {
                        // 静态资源（本地 JS 库等）
                        SendStaticFile(res, path.Substring("/libs/".Length));
                    }
                    else
                    {
                        Send404(res);
                    }
                }
                else if (req.HttpMethod == "POST")
                {
                    if (path == "/upload")
                    {
                        HandleUpload(req, res, clientIP, userAgent);
                    }
                    else if (path == "/login")
                    {
                        HandleLogin(req, res);
                    }
                    else if (path == "/createFolder")
                    {
                        HandleCreateFolder(req, res);
                    }
                    else if (path == "/delete")
                    {
                        HandleDelete(req, res);
                    }
                    else if (path == "/rename")
                    {
                        HandleRename(req, res);
                    }
                    else if (path == "/copy")
                    {
                        HandleCopyMove(req, res, false);
                    }
                    else if (path == "/cut")
                    {
                        HandleCopyMove(req, res, true);
                    }
                    else if (path == "/paste")
                    {
                        HandlePaste(req, res);
                    }
                    else if (path == "/service/start")
                    {
                        HandleServiceStart(req, res);
                    }
                    else if (path == "/service/stop")
                    {
                        HandleServiceStop(req, res);
                    }
                    else if (path == "/ngrok/start")
                    {
                        HandleNgrokStart(req, res);
                    }
                    else if (path == "/ngrok/stop")
                    {
                        HandleNgrokStop(req, res);
                    }
                    else if (path == "/settings/save")
                    {
                        HandleSettingsSave(req, res);
                    }
                    else if (path == "/settings/changepassword")
                    {
                        HandleChangePassword(req, res);
                    }
                    else if (path == "/settings/reset")
                    {
                        HandleSettingsReset(req, res);
                    }
                    else if (path == "/transfer/delete")
                    {
                        HandleTransferDelete(req, res);
                    }
                    else
                    {
                        Send404(res);
                    }
                }
                else
                {
                    Send404(res);
                }
            }
            catch (Exception ex)
            {
                AddLog($"HandleRequest 全局异常: {ex}");
                try
                {
                    Send500(res, ex.Message);
                }
                catch
                {
                    // 响应已提交或客户端断开：绝不让异常从 catch 块逃逸（否则进程崩溃）
                }
            }
            finally
            {
                try
                {
                    res.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>获取所有磁盘根目录（管理员）</summary>
        private void GetDrivesList(HttpListenerResponse res)
        {
            try
            {
                List<Dictionary<string, object>> drivesInfo = new List<Dictionary<string, object>>();
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        // 构建显示名称，如 "本地磁盘 (C:)"
                        string volumeLabel = drive.VolumeLabel;
                        if (string.IsNullOrEmpty(volumeLabel))
                        {
                            volumeLabel = "本地磁盘";
                        }
                        string driveLetter = drive.Name.TrimEnd('\\');
                        string displayName = $"{volumeLabel} ({driveLetter})";

                        Dictionary<string, object> info = new Dictionary<string, object>();
                        info["name"] = displayName;
                        info["path"] = drive.Name;
                        info["freeSpace"] = drive.AvailableFreeSpace;
                        info["totalSize"] = drive.TotalSize;
                        drivesInfo.Add(info);
                    }
                }

                // 手动构建 JSON 字符串（避免引入额外库）
                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("[");
                for (int i = 0; i < drivesInfo.Count; i++)
                {
                    Dictionary<string, object> d = drivesInfo[i];
                    jsonBuilder.Append("{");
                    jsonBuilder.Append("\"name\":\"" + EscapeJsonString(d["name"].ToString()) + "\",");
                    jsonBuilder.Append("\"path\":\"" + EscapeJsonString(d["path"].ToString()) + "\",");
                    jsonBuilder.Append("\"freeSpace\":" + d["freeSpace"] + ",");
                    jsonBuilder.Append("\"totalSize\":" + d["totalSize"]);
                    jsonBuilder.Append("}");
                    if (i < drivesInfo.Count - 1) jsonBuilder.Append(",");
                }
                jsonBuilder.Append("]");
                string json = jsonBuilder.ToString();

                SendJsonResponse(res, json, 200);
            }
            catch (Exception ex)
            {
                AddLog($"GetDrivesList 异常: {ex}");
                SendJsonError(res, "获取磁盘列表失败: " + ex.Message, 500);
            }
        }

        /// <summary>浏览任意路径（管理员）</summary>
        private void BrowseAll(HttpListenerResponse res, string targetPath)
        {
            try
            {
                targetPath = Uri.UnescapeDataString(targetPath);
                // 安全检查：禁止路径遍历
                if (targetPath.Contains("..") || targetPath.Contains("::"))
                {
                    SendTextError(res, "非法路径", 403);
                    return;
                }

                // 如果路径为空，返回磁盘列表（相当于根目录）
                if (string.IsNullOrEmpty(targetPath))
                {
                    GetDrivesList(res);
                    return;
                }

                string fullPath = targetPath;
                if (!Directory.Exists(fullPath))
                {
                    SendJsonError(res, "目录不存在", 404);
                    return;
                }

                List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();

                // 添加上级目录（如果不是根目录）
                if (!string.IsNullOrEmpty(targetPath) && targetPath.Length > 3)
                {
                    string parent = Directory.GetParent(fullPath).FullName;
                    Dictionary<string, object> parentItem = new Dictionary<string, object>();
                    parentItem["name"] = "..";
                    parentItem["type"] = "parent";
                    parentItem["path"] = parent;
                    parentItem["size"] = 0;
                    parentItem["lastModified"] = "";
                    items.Add(parentItem);
                }

                // 子文件夹
                foreach (string dir in Directory.GetDirectories(fullPath))
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dir);
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["name"] = dirInfo.Name;
                    item["type"] = "folder";
                    item["path"] = dir;
                    item["size"] = 0;
                    item["lastModified"] = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                    items.Add(item);
                }

                // 文件
                foreach (string file in Directory.GetFiles(fullPath))
                {
                    FileInfo fileInfo = new FileInfo(file);
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["name"] = fileInfo.Name;
                    item["type"] = "file";
                    item["path"] = file;
                    item["size"] = fileInfo.Length;
                    item["lastModified"] = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                    item["category"] = GetFileCategory(fileInfo.Name);
                    items.Add(item);
                }

                // 构建 JSON（使用 TryGetValue 避免键缺失）
                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"currentPath\":\"" + EscapeJsonString(targetPath) + "\",\"items\":[");
                for (int i = 0; i < items.Count; i++)
                {
                    Dictionary<string, object> item = items[i];
                    jsonBuilder.Append("{");
                    jsonBuilder.Append("\"name\":\"" + EscapeJsonString(item["name"].ToString()) + "\",");
                    jsonBuilder.Append("\"type\":\"" + item["type"].ToString() + "\",");
                    jsonBuilder.Append("\"path\":\"" + EscapeJsonString(item["path"].ToString()) + "\",");
                    // 安全获取 size
                    long sizeVal = 0;
                    if (item.ContainsKey("size"))
                    {
                        sizeVal = Convert.ToInt64(item["size"]);
                    }
                    jsonBuilder.Append("\"size\":" + sizeVal + ",");
                    jsonBuilder.Append("\"lastModified\":\"" + EscapeJsonString(item["lastModified"].ToString()) + "\"");
                    if (item.ContainsKey("category"))
                    {
                        jsonBuilder.Append(",\"category\":\"" + EscapeJsonString(item["category"].ToString()) + "\"");
                    }
                    jsonBuilder.Append("}");
                    if (i < items.Count - 1) jsonBuilder.Append(",");
                }
                jsonBuilder.Append("]}");
                string json = jsonBuilder.ToString();
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                res.ContentType = "application/json; charset=utf-8";
                res.StatusCode = 200;
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);

            }
            catch (Exception ex)
            {
                AddLog($"BrowseAll 异常: {ex}");
                SendJsonError(res, "浏览失败: " + ex.Message, 500);
            }
        }

        /// <summary>下载任意文件（管理员）</summary>
        private void DownloadAll(HttpListenerResponse res, string filePath)
        {
            try
            {
                filePath = Uri.UnescapeDataString(filePath);
                if (!File.Exists(filePath))
                {
                    Send404(res);
                    return;
                }
                // 记录下载
                FileInfo dlFi = new FileInfo(filePath);
                RecordTransfer(dlFi.Name, "download", dlFi.Length, _lastClientIP, filePath);
                // 直接调用现有的 SendSingleFile 方法（它接受绝对路径）
                SendSingleFile(res, filePath);
            }
            catch (Exception ex)
            {
                AddLog($"DownloadAll 异常: {ex}");
                Send500(res, ex.Message);
            }
        }

        /// <summary>列出压缩包内容（支持 zip/rar/7z/tar/gz 等，使用 SharpCompress）</summary>
        private void HandleArchiveList(HttpListenerRequest req, HttpListenerResponse res)
        {
            try
            {
                string rawPath = req.QueryString["path"] ?? "";
                string isAdminStr = req.QueryString["isAdmin"] ?? "false";
                bool isAdmin = isAdminStr == "true";

                if (string.IsNullOrEmpty(rawPath))
                {
                    AddLog("[压缩包] 请求缺少 path 参数");
                    SendJsonError(res, "缺少路径参数", 400);
                    return;
                }

                // 管理员浏览磁盘时需登录；普通共享文件无需登录
                if (isAdmin && !IsAuthenticated(req))
                {
                    AddLog("[压缩包] 未登录访问磁盘压缩包");
                    SendJsonError(res, "未登录或登录已过期", 401);
                    return;
                }

                // 路径安全检查：防止路径穿越
                if (rawPath.Contains(".."))
                {
                    AddLog($"[压缩包] 拒绝非法路径: {rawPath}");
                    SendJsonError(res, "非法路径", 403);
                    return;
                }

                // 路径解析：管理员用绝对路径，普通共享文件拼接 SharedFolderPath 根目录
                string archivePath = isAdmin
                    ? Uri.UnescapeDataString(rawPath)
                    : Path.Combine(SharedFolderPath, Uri.UnescapeDataString(rawPath).Replace("/", "\\").TrimStart('\\'));
                archivePath = Path.GetFullPath(archivePath);

                AddLog($"[压缩包] 请求列出: {archivePath} (isAdmin={isAdmin})");

                if (!File.Exists(archivePath))
                {
                    AddLog($"[压缩包] 文件不存在: {archivePath}");
                    SendJsonError(res, "文件不存在", 404);
                    return;
                }

                // 使用 SharpCompress 列出压缩包条目
                List<Dictionary<string, object>> entries = new List<Dictionary<string, object>>();
                using (SharpCompress.Archives.IArchive archive = SharpCompress.Archives.ArchiveFactory.Open(archivePath))
                {
                    foreach (SharpCompress.Archives.IArchiveEntry entry in archive.Entries)
                    {
                        bool isDir = entry.IsDirectory;
                        Dictionary<string, object> item = new Dictionary<string, object>();
                        item["name"] = isDir ? entry.Key.TrimEnd('/', '\\') : entry.Key;
                        item["isDir"] = isDir;
                        item["size"] = isDir ? 0L : entry.Size;
                        entries.Add(item);
                    }
                }

                // 构建 JSON
                StringBuilder sb = new StringBuilder();
                sb.Append("{\"entries\":[");
                for (int i = 0; i < entries.Count; i++)
                {
                    Dictionary<string, object> e = entries[i];
                    sb.Append("{");
                    sb.Append("\"name\":\"" + EscapeJsonString(e["name"].ToString()) + "\",");
                    sb.Append("\"isDir\":" + (Convert.ToBoolean(e["isDir"]) ? "true" : "false") + ",");
                    sb.Append("\"size\":" + Convert.ToInt64(e["size"]).ToString());
                    sb.Append("}");
                    if (i < entries.Count - 1) sb.Append(",");
                }
                sb.Append("]}");
                AddLog($"[压缩包] 解析成功: {archivePath}，共 {entries.Count} 个条目");
                SendJsonResponse(res, sb.ToString(), 200);
            }
            catch (Exception ex)
            {
                AddLog($"[压缩包] HandleArchiveList 异常: {ex}");
                SendJsonError(res, "解析压缩包失败: " + ex.Message, 500);
            }
        }

        /// <summary>
        /// 浏览文件夹内容（返回 JSON）
        /// </summary>
        private void BrowseFolder(HttpListenerResponse res, string folderPath)
        {
            try
            {
                // 1. URL 解码（前端 encodeURIComponent 编码）
                folderPath = Uri.UnescapeDataString(folderPath);
                Console.WriteLine($"[BrowseFolder] 原始路径: '{folderPath}'");

                // 2. 清理路径：去除首尾空格、多余斜杠，统一为正斜杠
                folderPath = folderPath.Trim();
                folderPath = folderPath.Replace("\\", "/").Replace("//", "/");
                if (folderPath == "/") folderPath = "";
                string originalPath = folderPath;

                // 3. 安全验证：禁止路径遍历
                if (folderPath.Contains("..") || folderPath.Contains("::"))
                {
                    SendTextError(res, "禁止访问：非法路径", 403);
                    return;
                }

                // 4. 构建完整物理路径（将正斜杠转为 Windows 反斜杠）
                string physicalSubPath = folderPath.Replace("/", "\\");
                string fullPath = Path.Combine(SharedFolderPath, physicalSubPath);
                fullPath = Path.GetFullPath(fullPath);
                Console.WriteLine($"[BrowseFolder] 物理路径: '{fullPath}'");

                // 5. 确保路径在共享文件夹内
                string sharedFullPath = Path.GetFullPath(SharedFolderPath);
                if (!fullPath.StartsWith(sharedFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    SendTextError(res, "禁止访问：路径超出共享文件夹范围", 403);
                    return;
                }

                // 6. 如果文件夹不存在，返回空列表（状态200，JSON格式）
                if (!Directory.Exists(fullPath))
                {
                    Console.WriteLine($"[BrowseFolder] 文件夹不存在: {fullPath}");
                    string emptyJson = "{\"currentPath\":\"" + EscapeJsonString(originalPath) + "\",\"items\":[]}";
                    byte[] buffer = Encoding.UTF8.GetBytes(emptyJson);
                    res.ContentType = "application/json; charset=utf-8";
                    res.StatusCode = 200;
                    res.ContentLength64 = buffer.Length;
                    res.OutputStream.Write(buffer, 0, buffer.Length);
                    return;
                }

                // 7. 获取文件夹内容（使用 Dictionary 代替匿名类型）
                List<Dictionary<string, object>> folderItems = new List<Dictionary<string, object>>();

                // 添加返回上级目录
                if (!string.IsNullOrEmpty(originalPath))
                {
                    string parentPath = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        parentPath = parentPath.Replace("\\", "/");
                        Dictionary<string, object> item = new Dictionary<string, object>();
                        item["name"] = "..";
                        item["type"] = "parent";
                        item["category"] = "返回上级";
                        item["path"] = parentPath;
                        item["size"] = 0;
                        item["lastModified"] = "";
                        folderItems.Add(item);
                    }
                }

                // 获取子文件夹
                foreach (string subDir in Directory.GetDirectories(fullPath))
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(subDir);
                    string dirName = Path.GetFileName(subDir);
                    string relativePath = string.IsNullOrEmpty(originalPath) ? dirName : Path.Combine(originalPath, dirName);
                    relativePath = relativePath.Replace("\\", "/");

                    // 统计子文件夹内的文件数量（可选，不统计也可设为0）
                    int fileCount = Directory.GetFiles(subDir, "*", SearchOption.AllDirectories).Length;
                    int subDirCount = Directory.GetDirectories(subDir, "*", SearchOption.AllDirectories).Length;

                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["name"] = dirName;
                    item["size"] = 0;
                    item["lastModified"] = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                    item["type"] = "folder";
                    item["category"] = "文件夹";
                    item["path"] = relativePath;
                    item["fileCount"] = fileCount;
                    item["folderCount"] = subDirCount;
                    item["totalItems"] = fileCount + subDirCount;
                    folderItems.Add(item);
                }

                // 获取文件
                foreach (string file in Directory.GetFiles(fullPath))
                {
                    FileInfo fileInfo = new FileInfo(file);
                    string fileName = Path.GetFileName(file);
                    string relativePath = string.IsNullOrEmpty(originalPath) ? fileName : Path.Combine(originalPath, fileName);
                    relativePath = relativePath.Replace("\\", "/");

                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["name"] = fileName;
                    item["size"] = fileInfo.Length;
                    item["lastModified"] = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                    item["type"] = "file";
                    item["category"] = GetFileCategory(fileName);
                    item["path"] = relativePath;
                    folderItems.Add(item);
                }

                // 8. 构建 JSON 响应
                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{");
                jsonBuilder.Append("\"currentPath\":\"" + EscapeJsonString(originalPath) + "\",");
                jsonBuilder.Append("\"items\":[");

                for (int i = 0; i < folderItems.Count; i++)
                {
                    Dictionary<string, object> item = folderItems[i];
                    jsonBuilder.Append("{");
                    jsonBuilder.Append("\"name\":\"" + EscapeJsonString(item["name"].ToString()) + "\",");
                    jsonBuilder.Append("\"size\":" + item["size"].ToString() + ",");
                    jsonBuilder.Append("\"lastModified\":\"" + EscapeJsonString(item["lastModified"].ToString()) + "\",");
                    jsonBuilder.Append("\"type\":\"" + item["type"].ToString() + "\",");
                    jsonBuilder.Append("\"category\":\"" + EscapeJsonString(item["category"].ToString()) + "\",");
                    jsonBuilder.Append("\"path\":\"" + EscapeJsonString(item["path"].ToString()) + "\"");
                    // 如果是文件夹，添加额外字段
                    if (item["type"].ToString() == "folder")
                    {
                        jsonBuilder.Append(",\"fileCount\":" + item["fileCount"].ToString() + ",");
                        jsonBuilder.Append("\"folderCount\":" + item["folderCount"].ToString() + ",");
                        jsonBuilder.Append("\"totalItems\":" + item["totalItems"].ToString());
                    }
                    jsonBuilder.Append("}");
                    if (i < folderItems.Count - 1) jsonBuilder.Append(",");
                }
                jsonBuilder.Append("]}");

                string json = jsonBuilder.ToString();
                byte[] buffer1 = Encoding.UTF8.GetBytes(json);

                res.ContentType = "application/json; charset=utf-8";
                res.StatusCode = 200;
                res.ContentLength64 = buffer1.Length;
                res.OutputStream.Write(buffer1, 0, buffer1.Length);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"[BrowseFolder ERROR] {ex}");
                SendTextError(res, "服务器错误: " + ex.Message, 500);
            }
        }
        private void SendTextError(HttpListenerResponse res, string message, int statusCode)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            res.ContentType = "text/plain; charset=utf-8";
            res.StatusCode = statusCode;
            res.ContentLength64 = buffer.Length;
            res.OutputStream.Write(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 发送文件列表（JSON格式）- 支持文件和文件夹
        /// </summary>
        private void SendFileList(HttpListenerResponse res)
        {
            try
            {
                // 确保共享文件夹存在
                if (!Directory.Exists(SharedFolderPath))
                {
                    Directory.CreateDirectory(SharedFolderPath);
                }

                // 获取Shared文件夹中的所有文件和文件夹
                List<FileListItem> allItems = new List<FileListItem>();

                // 1. 获取所有文件
                string[] files = Directory.GetFiles(SharedFolderPath);
                foreach (string filePath in files)
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    allItems.Add(new FileListItem
                    {
                        name = Path.GetFileName(filePath),
                        size = fileInfo.Length,
                        lastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        type = "file",
                        category = GetFileCategory(Path.GetFileName(filePath))
                    });
                }

                // 2. 获取所有文件夹
                string[] directories = Directory.GetDirectories(SharedFolderPath);
                foreach (string dirPath in directories)
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dirPath);
                    allItems.Add(new FileListItem
                    {
                        name = Path.GetFileName(dirPath) + "/",  // 文件夹名称后加斜杠表示是文件夹
                        size = 0,
                        lastModified = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        type = "folder",
                        category = "文件夹"
                    });
                }

                // 按名称排序
                allItems = allItems.OrderBy(x => x.name).ToList();

                // 转换为JSON - 修复转义问题
                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("[");
                for (int i = 0; i < allItems.Count; i++)
                {
                    FileListItem item = allItems[i];
                    jsonBuilder.Append("{");

                    // 正确转义文件名中的所有特殊字符
                    string escapedName = EscapeJsonString(item.name);
                    jsonBuilder.Append($"\"name\":\"{escapedName}\",");
                    jsonBuilder.Append($"\"size\":{item.size},");
                    jsonBuilder.Append($"\"lastModified\":\"{item.lastModified}\",");
                    jsonBuilder.Append($"\"type\":\"{item.type}\",");
                    jsonBuilder.Append($"\"category\":\"{item.category}\"");

                    jsonBuilder.Append("}");
                    if (i < allItems.Count - 1)
                    {
                        jsonBuilder.Append(",");
                    }
                }
                jsonBuilder.Append("]");

                string json = jsonBuilder.ToString();
                byte[] buffer = Encoding.UTF8.GetBytes(json);

                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);

            }
            catch (Exception ex)
            {
                Send500(res, "获取文件列表失败: " + ex.Message);
            }
        }

        /// <summary>文件列表项（SendFileList 内部使用）</summary>
        private class FileListItem
        {
            public string name;
            public long size;
            public string lastModified;
            public string type;
            public string category;
        }

        /// <summary>
        /// 根据文件名获取文件分类
        /// </summary>
        private string GetFileCategory(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLower();

            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".gif":
                case ".bmp":
                case ".webp":
                case ".ico":
                case ".svg":
                    return "图片";
                case ".mp4":
                case ".avi":
                case ".mov":
                case ".wmv":
                case ".flv":
                case ".mkv":
                case ".mpg":
                case ".mpeg":
                    return "视频";
                case ".mp3":
                case ".wav":
                case ".wma":
                case ".aac":
                case ".flac":
                case ".ogg":
                    return "音频";
                case ".pdf":
                case ".doc":
                case ".docx":
                case ".xls":
                case ".xlsx":
                case ".ppt":
                case ".pptx":
                case ".txt":
                case ".rtf":
                    return "文档";
                case ".zip":
                case ".rar":
                case ".7z":
                case ".tar":
                case ".gz":
                case ".bz2":
                case ".iso":
                    return "压缩包";
                case ".exe":
                case ".msi":
                case ".bat":
                case ".cmd":
                case ".sh":
                    return "程序";
                default:
                    return "其他";
            }
        }

        /// <summary>
        /// 转义 JSON 字符串中的特殊字符
        /// </summary>
        private string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            StringBuilder sb = new StringBuilder();
            foreach (char c in input)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '/':
                        sb.Append("\\/");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        // 检查是否为控制字符（ASCII < 32）
                        if ((int)c < 32)
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 发送文件（单个文件或文件夹ZIP）- 优化版
        /// </summary>
        private void SendFile(HttpListenerResponse res, string fileName)
        {
            try
            {
                // 1. 调试日志 - 记录原始输入
                Console.WriteLine($"[DEBUG] SendFile 开始处理: 原始文件名 = '{fileName}'");

                // 2. 双重解码保护（处理可能的双重编码情况）
                string decodedFileName = fileName;
                try
                {
                    // 先尝试解码一次
                    decodedFileName = Uri.UnescapeDataString(fileName);
                    Console.WriteLine($"[DEBUG] 第一次解码后: '{decodedFileName}'");

                    // 如果解码后仍然包含编码字符，再解码一次
                    if (decodedFileName.Contains("%"))
                    {
                        decodedFileName = Uri.UnescapeDataString(decodedFileName);
                        Console.WriteLine($"[DEBUG] 第二次解码后: '{decodedFileName}'");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DEBUG] 解码异常: {ex.Message}");
                    // 解码失败时使用原始文件名
                }

                // 3. 清理路径分隔符
                string cleanName = decodedFileName;
                cleanName = cleanName.Replace('/', Path.DirectorySeparatorChar);
                cleanName = cleanName.Replace('\\', Path.DirectorySeparatorChar);

                // 4. 移除路径开头的斜杠（如果有）
                if (cleanName.StartsWith(Path.DirectorySeparatorChar))
                {
                    cleanName = cleanName.Substring(1);
                }

                // 5. 安全验证：禁止路径遍历攻击
                if (cleanName.Contains("..") || cleanName.Contains("::"))
                {
                    Console.WriteLine($"[SECURITY] 路径遍历攻击检测: {cleanName}");
                    Send403(res, "禁止访问：非法路径");
                    return;
                }

                // 6. 构建完整路径
                string filePath = Path.Combine(SharedFolderPath, cleanName);
                Console.WriteLine($"[DEBUG] 组合路径: SharedFolderPath='{SharedFolderPath}', cleanName='{cleanName}'");
                Console.WriteLine($"[DEBUG] 完整路径: '{filePath}'");

                // 7. 规范化路径（解决相对路径问题）
                try
                {
                    filePath = Path.GetFullPath(filePath);
                    Console.WriteLine($"[DEBUG] 规范化后路径: '{filePath}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 路径规范化失败: {ex.Message}");
                    Send500(res, "路径解析失败");
                    return;
                }

                // 8. 确保路径在共享文件夹内（安全性检查）
                if (!filePath.StartsWith(Path.GetFullPath(SharedFolderPath), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[SECURITY] 路径越界检测: filePath='{filePath}', SharedFolderPath='{SharedFolderPath}'");
                    Send403(res, "禁止访问：路径超出共享文件夹范围");
                    return;
                }

                // 9. 检查文件/文件夹是否存在
                Console.WriteLine($"[DEBUG] 检查文件是否存在: {File.Exists(filePath)}");
                Console.WriteLine($"[DEBUG] 检查文件夹是否存在: {Directory.Exists(filePath)}");

                if (File.Exists(filePath))
                {
                    // 单个文件下载
                    Console.WriteLine($"[INFO] 开始下载文件: {filePath}");
                    // 记录最近浏览（下载/预览均计入）
                    FileInfo fi = new FileInfo(filePath);
                    RecordRecentView(filePath, fi.Name, false, fi.Length, GetFileCategory(fi.Name));
                    RecordTransfer(fi.Name, "download", fi.Length, _lastClientIP, filePath);
                    SendSingleFile(res, filePath);
                }
                else if (Directory.Exists(filePath))
                {
                    // 文件夹下载（打包为ZIP）
                    Console.WriteLine($"[INFO] 开始下载文件夹: {filePath}");
                    string folderName = Path.GetFileName(cleanName);
                    if (string.IsNullOrEmpty(folderName))
                    {
                        folderName = "folder";
                    }
                    RecordRecentView(filePath, folderName, true, 0, "文件夹");
                    SendFolderAsZip(res, filePath, folderName);
                }
                else
                {
                    // 文件不存在
                    Console.WriteLine($"[ERROR] 文件不存在: {filePath}");
                    Console.WriteLine($"[DEBUG] 原始输入: {fileName}");
                    Console.WriteLine($"[DEBUG] 解码后: {decodedFileName}");
                    Console.WriteLine($"[DEBUG] 清理后: {cleanName}");
                    Console.WriteLine($"[DEBUG] 共享文件夹: {SharedFolderPath}");

                    // 尝试列出共享文件夹内容以帮助调试
                    try
                    {
                        if (Directory.Exists(SharedFolderPath))
                        {
                            string[] files = Directory.GetFiles(SharedFolderPath);
                            string[] dirs = Directory.GetDirectories(SharedFolderPath);

                        }
                        else
                        {
                            Console.WriteLine($"[ERROR] 共享文件夹不存在: {SharedFolderPath}");
                        }
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"[DEBUG] 列出文件夹内容失败: {ex2.Message}");
                    }

                    Send404(res);
                }

            }
            catch (Exception ex)
            {
                // 记录详细错误信息以便调试
                Console.WriteLine($"[ERROR] SendFile 异常: {ex}");
                Console.WriteLine($"[DEBUG] 堆栈跟踪: {ex.StackTrace}");
                Send500(res, $"文件下载失败: {ex.Message}");
            }
        }

        // --- 传输任务多行进度条（下载/上传共用） ---
        private readonly Dictionary<string, TransferTask> _transferTasks = new Dictionary<string, TransferTask>(StringComparer.OrdinalIgnoreCase);
        private int _uploadTaskCounter = 0;   // 上传任务唯一 ID 计数器（多文件并发时区分）

        private class TransferTask
        {
            public string Id;
            public string Name;
            public string Direction;   // "下载" 或 "上传"
            public long Current;
            public long Total;
            public double Speed;
            public DateTime LastTime;
            public long LastBytes;
            public bool Done;
        }

        /// <summary>获取或创建传输任务</summary>
        private TransferTask GetTransferTask(string id, string name, string direction, long total)
        {
            lock (_transferLock)
            {
                TransferTask t = null;
                if (_transferTasks.TryGetValue(id, out t))
                {
                    if (t.Total <= 0) t.Total = total;
                    return t;
                }
                t = new TransferTask()
                {
                    Id = id,
                    Name = name,
                    Direction = direction,
                    Total = total,
                    LastTime = DateTime.MinValue,
                    LastBytes = 0,
                    Done = false
                };
                _transferTasks[id] = t;
                return t;
            }
        }

        /// <summary>移除已完成任务（延迟保留由调用方控制）</summary>
        private void RemoveTransferTask(string id)
        {
            lock (_transferLock)
            {
                _transferTasks.Remove(id);
            }
        }

        /// <summary>
        /// 渲染后端日志中的多行进度条（显示所有活跃传输任务）
        /// 在日志末尾维护一个进度区，每个活跃任务一行文字进度条。
        /// </summary>
        private void RenderTransferProgress()
        {
            if (txtLog == null) return;
            List<TransferTask> tasks;
            lock (_transferLock)
            {
                tasks = _transferTasks.Values.Where(t => !t.Done).ToList();
            }

            List<string> lines = new List<string>(txtLog.Lines);

            // 移除旧进度区（以 [进度] 标记开头的行）
            List<string> kept = new List<string>();
            foreach (string ln in lines)
            {
                if (!ln.Contains("[进度]"))
                {
                    kept.Add(ln);
                }
            }

            // 追加新的进度区
            if (tasks.Count > 0)
            {
                string timeStr = DateTime.Now.ToString("HH:mm:ss");
                foreach (TransferTask t in tasks)
                {
                    double percent = t.Total > 0 ? t.Current * 100.0 / t.Total : 0;
                    string bar = BuildTextBar(percent);
                    string speedStr = t.Speed > 0 ? FormatFileSize((long)t.Speed) + "/s" : "";
                    kept.Add($"[{timeStr}] [进度] {t.Direction} {t.Name} {bar} {percent:F1}% {FormatFileSize(t.Current)}/{FormatFileSize(t.Total)} {speedStr}");
                }
            }

            txtLog.Lines = kept.ToArray();
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        /// <summary>生成文字进度条</summary>
        private string BuildTextBar(double percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            int totalBlocks = 20;
            int filled = (int)Math.Round(percent / 100 * totalBlocks);
            StringBuilder bar = new StringBuilder();
            for (int i = 0; i < totalBlocks; i++)
            {
                if (i < filled) bar.Append("█"); else bar.Append("░");
            }
            return bar.ToString();
        }

        private void UpdateDownloadProgress(long current, long total, string fileName, bool final = false)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateDownloadProgress(current, total, fileName, final)));
                return;
            }
            if (total <= 0) return;

            string id = "dl:" + fileName;
            TransferTask task = GetTransferTask(id, fileName, "下载", total);
            DateTime now = DateTime.Now;

            // 速度计算（节流 0.5 秒）
            bool shouldRender = final;
            if (!final)
            {
                if (task.LastTime == DateTime.MinValue)
                {
                    task.LastTime = now;
                    task.LastBytes = current;
                    shouldRender = true;
                }
                else
                {
                    double elapsed = (now - task.LastTime).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        long delta = current - task.LastBytes;
                        if (delta > 0 && elapsed > 0)
                        {
                            task.Speed = delta / elapsed;
                        }
                        task.LastTime = now;
                        task.LastBytes = current;
                        shouldRender = true;
                    }
                }
            }

            task.Current = current;
            task.Total = total;

            if (final)
            {
                task.Done = true;
                task.Current = total;
                // 完成：移除任务并记录一条完成日志
                RemoveTransferTask(id);
                RenderTransferProgress();
                AddLog($"下载完成: {fileName} ({FormatFileSize(total)})");
                return;
            }

            if (shouldRender) RenderTransferProgress();
        }

        /// <summary>
        /// 发送单个文件（流式传输，节省内存）- 优化版
        /// </summary>
        private void SendSingleFile(HttpListenerResponse res, string filePath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                AddLog($"开始发送文件: {fileInfo.Name} (大小: {FormatFileSize(fileInfo.Length)})");

                // 设置响应头
                res.ContentType = GetMimeType(fileInfo.Name);

                // 文件名编码处理：确保中文文件名正确显示
                string encodedFileName = Uri.EscapeDataString(fileInfo.Name);
                res.AddHeader("Content-Disposition", $"attachment; filename*=UTF-8''{encodedFileName}");

                // 添加缓存控制头
                res.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                res.AddHeader("Pragma", "no-cache");
                res.AddHeader("Expires", "0");

                // 设置文件大小
                res.ContentLength64 = fileInfo.Length;

                // 使用 Using 确保文件流正确关闭
                using (FileStream fs = File.OpenRead(filePath))
                {
                    // 设置缓冲区大小（64KB）
                    int bufferSize = 64 * 1024;
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;
                    long totalSent = 0;
                    do
                    {
                        bytesRead = fs.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            res.OutputStream.Write(buffer, 0, bytesRead);

                            totalSent += bytesRead;
                            // 更新后端日志进度（每秒更新几次）
                            UpdateDownloadProgress(totalSent, fileInfo.Length, fileInfo.Name);
                        }
                    } while (bytesRead > 0);

                    // 确保输出流刷新
                    res.OutputStream.Flush();
                }

                UpdateDownloadProgress(fileInfo.Length, fileInfo.Length, fileInfo.Name, true);
                AddLog($"文件发送完成: {fileInfo.Name}");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] SendSingleFile 异常: {ex}");
                // 如果响应头已经发送，不能再修改状态码
                try
                {
                    if (!res.OutputStream.CanWrite)
                    {
                        Send500(res, $"文件传输失败: {ex.Message}");
                    }
                }
                catch
                {
                    // 忽略
                }
            }
        }

        /// <summary>
        /// 将文件夹打包为ZIP并发送 - 修复版
        /// </summary>
        private void SendFolderAsZip(HttpListenerResponse res, string folderPath, string folderName)
        {
            string tempZipPath = Path.GetTempFileName() + ".zip";

            try
            {
                // 1. 创建 ZIP 文件 (使用上面的修复版方法)
                CreateZipFromFolder(folderPath, tempZipPath);

                // 2. 验证文件是否生成成功
                if (!File.Exists(tempZipPath) || new FileInfo(tempZipPath).Length == 0)
                {
                    throw new Exception("ZIP文件生成失败或为空");
                }

                // 3. 获取文件大小
                FileInfo zipInfo = new FileInfo(tempZipPath);

                // 4. 设置响应头
                res.ContentType = "application/zip";
                // 注意：文件名最好进行 URL 编码，防止中文文件名乱码
                string encodedFileName = Uri.EscapeDataString(folderName + ".zip");
                res.AddHeader("Content-Disposition", $"attachment; filename*=UTF-8''{encodedFileName}");
                res.ContentLength64 = zipInfo.Length;
                string zipFileName = folderName + ".zip";

                // 5. 流式发送 ZIP 文件
                using (FileStream fs = File.OpenRead(tempZipPath))
                {
                    int bufferSize = 64 * 1024;
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;
                    long totalSent = 0;

                    do
                    {
                        bytesRead = fs.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            res.OutputStream.Write(buffer, 0, bytesRead);
                            totalSent += bytesRead;
                            UpdateDownloadProgress(totalSent, zipInfo.Length, zipFileName);
                        }
                    } while (bytesRead > 0);

                    res.OutputStream.Flush();
                }

                UpdateDownloadProgress(zipInfo.Length, zipInfo.Length, zipFileName, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SendFolderAsZip Error: " + ex.ToString());

            }
            finally
            {
                // 6. 清理临时文件
                try
                {
                    if (File.Exists(tempZipPath))
                    {
                        File.Delete(tempZipPath);
                    }
                }
                catch
                {
                    // 忽略删除错误
                }
            }
        }

        /// <summary>
        /// 递归创建ZIP文件 - 修复版
        /// </summary>
        private void CreateZipFromFolder(string sourceFolder, string zipFilePath)
        {
            // 如果文件已存在，先删除，避免追加模式导致的问题
            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }

            // 使用 ZipFile.CreateFromDirectory 是最简单且最可靠的方法
            // 它会自动处理递归和流关闭
            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(
                sourceFolder,
                zipFilePath,
                System.IO.Compression.CompressionLevel.Optimal,
                false // includeBaseDirectory: False 表示不包含顶层文件夹名，只包含内容
            );
            }
            catch (Exception ex)
            {
                throw new Exception("创建ZIP失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 递归添加文件夹内容到ZIP
        /// </summary>
        private void AddFolderToZip(string sourceFolder, string relativePath, ZipArchive archive)
        {
            // 添加当前目录下的文件
            foreach (string filePath in Directory.GetFiles(sourceFolder))
            {
                string fileName = Path.GetFileName(filePath);
                string entryName = Path.Combine(relativePath, fileName).Replace("\\", "/"); // ZIP内部使用正斜杠

                // CreateEntryFromFile 是 .NET 4.5+ 的便捷方法
                archive.CreateEntryFromFile(filePath, entryName);
            }

            // 递归添加子目录
            foreach (string dirPath in Directory.GetDirectories(sourceFolder))
            {
                string dirName = Path.GetFileName(dirPath);
                string newRelativePath = Path.Combine(relativePath, dirName);
                AddFolderToZip(dirPath, newRelativePath, archive);
            }
        }

        /// <summary>
        /// 发送单个文件信息（JSON格式）
        /// </summary>
        private void SendFileInfo(HttpListenerResponse res, string fileName)
        {
            try
            {
                string filePath = Path.Combine(SharedFolderPath, fileName);

                if (File.Exists(filePath))
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    // 使用转义函数处理文件名
                    string escapedName = EscapeJsonString(fileName);
                    string json = "{\"name\":\"" + escapedName + "\",\"size\":" + fileInfo.Length + ",\"lastModified\":\"" + fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "\"}";
                    byte[] buffer = Encoding.UTF8.GetBytes(json);

                    res.ContentType = "application/json; charset=utf-8";
                    res.ContentLength64 = buffer.Length;
                    res.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    Send404(res);
                }
            }
            catch (Exception ex)
            {
                Send500(res, "获取文件信息失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 根据文件扩展名获取MIME类型
        /// </summary>
        private string GetMimeType(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLower();
            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                case ".gif":
                    return "image/gif";
                case ".pdf":
                    return "application/pdf";
                case ".txt":
                    return "text/plain";
                case ".zip":
                    return "application/zip";
                case ".rar":
                    return "application/x-rar-compressed";
                case ".doc":
                case ".docx":
                    return "application/msword";
                case ".xls":
                case ".xlsx":
                    return "application/vnd.ms-excel";
                case ".mp3":
                    return "audio/mpeg";
                case ".mp4":
                    return "video/mp4";
                default:
                    return "application/octet-stream";
            }
        }

        /// <summary>发送403禁止访问</summary>
        private void Send403(HttpListenerResponse res, string message)
        {
            try
            {
                byte[] msg = Encoding.UTF8.GetBytes($"403 Forbidden: {message}");
                res.StatusCode = 403;
                res.ContentLength64 = msg.Length;
                res.OutputStream.Write(msg, 0, msg.Length);
            }
            catch
            {
                // 客户端已断开：静默处理
            }
        }

        private void SendHtml(HttpListenerResponse res, string clientIP = "", string userAgent = "")
        {
            try
            {
                // 从独立网页文件读取 HTML（位于程序目录 web/index.html）
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "index.html");
                if (!File.Exists(htmlPath))
                {
                    AddLog("网页文件缺失: " + htmlPath);
                    Send500(res, "网页文件缺失: web/index.html");
                    return;
                }

                string html = File.ReadAllText(htmlPath, Encoding.UTF8);
                byte[] buffer = Encoding.UTF8.GetBytes(html);

                res.ContentType = "text/html; charset=utf-8";
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);

                // 提示外部设备连接成功
                this.Invoke(new Action(() =>
                {
                    AutoWindow($"外部设备IP:{clientIP} UA:{userAgent}连接成功!!!", 2, "外部设备连接成功提示");
                }));
            }
            catch (Exception ex)
            {
                AddLog($"SendHtml 读取网页文件失败: {ex.Message}");
                Send500(res, ex.Message);
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dbytes = bytes;
            while (dbytes >= 1024 && i < sizes.Length - 1)
            {
                dbytes /= 1024;
                i += 1;
            }
            return $"{dbytes:0.##} {sizes[i]}";
        }

        // --- 上传进度跟踪（使用多任务进度条） ---
        private void UpdateUploadProgress(string taskId, long current, long total, bool final = false)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateUploadProgress(taskId, current, total, final)));
                return;
            }
            if (total <= 0) return;

            // 上传任务使用唯一 id（taskId），支持多文件并发上传互不覆盖
            // 任务名带编号，便于区分多个并发上传
            int taskNum = 0;
            if (taskId.StartsWith("ul:")) int.TryParse(taskId.Substring(3), out taskNum);
            TransferTask task = GetTransferTask(taskId, $"前端上传#{taskNum}", "上传", total);
            DateTime now = DateTime.Now;

            bool shouldRender = final;
            if (!final)
            {
                if (task.LastTime == DateTime.MinValue)
                {
                    task.LastTime = now;
                    task.LastBytes = current;
                    shouldRender = true;
                }
                else
                {
                    double elapsed = (now - task.LastTime).TotalSeconds;
                    if (elapsed >= 0.5)
                    {
                        long delta = current - task.LastBytes;
                        if (delta > 0 && elapsed > 0)
                        {
                            task.Speed = delta / elapsed;
                        }
                        task.LastTime = now;
                        task.LastBytes = current;
                        shouldRender = true;
                    }
                }
            }

            task.Current = current;
            task.Total = total;

            if (final)
            {
                task.Done = true;
                task.Current = total;
                RemoveTransferTask(taskId);
                RenderTransferProgress();
                AddLog($"前端上传完成: {FormatFileSize(total)}");
                return;
            }

            if (shouldRender) RenderTransferProgress();
        }

        private void HandleUpload(HttpListenerRequest req, HttpListenerResponse res,
                                 string clientIP = "", string userAgent = "")
        {
            // 为本次上传请求分配唯一任务 ID（多文件并发上传时互不覆盖）
            string taskId = "ul:" + System.Threading.Interlocked.Increment(ref _uploadTaskCounter).ToString();
            try
            {
                // 检查是否为 multipart
                if (!req.ContentType.Contains("multipart/form-data"))
                {
                    Send400(res, "无效的请求类型");
                    return;
                }

                // 提取 boundary
                string boundary = "";
                string[] parts = req.ContentType.Split(';');
                foreach (string part in parts)
                {
                    if (part.Trim().StartsWith("boundary="))
                    {
                        boundary = part.Trim().Substring(9).Trim('"');
                        break;
                    }
                }
                if (string.IsNullOrEmpty(boundary))
                {
                    Send400(res, "无法解析 boundary");
                    return;
                }

                // 将上传数据保存到临时文件（避免内存溢出）
                string tempFile = Path.GetTempFileName();
                try
                {
                    long totalBytes = req.ContentLength64;
                    long uploadedBytes = 0;

                    using (Stream inputStream = req.InputStream)
                    {
                        using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.SequentialScan))
                        {
                            byte[] buffer = new byte[81921];
                            int read;
                            while (true)
                            {
                                read = inputStream.Read(buffer, 0, buffer.Length);
                                if (read == 0) break;
                                fs.Write(buffer, 0, read);
                                uploadedBytes += read;
                                if (totalBytes > 0)
                                {
                                    UpdateUploadProgress(taskId, uploadedBytes, totalBytes);
                                }
                            }
                        }
                    }
                    if (totalBytes > 0)
                    {
                        UpdateUploadProgress(taskId, uploadedBytes, totalBytes, true);
                    }

                    // 使用文件流解析 multipart
                    using (FileStream fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan))
                    {
                        byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
                        byte[] crlf = Encoding.UTF8.GetBytes("\r\n");
                        byte[] crlfcrlf = Encoding.UTF8.GetBytes("\r\n\r\n");
                        int fileCount = 0;
                        long pos = 0;
                        long fileLen = fs.Length;

                        // 存储前端传来的目标路径（子文件夹）
                        string uploadTargetPath = "";

                        // 查找第一个 boundary
                        pos = FindInStream(fs, boundaryBytes, 0);
                        if (pos < 0)
                        {
                            Send400(res, "未找到 boundary");
                            return;
                        }
                        pos += boundaryBytes.Length;
                        if (PeekMatch(fs, pos, crlf)) pos += crlf.Length;

                        while (pos < fileLen)
                        {
                            long nextBoundaryPos = FindInStream(fs, boundaryBytes, pos);
                            if (nextBoundaryPos < 0) break;

                            long headerStart = pos;
                            long headerEnd = FindInStream(fs, crlfcrlf, headerStart);
                            if (headerEnd < 0 || headerEnd >= nextBoundaryPos) break;

                            int headerLength = (int)(headerEnd - headerStart);
                            fs.Seek(headerStart, SeekOrigin.Begin);
                            byte[] headerBytes = new byte[headerLength];
                            fs.ReadExactly(headerBytes, 0, headerLength);
                            string headerStr = Encoding.UTF8.GetString(headerBytes);

                            // 检查是否是 targetPath 字段（普通表单字段）
                            if (headerStr.Contains("name=\"targetPath\""))
                            {
                                // 提取数据内容作为目标路径
                                long dataStart = headerEnd + 4;
                                long dataEnd = nextBoundaryPos - 2;
                                if (dataEnd > dataStart)
                                {
                                    long dataLength = dataEnd - dataStart;
                                    fs.Seek(dataStart, SeekOrigin.Begin);
                                    byte[] targetBuffer = new byte[dataLength];
                                    fs.ReadExactly(targetBuffer, 0, targetBuffer.Length);
                                    uploadTargetPath = Encoding.UTF8.GetString(targetBuffer).Trim();
                                    AddLog($"接收到上传目标路径: {uploadTargetPath}");
                                }
                                // 跳过该部分
                                pos = nextBoundaryPos + boundaryBytes.Length;
                                if (PeekMatch(fs, pos, crlf)) pos += crlf.Length;
                                continue;
                            }

                            // 解析文件部分
                            string fileName = ExtractFileName(headerStr);
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                long dataStart = headerEnd + 4;
                                long dataEnd = nextBoundaryPos - 2;
                                if (dataEnd > dataStart)
                                {
                                    long dataLength = dataEnd - dataStart;

                                    // 安全验证
                                    if (!fileName.Contains("..\\") && !fileName.Contains("../"))
                                    {
                                        // 构建保存路径：如果指定了子文件夹，则拼接到路径中
                                        string relativePath = fileName;
                                        if (!string.IsNullOrEmpty(uploadTargetPath))
                                        {
                                            // 将前端传来的正斜杠路径转换为 Windows 反斜杠
                                            string targetDir = uploadTargetPath.Replace("/", "\\");
                                            relativePath = Path.Combine(targetDir, fileName);
                                        }
                                        string savePath = Path.Combine(SharedFolderPath, relativePath);
                                        string saveDir = Path.GetDirectoryName(savePath);
                                        if (!Directory.Exists(saveDir))
                                        {
                                            Directory.CreateDirectory(saveDir);
                                        }
                                        // 重名文件直接覆盖（FileMode.Create），不再自动追加序号

                                        // 流式写入文件
                                        using (FileStream outFs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.SequentialScan))
                                        {
                                            fs.Seek(dataStart, SeekOrigin.Begin);
                                            byte[] buffer = new byte[81921];
                                            long remaining = dataLength;
                                            while (remaining > 0)
                                            {
                                                int toRead = (int)Math.Min(buffer.Length, remaining);
                                                int readBytes = fs.Read(buffer, 0, toRead);
                                                if (readBytes == 0) break;
                                                outFs.Write(buffer, 0, readBytes);
                                                remaining -= readBytes;
                                            }
                                        }
                                        fileCount += 1;
                                        AddLog($"上传文件: {relativePath} (大小: {dataLength} 字节)");
                                        RecordTransfer(fileName, "upload", dataLength, clientIP, savePath);
                                    }
                                }
                            }

                            // 移动到下一个 boundary 之后
                            pos = nextBoundaryPos + boundaryBytes.Length;
                            if (PeekMatch(fs, pos, crlf)) pos += crlf.Length;
                        }

                        // 返回成功
                        string responseMsg = $"成功上传 {fileCount} 个文件";
                        byte[] bufferOut = Encoding.UTF8.GetBytes(responseMsg);
                        res.StatusCode = 200;
                        res.ContentLength64 = bufferOut.Length;
                        res.OutputStream.Write(bufferOut, 0, bufferOut.Length);
                        AddLog($"IP:{clientIP} UA:{userAgent} 上传完成，共 {fileCount} 个文件");
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Upload Error: " + ex.ToString());
                Send500(res, "上传失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 在流中查找字节序列的位置
        /// </summary>
        private long FindInStream(Stream stream, byte[] pattern, long startPos)
        {
            const int BUFFER_SIZE = 81920;
            byte[] buffer = new byte[BUFFER_SIZE];
            int patternLen = pattern.Length;
            if (patternLen == 0) return -1;

            stream.Seek(startPos, SeekOrigin.Begin);
            long pos = startPos;
            while (pos <= stream.Length - patternLen)
            {
                int toRead = (int)Math.Min(BUFFER_SIZE, stream.Length - pos);
                stream.ReadExactly(buffer, 0, toRead);
                for (int i = 0; i <= toRead - patternLen; i++)
                {
                    bool match = true;
                    for (int j = 0; j < patternLen; j++)
                    {
                        if (buffer[i + j] != pattern[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return pos + i;
                }
                pos += toRead - patternLen + 1;
                stream.Seek(pos, SeekOrigin.Begin);
            }
            return -1;
        }

        /// <summary>
        /// 检查流指定位置是否匹配字节序列（不移动位置）
        /// </summary>
        private bool PeekMatch(Stream stream, long pos, byte[] pattern)
        {
            if (pos + pattern.Length > stream.Length) return false;
            long oldPos = stream.Position;
            stream.Seek(pos, SeekOrigin.Begin);
            byte[] buffer = new byte[pattern.Length];
            int readBytes = stream.Read(buffer, 0, buffer.Length);
            stream.Seek(oldPos, SeekOrigin.Begin);
            if (readBytes != pattern.Length) return false;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (buffer[i] != pattern[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// 从头部提取文件名
        /// </summary>
        private string ExtractFileName(string header)
        {
            string pattern = "filename=\"(.*?)\"";
            Match match = Regex.Match(header, pattern);
            return match.Success ? match.Groups[1].Value : "";
        }

        /// <summary>
        /// 上传的文件信息类
        /// </summary>
        private class UploadedFile
        {
            public string FileName = "";
            public byte[] Data = null;
        }

        /// <summary>
        /// ngrok 按钮点击事件 - 开启/关闭外网隧道
        /// </summary>
        private async void BtnNgrok_Click(object sender, EventArgs e)
        {
            btnNgrok.Enabled = false;

            try
            {
                if (isNgrokRunning)
                {
                    await StopNgrokAsync();
                }
                else
                {
                    await StartNgrokAsync();
                }
            }
            catch (Exception ex)
            {
                AddLog($"ngrok 操作失败: {ex.Message}");
                AutoWindow($"操作失败: {ex.Message}", 3, "错误");
            }
            finally
            {
                btnNgrok.Enabled = true;
            }
        }

        // ========== 外网自动监听时间段相关方法 ==========

        /// <summary>保存外网自动监听配置到文件</summary>
        private void SaveNgrokAutoConfig()
        {
            try
            {
                string cfgPath = GetConfigPath(NGROK_CONFIG_FILE);
                string startStr = txtNgrokStart.Value.ToString("HH:mm");
                string endStr = txtNgrokEnd.Value.ToString("HH:mm");
                string enabled = chkAutoNgrok.Checked ? "1" : "0";
                File.WriteAllText(cfgPath, $"{enabled}|{startStr}|{endStr}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddLog($"保存外网自动监听配置失败: {ex.Message}");
            }
        }

        /// <summary>从文件加载外网自动监听配置</summary>
        private void LoadNgrokAutoConfig()
        {
            try
            {
                // 加载期间禁止触发保存，避免用默认时间覆盖配置
                _loadingNgrokConfig = true;

                string cfgPath = GetConfigPath(NGROK_CONFIG_FILE);
                if (File.Exists(cfgPath))
                {
                    string[] parts = File.ReadAllText(cfgPath, Encoding.UTF8).Trim().Split('|');
                    if (parts.Length >= 3)
                    {
                        // 先设置时间，再设置勾选状态（避免 CheckedChanged 过早触发保存）
                        DateTime st;
                        DateTime ed;
                        if (DateTime.TryParse(parts[1], out st))
                        {
                            txtNgrokStart.Value = DateTime.Today.Add(st.TimeOfDay);
                        }
                        if (DateTime.TryParse(parts[2], out ed))
                        {
                            txtNgrokEnd.Value = DateTime.Today.Add(ed.TimeOfDay);
                        }
                        chkAutoNgrok.Checked = parts[0] == "1";
                    }
                }
                else
                {
                    // 默认值：不启用，时间段 09:00 - 18:00
                    txtNgrokStart.Value = DateTime.Today.AddHours(9);
                    txtNgrokEnd.Value = DateTime.Today.AddHours(18);
                    chkAutoNgrok.Checked = false;
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载外网自动监听配置失败: {ex.Message}");
            }
            finally
            {
                _loadingNgrokConfig = false;
            }
            UpdateNgrokAutoStatusLabel();
        }

        /// <summary>勾选框状态变化事件</summary>
        private void ChkAutoNgrok_CheckedChanged(object sender, EventArgs e)
        {
            if (txtNgrokStart == null || txtNgrokEnd == null) return;
            // 校验时间段长度不超过每日免费额度
            int minutes = GetNgrokTimeWindowMinutes();
            if (chkAutoNgrok.Checked && minutes > MAX_DAILY_NGROK_MINUTES)
            {
                AutoWindow($"时间段长度（{minutes} 分钟）超过每日免费额度（{MAX_DAILY_NGROK_MINUTES} 分钟），已自动限制。", 3, "额度提示");
            }
            if (!_loadingNgrokConfig)
            {
                SaveNgrokAutoConfig();
            }
            UpdateNgrokAutoStatusLabel();
        }

        /// <summary>时间选择器变化事件：保存配置</summary>
        private void NgrokTime_ValueChanged(object sender, EventArgs e)
        {
            if (_loadingNgrokConfig) return;
            if (txtNgrokStart == null || txtNgrokEnd == null) return;
            SaveNgrokAutoConfig();
            UpdateNgrokAutoStatusLabel();
        }

        /// <summary>计算时间段长度（分钟），支持跨天</summary>
        private int GetNgrokTimeWindowMinutes()
        {
            int startMins = (txtNgrokStart.Value.Hour * 60) + txtNgrokStart.Value.Minute;
            int endMins = (txtNgrokEnd.Value.Hour * 60) + txtNgrokEnd.Value.Minute;
            if (endMins > startMins)
            {
                return endMins - startMins;
            }
            else if (endMins < startMins)
            {
                // 跨天
                return 1440 - startMins + endMins;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>判断当前时间是否在设定的自动监听时间段内</summary>
        private bool IsInNgrokTimeWindow()
        {
            if (!chkAutoNgrok.Checked) return false;
            int nowMins = (DateTime.Now.Hour * 60) + DateTime.Now.Minute;
            int startMins = (txtNgrokStart.Value.Hour * 60) + txtNgrokStart.Value.Minute;
            int endMins = (txtNgrokEnd.Value.Hour * 60) + txtNgrokEnd.Value.Minute;

            if (startMins == endMins) return false;
            if (endMins > startMins)
            {
                return nowMins >= startMins && nowMins < endMins;
            }
            else
            {
                // 跨天
                return nowMins >= startMins || nowMins < endMins;
            }
        }

        /// <summary>启动外网自动监听调度定时器（每5秒检查一次）</summary>
        private void StartNgrokAutoScheduler()
        {
            if (ngrokAutoTimer != null) return;
            ngrokAutoTimer = new System.Timers.Timer(5000) { AutoReset = true };
            ngrokAutoTimer.Elapsed += NgrokAutoScheduler_Tick;
            ngrokAutoTimer.Start();

            // 窗体加载完成后立即执行一次检查，避免等待第一个定时周期
            this.BeginInvoke(new Action(() =>
            {
                NgrokAutoScheduler_Tick(null, null);
            }));
        }

        /// <summary>调度定时器触发：进入时间段自动启动 ngrok，离开则停止</summary>
        private void NgrokAutoScheduler_Tick(object sender, EventArgs e)
        {
            try
            {
                bool inWindow = IsInNgrokTimeWindow();

                if (inWindow)
                {
                    // 时间段内：如果未启动 ngrok 且未处于自动启动流程中，则自动启动
                    if (!isNgrokRunning && !autoNgrokActive)
                    {
                        autoNgrokActive = true;
                        RunOnUI(() =>
                        {
                            AddLog("已进入外网自动监听时间段，尝试自动开启外网...");
                            Task t = StartNgrokForAutoAsync();
                        });
                    }
                }
                else
                {
                    // 时间段外：如果 ngrok 是自动启动的，则停止
                    if (autoNgrokActive && isNgrokRunning)
                    {
                        autoNgrokActive = false;
                        RunOnUI(() =>
                        {
                            AddLog("已离开外网自动监听时间段，自动关闭外网...");
                            StopNgrok();
                        });
                    }
                    else if (autoNgrokActive)
                    {
                        autoNgrokActive = false;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略调度异常
            }
        }

        /// <summary>在 UI 线程安全地执行操作（避免 Invoke 在 UI 线程内死锁）</summary>
        private void RunOnUI(Action action)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
        }

        /// <summary>自动监听模式下启动 ngrok（不要求服务已运行，直接隧道到端口）</summary>
        private async Task StartNgrokForAutoAsync()
        {
            try
            {
                int port;
                if (!int.TryParse(txtPort.Text.Trim(), out port))
                {
                    port = DefaultPort;
                }

                string ngrokPath = GetNgrokExePath();
                if (!File.Exists(ngrokPath))
                {
                    AddLog("未找到 ngrok.exe，无法自动开启外网监听");
                    autoNgrokActive = false;
                    return;
                }

                // 直接启动 ngrok 进程（不强制要求服务已启动）
                await Task.Run(() => StartNgrokProcessDirect(ngrokPath, port));
            }
            catch (Exception ex)
            {
                AddLog($"自动开启外网失败: {ex.Message}");
                autoNgrokActive = false;
            }
        }

        /// <summary>直接启动 ngrok 进程（供自动监听调用，服务可未启动）</summary>
        private void StartNgrokProcessDirect(string ngrokPath, int port)
        {
            try
            {
                // 清理旧进程
                try
                {
                    Process killProcess = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = "taskkill",
                            Arguments = "/f /im ngrok.exe",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    killProcess.Start();
                    killProcess.WaitForExit(3000);
                    killProcess.Dispose();
                }
                catch
                {
                }

                if (ngrokProcess != null && !ngrokProcess.HasExited)
                {
                    ngrokProcess.Kill();
                    ngrokProcess.Dispose();
                    ngrokProcess = null;
                }

                string token = LoadNgrokAuthToken();
                if (string.IsNullOrEmpty(token))
                {
                    // 未配置令牌：提示并终止本次启动，绝不使用任何内置 / 硬编码令牌
                    this.Invoke(new Action(() =>
                    {
                        AddLog("未配置 ngrok 认证令牌，已取消启动外网隧道。请在弹窗中填写令牌后重试。");
                    }));
                    return;
                }

                // 配置认证
                Process cfg = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = ngrokPath,
                        Arguments = $"config add-authtoken {token}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                cfg.Start();
                cfg.WaitForExit(5000);
                cfg.Dispose();

                // 启动 ngrok
                ProcessStartInfo si = new ProcessStartInfo()
                {
                    FileName = ngrokPath,
                    Arguments = $"http {port} --log=stdout --log-level=debug --region=jp",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                ngrokProcess = new Process()
                {
                    StartInfo = si,
                    EnableRaisingEvents = true
                };

                ngrokProcess.OutputDataReceived -= NgrokOutputHandler;
                ngrokProcess.ErrorDataReceived -= NgrokErrorHandler;
                ngrokProcess.Exited -= NgrokExitedHandler;
                ngrokProcess.OutputDataReceived += NgrokOutputHandler;
                ngrokProcess.ErrorDataReceived += NgrokErrorHandler;
                ngrokProcess.Exited += NgrokExitedHandler;

                ngrokProcess.Start();
                ngrokProcess.BeginOutputReadLine();
                ngrokProcess.BeginErrorReadLine();

                isNgrokRunning = true;

                this.Invoke(new Action(() =>
                {
                    AddLog("✅ 外网自动监听已开启，等待外网访问自动启动服务...");
                    UpdateNgrokAutoStatusLabel();
                }));
            }
            catch (Exception ex)
            {
                AddLog($"自动启动 ngrok 失败: {ex.Message}");
                autoNgrokActive = false;
            }
        }

        /// <summary>复制外网地址按钮点击事件</summary>
        private void LblNgrokCopy_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(ngrokUrl)) return;
            try
            {
                Clipboard.SetText(ngrokUrl);
                AutoWindow("外网地址已复制到剪贴板", 1, "复制成功");
            }
            catch (Exception ex)
            {
                AddLog($"复制外网地址失败: {ex.Message}");
            }
        }

        /// <summary>更新时间段状态提示标签</summary>
        private void UpdateNgrokAutoStatusLabel()
        {
            if (lblNgrokStatus == null) return;
            if (chkAutoNgrok.Checked)
            {
                int mins = GetNgrokTimeWindowMinutes();
                lblNgrokStatus.Text = $"已启用 ({mins}分钟/日, 限额{MAX_DAILY_NGROK_MINUTES}分钟)";
                lblNgrokStatus.ForeColor = Color.FromArgb(0, 122, 255);
            }
            else
            {
                lblNgrokStatus.Text = "未启用";
                lblNgrokStatus.ForeColor = Color.Gray;
            }
        }

        /// <summary>
        /// 更新外网二维码框的显示/隐藏。
        /// 显示条件：ngrok 正在运行且已获取到外网地址
        /// （ngrok 运行说明：要么处于自动监听时间段内自动启动，要么用户手动开启外网）。
        /// </summary>
        private void UpdateWanQrVisibility()
        {
            if (pbQrCodeWan == null || lblWanHint == null) return;

            bool show = isNgrokRunning && !string.IsNullOrEmpty(ngrokUrl);
            pbQrCodeWan.Visible = show;
            lblWanHint.Visible = show;

            if (!show)
            {
                // 隐藏时清空外网二维码并复位提示
                if (pbQrCodeWan.Image != null)
                {
                    pbQrCodeWan.Image.Dispose();
                    pbQrCodeWan.Image = null;
                }
                lblWanHint.ForeColor = Color.Gray;
                lblWanHint.Text = "外网二维码（未开启）";
            }
        }

        private async Task StartNgrokAsync()
        {
            try
            {
                // 1. 检查服务是否已启动
                if (!isRunning)
                {
                    AutoWindow("请先启动本地服务（点击'启动服务'按钮）", 2, "提示");
                    return;
                }

                // 2. 获取端口
                int port;
                if (!int.TryParse(txtPort.Text.Trim(), out port))
                {
                    port = DefaultPort;
                }

                // 3. 检查 ngrok.exe 是否存在
                string ngrokPath = GetNgrokExePath();

                if (!File.Exists(ngrokPath))
                {
                    AutoWindow(
                        "未找到 ngrok.exe" + "\r\n\r\n" +
                        "请按以下步骤操作：" + "\r\n" +
                        "1. 访问 https://bin.ngrok.com/c/bNyj1mQVY4c/ngrok-v3-stable-windows-amd64.zip" + "\r\n" +
                        "2. 下载 Windows 64-bit 版本" + "\r\n" +
                        "3. 解压后将 ngrok.exe 放入程序目录下的 config 文件夹" + "\r\n" +
                        "4. 重新点击'开启外网'",
                        5,
                        "缺少 ngrok"
                    );
                    return;
                }

                // 4. 启动 ngrok 进程
                await StartNgrokProcessAsync(ngrokPath, port);

            }
            catch (Exception ex)
            {
                AddLog($"启动 ngrok 失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 启动 ngrok 进程（异步）- 修复版
        /// </summary>
        private Task StartNgrokProcessAsync(string ngrokPath, int port)
        {
            return Task.Run(() =>
            {
                try
                {
                    // ========== 先强制结束所有 ngrok 进程 ==========
                    try
                    {
                        Process killProcess = new Process()
                        {
                            StartInfo = new ProcessStartInfo()
                            {
                                FileName = "taskkill",
                                Arguments = "/f /im ngrok.exe",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true
                            }
                        };
                        killProcess.Start();
                        killProcess.WaitForExit(3000);
                        killProcess.Dispose();
                        AddLog("已清理旧的 ngrok 进程");
                    }
                    catch (Exception)
                    {
                        // 忽略
                    }

                    // 如果已有进程，先停止
                    if (ngrokProcess != null && !ngrokProcess.HasExited)
                    {
                        ngrokProcess.Kill();
                        ngrokProcess.Dispose();
                        ngrokProcess = null;
                    }

                    string AUTH_TOKEN = LoadNgrokAuthToken();
                    if (string.IsNullOrEmpty(AUTH_TOKEN))
                    {
                        // 未配置令牌：提示并终止本次启动，绝不使用任何内置 / 硬编码令牌
                        this.Invoke(new Action(() =>
                        {
                            AddLog("未配置 ngrok 认证令牌，已取消开启外网。请在弹窗中填写令牌后重试。");
                        }));
                        return;
                    }
                    this.Invoke(new Action(() =>
                    {
                        AddLog("正在配置 ngrok 认证...");
                        btnNgrok.Text = "⏳ 认证中...";
                    }));

                    // 配置认证
                    Process configProcess = new Process()
                    {
                        StartInfo = new ProcessStartInfo()
                        {
                            FileName = ngrokPath,
                            Arguments = $"config add-authtoken {AUTH_TOKEN}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    configProcess.Start();
                    configProcess.WaitForExit(5000);
                    configProcess.Dispose();

                    this.Invoke(new Action(() => AddLog("✅ ngrok 认证配置成功")));

                    // ========== 启动 ngrok ==========
                    ProcessStartInfo startInfo = new ProcessStartInfo()
                    {
                        FileName = ngrokPath,
                        Arguments = $"http {port} --log=stdout --log-level=debug --region=jp",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    ngrokProcess = new Process()
                    {
                        StartInfo = startInfo,
                        EnableRaisingEvents = true
                    };

                    ngrokProcess.OutputDataReceived -= NgrokOutputHandler;
                    ngrokProcess.ErrorDataReceived -= NgrokErrorHandler;
                    ngrokProcess.Exited -= NgrokExitedHandler;

                    ngrokProcess.OutputDataReceived += NgrokOutputHandler;
                    ngrokProcess.ErrorDataReceived += NgrokErrorHandler;
                    ngrokProcess.Exited += NgrokExitedHandler;

                    ngrokProcess.Start();
                    ngrokProcess.BeginOutputReadLine();
                    ngrokProcess.BeginErrorReadLine();

                    isNgrokRunning = true;

                    this.Invoke(new Action(() =>
                    {
                        btnNgrok.Text = "🔴 关闭外网";
                        btnNgrok.BackColor = Color.FromArgb(255, 59, 48);
                        AddLog("ngrok 已启动，正在获取公网地址...");

                        Control lblStatus = this.Controls.Find("lblDownloadStatus", true).FirstOrDefault();
                        if (lblStatus != null)
                        {
                            lblStatus.Text = "⏳ 正在连接 ngrok...";
                            lblStatus.ForeColor = Color.FromArgb(0, 122, 255);
                            lblStatus.Visible = true;
                        }
                    }));

                    // 超时检测 60 秒
                    System.Timers.Timer timeoutTimer = new System.Timers.Timer(60000) { AutoReset = false };
                    timeoutTimer.Elapsed += (sender2, e2) =>
                    {
                        if (string.IsNullOrEmpty(ngrokUrl) && isNgrokRunning)
                        {
                            this.Invoke(new Action(() =>
                            {
                                AddLog("⚠️ ngrok 获取地址超时 (60秒)");
                                Control lblStatus = this.Controls.Find("lblDownloadStatus", true).FirstOrDefault();
                                if (lblStatus != null)
                                {
                                    lblStatus.Text = "⚠️ 连接超时，请检查网络";
                                    lblStatus.ForeColor = Color.Red;
                                    lblStatus.Visible = true;
                                }
                            }));
                        }
                        timeoutTimer.Dispose();
                    };
                    timeoutTimer.Start();

                }
                catch (Exception ex)
                {
                    AddLog($"启动 ngrok 进程失败: {ex.Message}");
                    this.Invoke(new Action(() =>
                    {
                        btnNgrok.Text = "🌐 开启外网";
                        btnNgrok.BackColor = Color.FromArgb(52, 199, 89);
                        isNgrokRunning = false;
                    }));
                }
            });
        }

        /// <summary>
        /// 加载保存的 ngrok 认证令牌
        /// </summary>
        private string LoadNgrokAuthToken()
        {
            try
            {
                string tokenFile = GetConfigPath("ngrok_token.txt");
                if (File.Exists(tokenFile))
                {
                    string saved = File.ReadAllText(tokenFile).Trim();
                    if (!string.IsNullOrEmpty(saved)) return saved;
                }

                // 文件不存在或内容为空：弹窗由人工输入，绝不回退到内置 / 硬编码令牌
                return PromptForNgrokToken();
            }
            catch (Exception ex)
            {
                AddLog($"读取认证令牌失败: {ex.Message}");
            }
            return "";
        }

        /// <summary>
        /// 保存 ngrok 认证令牌
        /// </summary>
        private void SaveNgrokAuthToken(string token)
        {
            try
            {
                string tokenFile = GetConfigPath("ngrok_token.txt");
                File.WriteAllText(tokenFile, token);
                AddLog("认证令牌已保存");
            }
            catch (Exception ex)
            {
                AddLog($"保存认证令牌失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 弹窗让用户输入 ngrok 认证令牌（保证在 UI 线程弹出），并写入 config/ngrok_token.txt。
        /// 返回用户输入的令牌；用户取消 / 留空则返回空字符串。
        /// </summary>
        private string PromptForNgrokToken()
        {
            string input = null;
            this.Invoke(new Action(() =>
            {
                input = InputBox("请输入 ngrok 认证令牌（AuthToken）\n可在 ngrok 官网控制台复制",
                                 "认证令牌输入", "");
            }));

            if (!string.IsNullOrWhiteSpace(input))
            {
                string token = input.Trim();
                SaveNgrokAuthToken(token);
                return token;
            }
            return "";
        }

        /// <summary>
        /// 停止 ngrok（异步）
        /// </summary>
        private Task StopNgrokAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    // 标记为主动停止，避免 NgrokExitedHandler 误报"意外退出"
                    _isStoppingNgrok = true;

                    // 停止 ngrok 进程
                    if (ngrokProcess != null && !ngrokProcess.HasExited)
                    {
                        // 进程未重定向 StandardInput，无法通过 stdin 优雅关闭，直接 Kill
                        try
                        {
                            ngrokProcess.Kill();
                            ngrokProcess.WaitForExit(2000);
                        }
                        catch
                        {
                        }

                        ngrokProcess.Dispose();
                        ngrokProcess = null;
                    }

                    isNgrokRunning = false;
                    ngrokUrl = "";

                    this.Invoke(new Action(() =>
                    {
                        btnNgrok.Text = "🌐 开启外网";
                        btnNgrok.BackColor = Color.FromArgb(52, 199, 89);

                        Control lblPublicUrl = this.Controls.Find("lblPublicUrl", true).FirstOrDefault();
                        if (lblPublicUrl != null)
                        {
                            lblPublicUrl.Text = "";
                        }

                        // 隐藏复制地址按钮
                        if (lblNgrokCopy != null)
                        {
                            lblNgrokCopy.Visible = false;
                        }

                        Control lblStatus = this.Controls.Find("lblDownloadStatus", true).FirstOrDefault();
                        if (lblStatus != null)
                        {
                            lblStatus.Text = "";
                            lblStatus.Visible = false;
                        }

                        ProgressBar pb = this.Controls.Find("pbDownloadProgress", true).FirstOrDefault() as ProgressBar;
                        if (pb != null)
                        {
                            pb.Visible = false;
                        }

                        lblHint.Text = "提示:启动服务后将会显示登录二维码";

                        // 隐藏外网二维码框
                        UpdateWanQrVisibility();

                        // 重新生成局域网二维码
                        string ip = txtIp.Text.Trim();
                        int port;
                        if (int.TryParse(txtPort.Text.Trim(), out port))
                        {
                            GenerateQrCode(ip, port);
                        }

                        AddLog("ngrok 已停止");
                    }));
                }
                catch (Exception ex)
                {
                    AddLog($"停止 ngrok 失败: {ex.Message}");
                }
                finally
                {
                    _isStoppingNgrok = false;
                }
            });
        }

        /// <summary>
        /// 处理 ngrok 输出（解析公网地址）
        /// </summary>
        private void NgrokOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            string line = e.Data.Trim();

            // 调试日志
            if (line.Length > 0)
            {
                Console.WriteLine($"ngrok: {line}");
                // 记录关键信息
                if (line.Contains("url") || line.Contains("Forwarding") || line.Contains("error") || line.Contains("ERR_"))
                {
                    this.Invoke(new Action(() => AddLog($"ngrok: {line}")));
                }
            }

            // 检查错误
            if (line.Contains("ERR_NGROK"))
            {
                this.Invoke(new Action(() =>
                {
                    AddLog($"⚠️ ngrok 错误: {line}");
                    // 如果是域名冲突错误，自动重试
                    if (line.Contains("ERR_NGROK_334"))
                    {
                        AddLog("检测到域名冲突，正在重试...");
                    }
                }));
                return;
            }

            string url = "";

            // 匹配 Forwarding 格式
            Match match = Regex.Match(line, "Forwarding\\s+(https?://[^\\s]+)");
            if (match.Success)
            {
                url = match.Groups[1].Value;
            }

            // 匹配 started tunnel
            if (string.IsNullOrEmpty(url))
            {
                Match match2 = Regex.Match(line, "started tunnel\\s+(https?://[^\\s]+)");
                if (match2.Success)
                {
                    url = match2.Groups[1].Value;
                }
            }

            // 匹配通用 URL
            if (string.IsNullOrEmpty(url))
            {
                Match match3 = Regex.Match(line, "(https?://[a-z0-9-]+\\.ngrok-[a-z]+\\.dev)");
                if (match3.Success)
                {
                    url = match3.Groups[1].Value;
                }
            }

            if (!string.IsNullOrEmpty(url) && url != ngrokUrl)
            {
                ngrokUrl = url;
                this.Invoke(new Action(() =>
                {
                    Control lblPublicUrl = this.Controls.Find("lblPublicUrl", true).FirstOrDefault();
                    if (lblPublicUrl != null)
                    {
                        lblPublicUrl.Text = $"🌐 {url}";
                        lblPublicUrl.ForeColor = Color.Green;
                        if (tooltipNgrok == null) tooltipNgrok = new ToolTip();
                        tooltipNgrok.SetToolTip(lblPublicUrl, url);
                        tooltipNgrok.SetToolTip(lblNgrokCopy, $"复制外网地址:\r\n{url}");
                    }

                    lblHint.Text = $"✅ 外网已开启!\r\n手机扫描二维码或访问: {url}";

                    try
                    {
                        GenerateWanQrCode(url);
                        UpdateWanQrVisibility();
                    }
                    catch (Exception)
                    {
                        // 忽略
                    }

                    AddLog($"✅ ngrok 公网地址: {url}");

                    Control lblStatus = this.Controls.Find("lblDownloadStatus", true).FirstOrDefault();
                    if (lblStatus != null)
                    {
                        lblStatus.Text = "✅ 外网已开启";
                        lblStatus.ForeColor = Color.Green;
                        lblStatus.Visible = true;
                    }

                    // 显示复制地址按钮
                    if (lblNgrokCopy != null)
                    {
                        lblNgrokCopy.Visible = true;
                    }

                    // 更新外网按钮为"关闭外网"状态（自动监听启动时服务可能未运行）
                    if (btnNgrok != null)
                    {
                        btnNgrok.Text = "🔴 关闭外网";
                        btnNgrok.BackColor = Color.FromArgb(255, 59, 48);
                        btnNgrok.Enabled = true;
                    }
                }));
            }
        }

        /// <summary>
        /// 处理 ngrok 错误输出
        /// </summary>
        private void NgrokErrorHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            string line = e.Data.Trim();

            // 只记录重要错误
            if (line.Contains("error") || line.Contains("failed") || line.Contains("FATA"))
            {
                AddLog($"ngrok 错误: {line}");
            }
        }

        /// <summary>
        /// ngrok 进程退出事件
        /// </summary>
        private void NgrokExitedHandler(object sender, EventArgs e)
        {
            this.Invoke(new Action(() =>
            {
                // 主动停止时不提示"意外退出"
                if (_isStoppingNgrok) return;

                if (isNgrokRunning)
                {
                    AddLog("⚠️ ngrok 进程意外退出");
                    isNgrokRunning = false;
                    ngrokUrl = "";
                    btnNgrok.Text = "🌐 开启外网";
                    btnNgrok.BackColor = Color.FromArgb(52, 199, 89);

                    Control lblPublicUrl = this.Controls.Find("lblPublicUrl", true).FirstOrDefault();
                    if (lblPublicUrl != null)
                    {
                        lblPublicUrl.Text = "❌ 外网已断开";
                        lblPublicUrl.ForeColor = Color.Red;
                    }

                    // 隐藏复制地址按钮
                    if (lblNgrokCopy != null)
                    {
                        lblNgrokCopy.Visible = false;
                    }

                    // 清空"外网已开启"状态标签
                    Control lblDlStatus = this.Controls.Find("lblDownloadStatus", true).FirstOrDefault();
                    if (lblDlStatus != null)
                    {
                        lblDlStatus.Text = "";
                        lblDlStatus.Visible = false;
                    }

                    // 隐藏外网二维码框
                    UpdateWanQrVisibility();

                    // 尝试自动重连（可选）
                    // StartNgrok()
                }
            }));
        }

        /// <summary>
        /// 停止 ngrok
        /// </summary>
        private void StopNgrok()
        {
            try
            {
                // 标记为主动停止，避免 NgrokExitedHandler 误报"意外退出"
                _isStoppingNgrok = true;

                if (ngrokProcess != null && !ngrokProcess.HasExited)
                {
                    // 进程未重定向 StandardInput，无法通过 stdin 优雅关闭，直接 Kill
                    try
                    {
                        ngrokProcess.Kill();
                        ngrokProcess.WaitForExit(2000);
                    }
                    catch
                    {
                    }
                    ngrokProcess.Dispose();
                    ngrokProcess = null;
                }

                isNgrokRunning = false;
                ngrokUrl = "";

                btnNgrok.Text = "🌐 开启外网";
                btnNgrok.BackColor = Color.FromArgb(52, 199, 89);

                Control lblPublicUrl = this.Controls.Find("lblPublicUrl", true).FirstOrDefault();
                if (lblPublicUrl != null)
                {
                    lblPublicUrl.Text = "";
                }

                // 隐藏复制地址按钮
                if (lblNgrokCopy != null)
                {
                    lblNgrokCopy.Visible = false;
                }

                // 清空"外网已开启"状态标签
                Control lblDlStatus = this.Controls.Find("lblDownloadStatus", true).FirstOrDefault();
                if (lblDlStatus != null)
                {
                    lblDlStatus.Text = "";
                    lblDlStatus.Visible = false;
                }

                lblHint.Text = "提示:启动服务后将会显示登录二维码";

                // 隐藏外网二维码框
                UpdateWanQrVisibility();

                // 重新生成局域网二维码
                string ip = txtIp.Text.Trim();
                int port;
                if (int.TryParse(txtPort.Text.Trim(), out port))
                {
                    GenerateQrCode(ip, port);
                }

                AddLog("ngrok 已停止");
                AutoWindow("外网隧道已关闭", 1, "提示");

            }
            catch (Exception ex)
            {
                AddLog($"停止 ngrok 失败: {ex.Message}");
            }
            finally
            {
                _isStoppingNgrok = false;
            }
        }

        /// <summary>
        /// 生成局域网二维码（显示到局域网二维码框）
        /// </summary>
        private void GenerateLanQrCode(string host, int port)
        {
            GenerateQrCodeTo(pbQrCodeLan, host, port);
            if (lblLanHint != null)
            {
                lblLanHint.ForeColor = Color.Green;
                lblLanHint.Text = "局域网二维码（可用）";
            }
        }

        /// <summary>
        /// 生成外网二维码（显示到外网二维码框）
        /// </summary>
        private void GenerateWanQrCode(string url)
        {
            GenerateQrCodeTo(pbQrCodeWan, url, 443);
            if (lblWanHint != null)
            {
                lblWanHint.ForeColor = Color.Green;
                lblWanHint.Text = "外网二维码（可用）";
            }
        }

        /// <summary>
        /// 通用二维码生成，输出到指定 PictureBox
        /// </summary>
        private void GenerateQrCodeTo(PictureBox target, string host, int port)
        {
            if (target == null) return;
            try
            {
                // 判断是否使用 HTTPS（端口443或地址以https开头）
                bool useHttps = (port == 443) || host.StartsWith("https://");

                // 清理 host（去掉协议前缀）
                string cleanHost = host.Replace("https://", "").Replace("http://", "");

                // 构建 URL
                string protocol = useHttps ? "https" : "http";
                string url;

                if (port == 443 && useHttps)
                {
                    url = $"https://{cleanHost}/";
                }
                else if (port == 80 && !useHttps)
                {
                    url = $"http://{cleanHost}/";
                }
                else
                {
                    url = $"{protocol}://{cleanHost}:{port}/";
                }

                // 使用 QRCoder 生成二维码
                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                QRCode qrCode = new QRCode(qrCodeData);

                Bitmap qrImage = qrCode.GetGraphic(20);

                target.Image?.Dispose();
                target.Image = qrImage;

            }
            catch (Exception ex)
            {
                AddLog($"生成二维码失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 兼容旧代码的 GenerateQrCode 方法，支持域名（生成局域网二维码）
        /// </summary>
        private void GenerateQrCode(string host, int port)
        {
            GenerateLanQrCode(host, port);

            // 更新提示文字（保留已有信息）
            if (lblHint != null)
            {
                if (!string.IsNullOrEmpty(lblHint.Text) && !lblHint.Text.Contains("外网"))
                {
                    lblHint.Text = $"手机扫描二维码访问";
                }
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (ip.ToString().StartsWith("10.") || ip.ToString().StartsWith("192.168."))
                        {
                            return ip.ToString();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return "";
        }

        private void Send404(HttpListenerResponse res)
        {
            try
            {
                byte[] msg = Encoding.UTF8.GetBytes("404 Not Found");
                res.StatusCode = 404;
                res.ContentLength64 = msg.Length;
                res.OutputStream.Write(msg, 0, msg.Length);
            }
            catch
            {
                // 客户端已断开：静默处理
            }
        }

        /// <summary>提供静态资源文件（如 web/libs/ 下的 JS 库），供局域网离线使用</summary>
        private void SendStaticFile(HttpListenerResponse res, string relativePath)
        {
            try
            {
                // 防止路径穿越：仅允许文件名，不含目录分隔符
                string fileName = Path.GetFileName(relativePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    Send404(res);
                    return;
                }
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "libs", fileName);
                if (!File.Exists(filePath))
                {
                    Send404(res);
                    return;
                }

                byte[] buffer = File.ReadAllBytes(filePath);
                // 根据扩展名设置 Content-Type
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                switch (ext)
                {
                    case ".js":
                        res.ContentType = "application/javascript; charset=utf-8";
                        break;
                    case ".css":
                        res.ContentType = "text/css; charset=utf-8";
                        break;
                    case ".png":
                        res.ContentType = "image/png";
                        break;
                    case ".jpg":
                    case ".jpeg":
                        res.ContentType = "image/jpeg";
                        break;
                    case ".gif":
                        res.ContentType = "image/gif";
                        break;
                    case ".svg":
                        res.ContentType = "image/svg+xml";
                        break;
                    case ".ico":
                        res.ContentType = "image/x-icon";
                        break;
                    default:
                        res.ContentType = "application/octet-stream";
                        break;
                }
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                AddLog($"SendStaticFile 异常: {ex.Message}");
                Send404(res);
            }
        }

        private void Send400(HttpListenerResponse res, string info)
        {
            try
            {
                byte[] msg = Encoding.UTF8.GetBytes($"400 Bad Request: {info}");
                res.StatusCode = 400;
                res.ContentLength64 = msg.Length;
                res.OutputStream.Write(msg, 0, msg.Length);
            }
            catch
            {
                // 客户端已断开：静默处理
            }
        }

        private void Send500(HttpListenerResponse res, string info)
        {
            try
            {
                byte[] msg = Encoding.UTF8.GetBytes($"500 Error: {info}");
                res.StatusCode = 500;
                res.ContentLength64 = msg.Length;
                res.OutputStream.Write(msg, 0, msg.Length);
            }
            catch
            {
                // 响应已提交或客户端已断开：静默处理，禁止在异常处理链中再抛异常
            }
        }

        // 托盘相关
        private NotifyIcon notifyIcon;
        private ContextMenuStrip notifyContextMenu;

        /// <summary>初始化托盘图标</summary>
        private void InitializeTrayIcon()
        {
            notifyIcon = new NotifyIcon()
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Text = "私有云盘",
                Visible = true
            };

            notifyContextMenu = new ContextMenuStrip();
            ToolStripMenuItem showItem = new ToolStripMenuItem("显示主程序");
            showItem.Click += TrayShowWindow;
            ToolStripMenuItem exitItem = new ToolStripMenuItem("关闭程序");
            exitItem.Click += TrayExitApplication;
            notifyContextMenu.Items.Add(showItem);
            notifyContextMenu.Items.Add(exitItem);
            notifyIcon.ContextMenuStrip = notifyContextMenu;

            notifyIcon.DoubleClick += TrayShowWindow;
        }

        private void TrayShowWindow(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
        }

        private void TrayExitApplication(object sender, EventArgs e)
        {
            // 停止外网自动监听调度定时器
            if (ngrokAutoTimer != null)
            {
                ngrokAutoTimer.Stop();
                ngrokAutoTimer.Dispose();
                ngrokAutoTimer = null;
            }

            // 新增：停止 ngrok
            if (isNgrokRunning)
            {
                StopNgrok();
            }

            if (isRunning)
            {
                StopService();
            }
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            Application.Exit();
        }

        // 重写窗体关闭和大小调整
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }

        /// <summary>
        /// 窗体真正关闭（程序退出）时，自动静默保存本次运行日志。
        /// 注意：点击关闭按钮只是隐藏窗体（见 OnFormClosing），不会走到这里。
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveLogToFile();
            base.OnFormClosed(e);
        }

        // 启动监听模式（等待第一个连接）
        private void StartListeningForFirstConnection()
        {
            if (listenForFirstConnection) return;
            listenForFirstConnection = true;
            _stopWaiting = false;

            int port;
            if (!int.TryParse(txtPort.Text.Trim(), out port))
            {
                port = DefaultPort;
            }

            try
            {
                tcpListener = new TcpListener(IPAddress.Any, port);
                tcpListener.Start();
                AddLog($"端口 {port} 监听已启动，等待外部设备访问以自动启动服务...");
                lblHint.Text = "程序后台运行，外部访问将自动启动服务";
                lblStatus.Text = "⏳ 待机监听中...";
                lblStatus.ForeColor = Color.Gray;

                waitThread = new System.Threading.Thread(WaitForFirstConnection) { IsBackground = true };
                waitThread.Start(port);
            }
            catch (Exception ex)
            {
                AddLog("启动监听失败: " + ex.Message);
            }
        }

        // 服务空闲超时（分钟）
        private const int IDLE_TIMEOUT_MINUTES = 10;

        /// <summary>等待第一个连接以唤醒服务（待机监听模式）</summary>
        private void WaitForFirstConnection(object portObj)
        {
            // 记录待机监听实际使用的端口，供后续自动拉起服务使用（避免与手动端口不一致）
            int listenPort = (portObj is int) ? (int)portObj : DefaultPort;
            try
            {
                AddLog("等待外部连接唤醒服务...");
                while (!_stopWaiting)
                {
                    TcpClient client = tcpListener.AcceptTcpClient();
                    if (_stopWaiting) { client.Close(); break; }
                    IPEndPoint remoteEP = (IPEndPoint)client.Client.RemoteEndPoint;
                    string clientIP = remoteEP.Address.ToString();
                    AddLog($"检测到外部连接，来源 IP: {clientIP}");

                    // 关键修复：待机模式下先把首页响应给客户端。
                    // 否则原始 TcpListener 只是接收后立刻关闭，手机浏览器会收到“连接被重置”，
                    // 导致“网页一直打不开”，必须手动启动服务后下一次请求才被 HttpListener 服务。
                    try { SendStandbyWebPage(client); }
                    catch (Exception exPage) { AddLog($"待机返回首页失败: {exPage.Message}"); try { client.Close(); } catch { } }

                    // 停止监听
                    tcpListener.Stop();
                    tcpListener = null;
                    listenForFirstConnection = false;
                    // 在主线程启动服务（传入与待机监听一致的端口，确保手机能连上）
                    this.Invoke(new Action(() => StartServiceAfterWakeup(clientIP, listenPort)));
                    break;
                }
            }
            catch (Exception ex)
            {
                if (!_stopWaiting)
                {
                    AddLog($"监听等待异常: {ex.Message}");
                }
            }
            finally
            {
                listenForFirstConnection = false;
            }
        }

        /// <summary>外部访问唤醒后自动启动服务</summary>
        private void StartServiceAfterWakeup(string clientIP, int port)
        {
            try
            {
                // 与正常服务一致：提示外部设备连接成功（待机模式下同样需要弹窗通知）
                AutoWindow($"外部设备IP:{clientIP} 连接成功!!!", 2, "外部设备连接成功提示");

                AddLog($"检测到外部访问（来自 {clientIP}），自动启动服务...");

                // 确保共享文件夹存在
                if (!Directory.Exists(SharedFolderPath))
                {
                    Directory.CreateDirectory(SharedFolderPath);
                }

                // 启动 HTTP 服务（使用与待机监听一致的端口）
                // 注意：每次重试都重新创建 HttpListener。因为一旦 listener.Start() 失败，
                // 该对象会进入错误/已释放状态，再次 Start() 会抛 ObjectDisposedException，
                // 若只在外面创建一次，重试将全部失效，导致自动拉起彻底失败。
                bool started = false;
                for (int i = 0; i < 10 && !started; i++)
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://*:{port}/");
                    try
                    {
                        listener.Start();
                        started = true;
                    }
                    catch (Exception exBind)
                    {
                        try { listener.Close(); } catch { }
                        listener = null;
                        AddLog($"自动启动服务绑定端口 {port} 重试 {i + 1}: {exBind.Message}");
                        System.Threading.Thread.Sleep(300);
                    }
                }
                if (!started)
                {
                    AddLog($"自动启动服务失败：端口 {port} 绑定重试后仍不可用");
                    UpdateUIState(false);
                    return;
                }

                isRunning = true;
                isServiceRunning = true;
                UpdateUIState(true);

                listenThread = new System.Threading.Thread(ListenLoop) { IsBackground = true };
                listenThread.Start();

                AddLog($"服务已通过外部访问自动启动，监听端口 {port}");
                GenerateQrCode(txtIp.Text.Trim(), port);

                // 启动空闲检测
                lastActivityTime = DateTime.Now;
                StartInactivityTimer();

                // 公网中转：自动启动中转核心（确保外网也能访问）
                // [已移除] EnsureRelayCore();
            }
            catch (Exception ex)
            {
                AddLog($"自动启动服务失败: {ex.Message}");
                UpdateUIState(false);
            }
        }

        /// <summary>
        /// 待机监听模式下，把首页（web/index.html）通过已接受的 TCP 连接响应给客户端。
        /// 这样手机/浏览器首次访问就能直接看到网页，而不是被原始 TcpListener 关闭导致“网页打不开”。
        /// 调用后 client 会被释放，调用方不要再重复 Close。
        /// </summary>
        private void SendStandbyWebPage(TcpClient client)
        {
            using (client)
            using (var ns = client.GetStream())
            {
                // 读取并消费请求头（读到空行即可），避免阻塞
                var buf = new byte[4096];
                var ms = new MemoryStream();
                int n;
                while ((n = ns.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    string txt = Encoding.ASCII.GetString(ms.ToArray());
                    if (txt.IndexOf("\r\n\r\n") >= 0) break;
                    if (ms.Length > 65536) break; // 防止异常连接耗尽内存
                }

                // 读取真实网页文件（与 SendHtml 使用相同路径）
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", "index.html");
                string html = File.Exists(htmlPath)
                    ? File.ReadAllText(htmlPath, Encoding.UTF8)
                    : "<html><body>LocalCloud</body></html>";
                byte[] body = Encoding.UTF8.GetBytes(html);

                string header = "HTTP/1.1 200 OK\r\n" +
                                "Content-Type: text/html; charset=utf-8\r\n" +
                                "Content-Length: " + body.Length.ToString() + "\r\n" +
                                "Connection: close\r\n\r\n";
                byte[] hb = Encoding.ASCII.GetBytes(header);
                ns.Write(hb, 0, hb.Length);
                ns.Write(body, 0, body.Length);
                ns.Flush();
            }
        }

        /// <summary>启动空闲检测定时器（每30秒检查一次）</summary>
        private void StartInactivityTimer()
        {
            if (inactivityTimer == null)
            {
                inactivityTimer = new System.Timers.Timer(30000) { AutoReset = true };
                inactivityTimer.Elapsed += CheckInactivity;
            }
            inactivityTimer.Start();
        }

        /// <summary>空闲检测：超过设定时间无访问则自动停止服务</summary>
        private void CheckInactivity(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (!isServiceRunning) return;
                double idleMinutes = (DateTime.Now - lastActivityTime).TotalMinutes;
                // 检查公网中转连接是否仍在活动（不计入空闲时间）
                // [已移除] If _relayConn IsNot Nothing AndAlso _relayConn.IsAlive Then ...
                if (idleMinutes >= IDLE_TIMEOUT_MINUTES)
                {
                    AddLog($"服务空闲 {idleMinutes:F1} 分钟，自动停止服务（命中空闲策略）");
                    AutoStopService();
                }
            }
            catch (Exception)
            {
                // 忽略定时器异常，避免崩溃
            }
        }

        /// <summary>空闲超时自动停止服务（在 UI 线程执行）</summary>
        private void AutoStopService()
        {
            try
            {
                // 在 UI 线程停止服务
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => AutoStopService()));
                    return;
                }
                AddLog("空闲超时，自动停止服务");
                StopService();

                // 空闲超时自动停止时保留本次日志
                SaveLogToFile();
            }
            catch (Exception ex)
            {
                AddLog($"自动停止服务失败: {ex.Message}");
            }
        }

        /// <summary>停止 HTTP 服务</summary>
        private void StopService()
        {
            try
            {
                isRunning = false;
                isServiceRunning = false;

                // 停止空闲检测定时器
                if (inactivityTimer != null)
                {
                    inactivityTimer.Stop();
                    inactivityTimer.Dispose();
                    inactivityTimer = null;
                }

                // 停止 HTTP 服务
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                    listener = null;
                }

                if (listenThread != null)
                {
                    listenThread = null;
                }

                // 停止公网中转核心（释放中转资源）
                // [已移除] StopRelayCore();

                // 更新 UI
                UpdateUIState(false);
                AddLog("服务已停止");
            }
            catch (Exception ex)
            {
                AddLog($"停止服务失败: {ex.Message}");
            }
        }
        /// <summary>C# 版 InputBox 简易实现（返回输入文本，取消返回空字符串）</summary>
        private string InputBox(string prompt, string title, string defaultResponse)
        {
            using (Form frm = new Form()
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(360, 140)
            })
            {
                Label lbl = new Label() { Text = prompt, Location = new Point(20, 20), AutoSize = true };
                TextBox txt = new TextBox() { Text = defaultResponse, Location = new Point(20, 50), Width = 320 };
                Button ok = new Button() { Text = "确定", Location = new Point(180, 90), Width = 75, DialogResult = DialogResult.OK };
                Button cancel = new Button() { Text = "取消", Location = new Point(265, 90), Width = 75, DialogResult = DialogResult.Cancel };
                frm.Controls.Add(lbl); frm.Controls.Add(txt); frm.Controls.Add(ok); frm.Controls.Add(cancel);
                frm.AcceptButton = ok; frm.CancelButton = cancel;
                return frm.ShowDialog(this) == DialogResult.OK ? txt.Text : "";
            }
        }
    }
}
