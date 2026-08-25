using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class ArrowWheel
{
    const string EXIT_EVENT = "ArrowWheel_ExitEvent";
    const string SHOW_EVENT = "ArrowWheel_ShowEvent";
    const string MUTEX_NAME = "ArrowWheel_SingleInstance";
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_SYSKEYDOWN = 0x0104;
    const uint WM_MOUSEWHEEL = 0x020A;
    const int VK_UP = 0x26;
    const int VK_DOWN = 0x28;
    const int WHEEL_DELTA = 120;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    const int INPUT_MOUSE = 0;
    const uint MOUSEEVENTF_WHEEL = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool GetGUIThreadInfo(uint idThread, out GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    static HookProc _proc;
    static IntPtr _hook = IntPtr.Zero;
    static IntPtr _ownHandle = IntPtr.Zero;
    static IntPtr _lastTarget = IntPtr.Zero;
    static int _mode = 1;

    public const int MODE_CURSOR = 0;      // 真实滚轮，光标位置
    public const int MODE_FOCUSED = 1;     // 真实滚轮，聚焦窗口（临时移光标）
    public const int MODE_SENDMSG = 2;     // 直接发消息给聚焦窗口

    [STAThread]
    static void Main()
    {
        if (!IsElevated())
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    Verb = "runas",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
            return;
        }

        bool createdNew;
        using (Mutex m = new Mutex(true, MUTEX_NAME, out createdNew))
        {
            if (!createdNew)
            {
                try { EventWaitHandle.OpenExisting(SHOW_EVENT).Set(); } catch { }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            _proc = new HookProc(KeyboardHookCallback);
            Application.Run(new MainForm());
        }
    }

    static bool IsElevated()
    {
        try
        {
            return new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == VK_UP || vk == VK_DOWN)
                {
                    int delta = (vk == VK_UP) ? WHEEL_DELTA : -WHEEL_DELTA;
                    Scroll(delta);
                    return (IntPtr)1;
                }
            }
        }
        catch
        {
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static void Scroll(int delta)
    {
        if (_mode == MODE_CURSOR)
        {
            SendWheelInput(delta);
            return;
        }

        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return;

        if (fg == _ownHandle)
        {
            fg = (_lastTarget != IntPtr.Zero && IsWindow(_lastTarget)) ? _lastTarget : IntPtr.Zero;
            if (fg == IntPtr.Zero)
                return;
        }
        else
        {
            _lastTarget = fg;
        }

        if (_mode == MODE_FOCUSED)
        {
            RECT rc;
            GetWindowRect(fg, out rc);
            int cx = rc.Left + (rc.Right - rc.Left) / 2;
            int cy = rc.Top + (rc.Bottom - rc.Top) / 2;
            POINT orig;
            GetCursorPos(out orig);
            SetCursorPos(cx, cy);
            SendWheelInput(delta);
            SetCursorPos(orig.X, orig.Y);
            return;
        }

        // MODE_SENDMSG: 直接发消息给聚焦窗口的深层子窗口
        RECT rc2;
        GetWindowRect(fg, out rc2);
        int cx2 = rc2.Left + (rc2.Right - rc2.Left) / 2;
        int cy2 = rc2.Top + (rc2.Bottom - rc2.Top) / 2;

        IntPtr target = fg;

        uint tid;
        GetWindowThreadProcessId(fg, out tid);
        GUITHREADINFO gti = new GUITHREADINFO();
        gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        if (GetGUIThreadInfo(tid, out gti) && gti.hwndFocus != IntPtr.Zero && IsWindow(gti.hwndFocus))
            target = gti.hwndFocus;

        POINT center = new POINT();
        center.X = cx2;
        center.Y = cy2;
        IntPtr wp = WindowFromPoint(center);
        if (wp != IntPtr.Zero && IsWindow(wp) && wp != _ownHandle)
            target = wp;

        int wParam = delta << 16;
        int lParam = (cy2 << 16) | (cx2 & 0xFFFF);
        SendMessage(target, WM_MOUSEWHEEL, (IntPtr)wParam, (IntPtr)lParam);
    }

    static void SendWheelInput(int delta)
    {
        INPUT[] inp = new INPUT[1];
        inp[0].type = INPUT_MOUSE;
        inp[0].U.mi.dx = 0;
        inp[0].U.mi.dy = 0;
        inp[0].U.mi.mouseData = unchecked((uint)delta);
        inp[0].U.mi.dwFlags = MOUSEEVENTF_WHEEL;
        inp[0].U.mi.time = 0;
        inp[0].U.mi.dwExtraInfo = IntPtr.Zero;
        SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
    }

    public static bool IsRunning { get { return _hook != IntPtr.Zero; } }

    public static int Mode
    {
        get { return _mode; }
        set { _mode = value; }
    }

    public static IntPtr OwnHandle
    {
        get { return _ownHandle; }
        set { _ownHandle = value; }
    }

    public static bool Start()
    {
        if (_hook != IntPtr.Zero)
            return true;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public static bool Stop()
    {
        if (_hook == IntPtr.Zero)
            return true;
        bool ok = UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        return ok;
    }
}

class MainForm : Form
{
    const string EXIT_EVENT = "ArrowWheel_ExitEvent";
    const string SHOW_EVENT = "ArrowWheel_ShowEvent";

    Label _status;
    Button _btnStart, _btnStop, _btnExit;
    RadioButton _rbCursor, _rbFocused, _rbSendMsg;
    NotifyIcon _tray;
    EventWaitHandle _exitEvt;
    EventWaitHandle _showEvt;
    System.Windows.Forms.Timer _tick;
    bool _allowClose;

    public MainForm()
    {
        ArrowWheel.OwnHandle = this.Handle;

        Text = "ArrowWheel 设置";
        ClientSize = new Size(312, 238);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _status = new Label();
        _status.SetBounds(14, 14, 284, 20);
        _status.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        Controls.Add(_status);

        _btnStart = new Button();
        _btnStart.Text = "启动";
        _btnStart.SetBounds(14, 48, 92, 32);
        _btnStart.Click += (s, e) => StartHook();
        Controls.Add(_btnStart);

        _btnStop = new Button();
        _btnStop.Text = "停止";
        _btnStop.SetBounds(110, 48, 92, 32);
        _btnStop.Click += (s, e) => StopHook();
        Controls.Add(_btnStop);

        _btnExit = new Button();
        _btnExit.Text = "退出";
        _btnExit.SetBounds(206, 48, 92, 32);
        _btnExit.Click += (s, e) => DoExit();
        Controls.Add(_btnExit);

        var grp = new Label();
        grp.SetBounds(14, 92, 284, 18);
        grp.Text = "滚动模式：";
        grp.Font = new Font("Microsoft YaHei UI", 9F);
        Controls.Add(grp);

        _rbFocused = new RadioButton();
        _rbFocused.Text = "聚焦窗口（推荐，兼容无滚动条页面）";
        _rbFocused.SetBounds(22, 112, 276, 20);
        _rbFocused.Checked = true;
        _rbFocused.CheckedChanged += (s, e) => { if (_rbFocused.Checked) ArrowWheel.Mode = ArrowWheel.MODE_FOCUSED; };
        Controls.Add(_rbFocused);

        _rbCursor = new RadioButton();
        _rbCursor.Text = "光标所在位置（同真实鼠标滚轮）";
        _rbCursor.SetBounds(22, 134, 276, 20);
        _rbCursor.CheckedChanged += (s, e) => { if (_rbCursor.Checked) ArrowWheel.Mode = ArrowWheel.MODE_CURSOR; };
        Controls.Add(_rbCursor);

        _rbSendMsg = new RadioButton();
        _rbSendMsg.Text = "直接发消息（前台窗口不移动鼠标）";
        _rbSendMsg.SetBounds(22, 156, 276, 20);
        _rbSendMsg.CheckedChanged += (s, e) => { if (_rbSendMsg.Checked) ArrowWheel.Mode = ArrowWheel.MODE_SENDMSG; };
        Controls.Add(_rbSendMsg);

        var tip = new Label();
        tip.SetBounds(14, 184, 284, 34);
        tip.Text = "按 ↑ / ↓ 方向键模拟鼠标滚轮。\r\n点右上角 × 隐藏到托盘。";
        tip.ForeColor = Color.Gray;
        tip.Font = new Font("Microsoft YaHei UI", 9F);
        Controls.Add(tip);

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示设置", null, (s, e) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => DoExit());
        _tray = new NotifyIcon();
        _tray.Text = "ArrowWheel";
        _tray.Icon = SystemIcons.Shield;
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (s, e) => ShowWindow();
        _tray.Visible = true;

        _exitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, EXIT_EVENT);
        _showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, SHOW_EVENT);
        _tick = new System.Windows.Forms.Timer();
        _tick.Interval = 200;
        _tick.Tick += (s, e) =>
        {
            if (_exitEvt.WaitOne(0))
                DoExit();
            else if (_showEvt.WaitOne(0))
                ShowWindow();
        };
        _tick.Start();

        RefreshStatus();
        ArrowWheel.Start();
    }

    void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void StartHook()
    {
        if (ArrowWheel.Start())
            RefreshStatus();
        else
            MessageBox.Show(this, "启动钩子失败。", "ArrowWheel", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    void StopHook()
    {
        ArrowWheel.Stop();
        RefreshStatus();
    }

    void RefreshStatus()
    {
        bool running = ArrowWheel.IsRunning;
        _status.Text = running ? "● 运行中（↑/↓ 模拟滚轮）" : "○ 已停止";
        _status.ForeColor = running ? Color.ForestGreen : Color.DimGray;
        _btnStart.Enabled = !running;
        _btnStop.Enabled = running;
    }

    void DoExit()
    {
        _allowClose = true;
        ArrowWheel.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(1500, "ArrowWheel", "已隐藏到托盘，双击图标可重新打开设置。", ToolTipIcon.Info);
            return;
        }
        base.OnFormClosing(e);
    }
}
