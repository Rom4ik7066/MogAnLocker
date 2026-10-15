using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using MogLocker.AdBlock;
using System.Web.Script.Serialization;

namespace MogLocker
{
    internal sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
    {
        public BufferedTableLayoutPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    internal sealed class AlertRecord
    {
        public DateTime Time;
        public string Severity;
        public string Source;
        public string Details;
        public string Path;
        public uint ProcessId;
        public IntPtr WindowHandle;
    }

    internal sealed class SoundPadEntry
    {
        public string Name;
        public string Path;
    }

    internal sealed class WindowInfo
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string Title;
        public string ProcessName;
        public string ProcessPath;
        public bool IsTopmost;
        public bool IsBrowser;
        public Rectangle Bounds;
    }

    internal static class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        internal static extern int GetWindowLong32(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);

        [DllImport("user32.dll")]
        internal static extern uint GetMenuState(IntPtr menu, uint item, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint attachThreadId, uint attachThreadIdTo,
            [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("user32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllowSetForegroundWindow(uint processId);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WaveOutputCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public ushort DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ProductName;
            public uint Formats;
            public ushort Channels;
            public ushort Reserved;
            public uint Support;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WaveInputCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public ushort DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ProductName;
            public uint Formats;
            public ushort Channels;
            public ushort Reserved;
        }

        [DllImport("winmm.dll", EntryPoint = "waveOutGetNumDevs")]
        internal static extern uint GetWaveOutputDeviceCount();

        [DllImport("winmm.dll", EntryPoint = "waveInGetNumDevs")]
        internal static extern uint GetWaveInputDeviceCount();

        [DllImport("winmm.dll", EntryPoint = "waveOutGetDevCapsW", CharSet = CharSet.Unicode)]
        internal static extern uint GetWaveOutputDeviceCaps(
            IntPtr deviceId, out WaveOutputCaps caps, uint capsSize);

        [DllImport("winmm.dll", EntryPoint = "waveInGetDevCapsW", CharSet = CharSet.Unicode)]
        internal static extern uint GetWaveInputDeviceCaps(
            IntPtr deviceId, out WaveInputCaps caps, uint capsSize);
    }

    internal sealed class MainForm : Form
    {
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int WsCaption = 0x00C00000;
        private const int WsSysMenu = 0x00080000;
        private const int WsExTopmost = 0x00000008;
        private const uint ScClose = 0xF060;
        private const uint MfByCommand = 0x00000000;
        private const uint MfGrayed = 0x00000001;
        private const uint MfDisabled = 0x00000002;
        private const int EmergencyHotKeyId = 0x4D47;
        private const int WmHotKey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint VirtualKeyM = 0x4D;
        private const int PageTransitionDuration = 420;

        private readonly Timer scanTimer;
        private readonly Timer animationTimer;
        private readonly Timer pulseTimer;
        private readonly Timer pageTransitionTimer;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly List<AlertRecord> records = new List<AlertRecord>();
        private readonly Dictionary<string, DateTime> recentSignals = new Dictionary<string, DateTime>();
        private readonly object recentSignalsLock = new object();
        private readonly Dictionary<Panel, float> cardRevealProgress = new Dictionary<Panel, float>();
        private readonly DataGridView alertsGrid;
        private readonly Panel sidebar;
        private readonly Panel homePanel;
        private readonly Panel monitorPanel;
        private readonly Panel settingsPanel;
        private readonly Panel macTitleBar;
        private readonly Panel pageHost;
        private readonly TableLayoutPanel rootLayout;
        private readonly TableLayoutPanel bodyLayout;
        private readonly Button menuToggleButton;
        private readonly Button homeNavButton;
        private readonly Button adBlockNavButton;
        private readonly Button monitorNavButton;
        private readonly Button settingsNavButton;
        private readonly Label monitorState;
        private readonly Label pulseDot;
        private readonly Label logCountLabel;
        private readonly Label screenState;
        private readonly Label startupState;
        private readonly Label registryState;
        private readonly Label explorerState;
        private readonly Label networkState;
        private readonly Button pauseButton;
        private readonly AdBlockPanel adBlockPanel;
        private readonly AppSettings appSettings;
        private readonly List<SoundPadEntry> soundPadEntries = new List<SoundPadEntry>();
        private readonly Dictionary<int, string> soundPadHotkeys = new Dictionary<int, string>();
        private NotifyIcon trayIcon;
        private Form soundPadDialog;
        private ComboBox themeSelector;
        private ComboBox headerStyleSelector;
        private ComboBox logFilterSelector;
        private CheckBox animationsCheckBox;
        private Label exportDirectoryLabel;
        private Label updateStatusLabel;
        private Button checkUpdatesButton;
        private string currentFilter = null;
        private static CustomTheme activeCustomTheme;
        private bool applyingTheme;
        private readonly Dictionary<Control, ControlThemeState> originalControlColors =
            new Dictionary<Control, ControlThemeState>();
        private readonly Dictionary<DataGridViewCellStyle, CellThemeState> originalCellColors =
            new Dictionary<DataGridViewCellStyle, CellThemeState>();
        private Panel navigationIndicator;
        private bool menuExpanded = true;
        private int selectedPage = -1;
        private Control transitioningPage;
        private Control outgoingPage;
        private int transitionStartX;
        private DateTime transitionStartedAt;
        private Dictionary<string, string> registryBaseline;
        private bool isMonitoring = true;
        private bool emergencyHotKeyRegistered;
        private bool? explorerWasRunning;
        private bool? networkWasAvailable;
        private DateTime lastRegistryScan = DateTime.MinValue;
        private DateTime lastSystemStateScan = DateTime.MinValue;
        private DateTime lastDefenderExclusionScan = DateTime.MinValue;
        private Dictionary<string, string> defenderExclusionBaseline;
        private readonly HashSet<int> reportedRegeditProcesses = new HashSet<int>();
        private int defenderScanRunning;
        private int pulseStep;

        public MainForm(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            appSettings = settings;
            Text = "Моглокер — монитор защиты";
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimumSize = new Size(1000, 680);
            Size = new Size(1280, 820);
            BackColor = Color.FromArgb(12, 17, 31);
            ForeColor = Color.FromArgb(226, 232, 244);
            Font = new Font("Segoe UI", 9F);

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "Моглокер — монитор защиты",
                Visible = false
            };
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Открыть", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Пауза/Продолжить", null, (s, e) => ToggleMonitoring(null, EventArgs.Empty));
            trayMenu.Items.Add("Выход", null, (s, e) => Close());
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 120,
                BackColor = Color.FromArgb(18, 26, 45),
                Padding = new Padding(24, 18, 24, 14)
            };
            var brandMark = new Panel
            {
                BackColor = Color.FromArgb(104, 211, 184),
                Location = new Point(22, 24),
                Size = new Size(4, 58)
            };
            var title = new Label
            {
                Text = "МОГЛОКЕР",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold),
                ForeColor = Color.FromArgb(237, 244, 255),
                Location = new Point(38, 15)
            };
            var subtitle = new Label
            {
                Text = "ЛОКАЛЬНЫЙ МОНИТОР ПОДОЗРИТЕЛЬНЫХ БЛОКИРОВОК WINDOWS",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(142, 160, 185),
                Location = new Point(40, 62)
            };
            var headerStatus = new FlowLayoutPanel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Size = new Size(244, 38),
                Location = new Point(1012, 40),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.FromArgb(19, 39, 48),
                Padding = new Padding(12, 7, 8, 0)
            };
            pulseDot = new Label
            {
                Text = "●",
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(104, 211, 184),
                Margin = new Padding(0, 2, 8, 0)
            };
            monitorState = new Label
            {
                Text = "МОНИТОРИНГ ВКЛЮЧЁН",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(104, 211, 184),
                Margin = new Padding(0, 4, 0, 0)
            };
            headerStatus.Controls.Add(pulseDot);
            headerStatus.Controls.Add(monitorState);
            header.Controls.Add(brandMark);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(headerStatus);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22, 18, 22, 22),
                BackColor = BackColor,
                ColumnCount = 2,
                RowCount = 2
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 204F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var overview = MakeCard(Color.FromArgb(104, 211, 184));
            overview.Dock = DockStyle.Fill;
            overview.Padding = new Padding(21, 19, 20, 18);
            var overviewTitle = MakeSectionTitle("СОСТОЯНИЕ ЗАЩИТЫ");
            overviewTitle.Dock = DockStyle.Top;
            screenState = MakeStatusLabel("Экранные окна: ожидание");
            startupState = MakeStatusLabel("Автозагрузка: подготовка");
            registryState = MakeStatusLabel("Системные параметры: подготовка");
            var overviewBody = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 9, 0, 0)
            };
            overviewBody.Controls.Add(screenState);
            overviewBody.Controls.Add(startupState);
            overviewBody.Controls.Add(registryState);
            overview.Controls.Add(overviewBody);
            overview.Controls.Add(overviewTitle);

            var health = MakeCard(Color.FromArgb(111, 164, 211));
            health.Dock = DockStyle.Fill;
            health.Padding = new Padding(21, 19, 20, 18);
            var healthTitle = MakeSectionTitle("ПРОВЕРКИ WINDOWS");
            healthTitle.Dock = DockStyle.Top;
            var healthBody = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 9, 0, 0)
            };
            explorerState = MakeStatusLabel("Проводник: проверка");
            networkState = MakeStatusLabel("Сеть: проверка");
            healthBody.Controls.Add(explorerState);
            healthBody.Controls.Add(networkState);
            var safeNote = new Label
            {
                Text = "Подозрительную программу можно завершить вручную после подтверждения.",
                AutoSize = false,
                Height = 44,
                Width = 390,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(133, 151, 177),
                Margin = new Padding(0, 12, 0, 0)
            };
            healthBody.Controls.Add(safeNote);
            health.Controls.Add(healthBody);
            health.Controls.Add(healthTitle);

            var logCard = MakeCard(Color.FromArgb(67, 91, 122));
            logCard.Dock = DockStyle.Fill;
            logCard.Padding = new Padding(18);
            var logHeader = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.Transparent };
            var titleRow = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.Transparent };
            var logTitle = MakeSectionTitle("ЖУРНАЛ СОБЫТИЙ");
            logTitle.Location = new Point(4, 4);
            logTitle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            logCountLabel = new Label
            {
                Text = "0 событий",
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(133, 151, 177),
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0)
            };
            logFilterSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 132,
                Height = 22,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0, 0, 8, 0)
            };
            logFilterSelector.Items.AddRange(new object[]
            {
                "Все события",
                "ПОДОЗРИТЕЛЬНО",
                "ПРЕДУПРЕЖДЕНИЕ",
                "ИНФО",
                "ОШИБКА"
            });
            logFilterSelector.SelectedIndex = 0;
            currentFilter = null;
            logFilterSelector.SelectedIndexChanged += ApplyLogFilter;
            titleRow.Resize += delegate
            {
                int labelX = Math.Max(8, titleRow.ClientSize.Width - logCountLabel.Width - 10);
                logCountLabel.Location = new Point(labelX, 5);
                int filterX = Math.Max(8, labelX - logFilterSelector.Width - 10);
                logFilterSelector.Location = new Point(filterX, 2);
            };
            titleRow.Controls.Add(logTitle);
            titleRow.Controls.Add(logFilterSelector);
            titleRow.Controls.Add(logCountLabel);
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };
            pauseButton = MakeButton("Пауза", Color.FromArgb(41, 53, 76));
            pauseButton.Width = 100;
            var detailsButton = MakeButton("Сведения", Color.FromArgb(41, 53, 76));
            detailsButton.Width = 100;
            var copyButton = MakeButton("Копировать журнал", Color.FromArgb(41, 53, 76));
            copyButton.Width = 145;
            var saveButton = MakeButton("Сохранить лог", Color.FromArgb(41, 53, 76));
            saveButton.Width = 120;
            var soundPadButton = MakeButton("Саундпад", Color.FromArgb(64, 89, 122));
            soundPadButton.Width = 110;
            var recoveryButton = MakeButton("Помощь при блокировке", Color.FromArgb(64, 89, 122));
            recoveryButton.Width = 170;
            var terminateButton = MakeButton("Завершить выбранную", Color.FromArgb(133, 67, 62));
            terminateButton.Width = 164;
            var defenderButton = MakeButton("Проверить Defender", Color.FromArgb(53, 105, 99));
            defenderButton.Width = 160;
            pauseButton.Click += ToggleMonitoring;
            detailsButton.Click += ShowSelectedDetails;
            copyButton.Click += CopyReport;
            saveButton.Click += SaveReport;
            soundPadButton.Click += OpenSoundPad;
            recoveryButton.Click += ShowRecoveryGuide;
            terminateButton.Click += TerminateSelectedProcess;
            defenderButton.Click += ScanSelectedFileWithDefender;
            actions.Controls.Add(copyButton);
            actions.Controls.Add(saveButton);
            actions.Controls.Add(soundPadButton);
            actions.Controls.Add(detailsButton);
            actions.Controls.Add(pauseButton);
            actions.Controls.Add(recoveryButton);
            actions.Controls.Add(defenderButton);
            actions.Controls.Add(terminateButton);
            logHeader.Controls.Add(titleRow);
            logHeader.Controls.Add(actions);

            alertsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(14, 21, 36),
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                GridColor = Color.FromArgb(37, 49, 71),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                EnableHeadersVisualStyles = false,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(222, 229, 242),
                ColumnHeadersHeight = 40,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 40 },
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells
            };
            alertsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(25, 35, 56);
            alertsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(157, 176, 204);
            alertsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            alertsGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            alertsGrid.DefaultCellStyle.BackColor = Color.FromArgb(15, 22, 39);
            alertsGrid.DefaultCellStyle.ForeColor = Color.FromArgb(222, 229, 242);
            alertsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(41, 61, 86);
            alertsGrid.DefaultCellStyle.SelectionForeColor = Color.White;
            alertsGrid.DefaultCellStyle.Padding = new Padding(10, 2, 10, 2);
            alertsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(18, 26, 45);
            alertsGrid.Columns.Add("time", "Время");
            alertsGrid.Columns.Add("level", "Уровень");
            alertsGrid.Columns.Add("source", "Источник");
            alertsGrid.Columns.Add("details", "Событие");
            foreach (DataGridViewColumn column in alertsGrid.Columns)
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            alertsGrid.Columns[0].FillWeight = 13F;
            alertsGrid.Columns[1].FillWeight = 18F;
            alertsGrid.Columns[2].FillWeight = 20F;
            alertsGrid.Columns[3].FillWeight = 59F;
            alertsGrid.Columns[0].MinimumWidth = 82;
            alertsGrid.Columns[1].MinimumWidth = 125;
            alertsGrid.Columns[2].MinimumWidth = 150;
            alertsGrid.Columns[3].MinimumWidth = 300;
            alertsGrid.Columns[3].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            alertsGrid.Columns[3].DefaultCellStyle.ForeColor = Color.FromArgb(197, 209, 228);
            alertsGrid.CellToolTipTextNeeded += delegate(object sender, DataGridViewCellToolTipTextNeededEventArgs args)
            {
                if (args.RowIndex >= 0 && args.ColumnIndex == 3)
                    args.ToolTipText = Convert.ToString(alertsGrid.Rows[args.RowIndex].Cells[args.ColumnIndex].Value);
            };
            alertsGrid.CellDoubleClick += delegate { ShowSelectedDetails(this, EventArgs.Empty); };
            logCard.Controls.Add(alertsGrid);
            logCard.Controls.Add(logHeader);

            content.Controls.Add(overview, 0, 0);
            content.Controls.Add(health, 1, 0);
            content.Controls.Add(logCard, 0, 1);
            content.SetColumnSpan(logCard, 2);

            sidebar = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 22, 38),
                Padding = new Padding(0)
            };
            menuToggleButton = MakeButton("☰   МЕНЮ", Color.FromArgb(24, 35, 56));
            menuToggleButton.SetBounds(12, 16, 194, 40);
            menuToggleButton.TextAlign = ContentAlignment.MiddleLeft;
            menuToggleButton.Padding = new Padding(12, 0, 0, 0);
            menuToggleButton.Click += ToggleNavigation;
            homeNavButton = CreateNavigationButton("⌂   Главная");
            adBlockNavButton = CreateNavigationButton("◉   AdBlock");
            monitorNavButton = CreateNavigationButton("◈   Мониторинг");
            settingsNavButton = CreateNavigationButton("⚙   Настройки");
            homeNavButton.SetBounds(8, 74, 202, 44);
            adBlockNavButton.SetBounds(8, 126, 202, 44);
            monitorNavButton.SetBounds(8, 178, 202, 44);
            settingsNavButton.SetBounds(8, 230, 202, 44);
            navigationIndicator = new Panel
            {
                BackColor = Color.FromArgb(104, 211, 184),
                Bounds = new Rectangle(0, 74, 3, 44)
            };
            SetActiveNavigation(0);

            pageHost = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor
            };
            content.Dock = DockStyle.Fill;
            monitorPanel = content;
            homePanel = CreateHomePage();
            settingsPanel = CreateSettingsPage();
            adBlockPanel = new AdBlockPanel
            {
                Dock = DockStyle.Fill,
            };
            pageHost.Controls.Add(homePanel);
            pageHost.Controls.Add(content);
            pageHost.Controls.Add(adBlockPanel);
            pageHost.Controls.Add(settingsPanel);
            homeNavButton.Click += delegate { ShowPage(0); };
            adBlockNavButton.Click += delegate { ShowPage(1); };
            monitorNavButton.Click += delegate { ShowPage(2); };
            settingsNavButton.Click += delegate { ShowPage(3); };
            sidebar.Controls.Add(menuToggleButton);
            sidebar.Controls.Add(homeNavButton);
            sidebar.Controls.Add(adBlockNavButton);
            sidebar.Controls.Add(monitorNavButton);
            sidebar.Controls.Add(settingsNavButton);
            sidebar.Controls.Add(navigationIndicator);
            bodyLayout = new BufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218F));
            bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bodyLayout.Controls.Add(sidebar, 0, 0);
            bodyLayout.Controls.Add(pageHost, 1, 0);
            macTitleBar = CreateMacTitleBar();
            rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, header.Height));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            header.Dock = DockStyle.Fill;
            bodyLayout.Dock = DockStyle.Fill;
            rootLayout.Controls.Add(macTitleBar, 0, 0);
            rootLayout.Controls.Add(header, 0, 1);
            rootLayout.Controls.Add(bodyLayout, 0, 2);
            Controls.Add(rootLayout);
            ShowPage(0);

            registryBaseline = ReadRegistrySnapshot();
            ConfigureStartupWatchers();
            AddAlert("ИНФО", "Монитор", "Наблюдение запущено. Автоматического закрытия, удаления или восстановления нет.");
            screenState.Text = "Экранные окна: проверка каждые 1,5 сек.";
            startupState.Text = watchers.Count == 0
                ? "Автозагрузка: папки запуска не удалось открыть."
                : "Автозагрузка: наблюдаются папки запуска — " + watchers.Count + ".";
            registryState.Text = "Реестр: контроль ключевых параметров включён.";

            scanTimer = new Timer { Interval = 1500 };
            scanTimer.Tick += ScanTimerTick;
            scanTimer.Start();
            Shown += delegate { ScanDefenderExclusions(); };
            animationTimer = new Timer { Interval = 30 };
            animationTimer.Tick += AnimateInterface;
            pageTransitionTimer = new Timer { Interval = 15 };
            pageTransitionTimer.Tick += AnimatePageTransition;
            pulseTimer = new Timer { Interval = 420 };
            pulseTimer.Tick += AnimatePulse;
            pulseTimer.Enabled = appSettings.Animations;
            if (appSettings.Animations)
            {
                foreach (Panel card in cardRevealProgress.Keys.ToArray())
                    cardRevealProgress[card] = 0F;
                animationTimer.Start();
            }
            else
            {
                foreach (Panel card in cardRevealProgress.Keys.ToArray())
                    cardRevealProgress[card] = 1F;
            }
            InitializeSoundPad();
            ApplyTheme();
            ApplyWindowHeaderStyle();
        }

        private Panel MakeCard(Color accentColor)
        {
            var card = new Panel
            {
                BackColor = Color.FromArgb(18, 26, 45),
                Margin = new Padding(8),
                BorderStyle = BorderStyle.None
            };
            card.Paint += delegate(object sender, PaintEventArgs args)
            {
                Rectangle bounds = card.ClientRectangle;
                bounds.Width -= 1;
                bounds.Height -= 1;
                float reveal;
                if (!cardRevealProgress.TryGetValue(card, out reveal))
                    reveal = 1F;
                using (var border = new Pen(TranslateThemeColor(Color.FromArgb(37, 49, 71),
                    SettingsStore.ResolveLightTheme(appSettings.Theme), false)))
                {
                    args.Graphics.DrawRectangle(border, bounds);
                    if (reveal > 0F)
                    {
                        using (var accent = new Pen(Color.FromArgb(
                            (int)(104 + 151 * reveal), accentColor), 2F))
                        {
                            int accentEnd = 1 + (int)((bounds.Width - 2) * reveal);
                            args.Graphics.DrawLine(accent, 1, 1, Math.Max(1, accentEnd), 1);
                        }
                    }
                }
            };
            cardRevealProgress[card] = 0F;
            return card;
        }

        private static AnimatedButton CreateNavigationButton(string text)
        {
            var button = MakeButton(text, Color.FromArgb(15, 22, 38));
            button.Height = 44;
            button.Width = 202;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(14, 0, 0, 0);
            button.Margin = new Padding(0);
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private Panel CreateHomePage()
        {
            var home = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                Padding = new Padding(26, 22, 26, 22)
            };
            var layout = new BufferedTableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 224F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var heading = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var headingTitle = new Label
            {
                Text = "Добро пожаловать в Моглокер",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold),
                ForeColor = Color.FromArgb(237, 244, 255),
                Location = new Point(4, 2)
            };
            var headingSubtitle = new Label
            {
                Text = "Локальные инструменты для мониторинга Windows и браузерной фильтрации.",
                AutoSize = true,
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(142, 160, 185),
                Location = new Point(7, 48)
            };
            heading.Controls.Add(headingTitle);
            heading.Controls.Add(headingSubtitle);
            layout.Controls.Add(heading, 0, 0);
            layout.SetColumnSpan(heading, 2);

            layout.Controls.Add(CreateHomeCard(
                "МОНИТОРИНГ WINDOWS",
                "Отслеживает некоторые подозрительные полноэкранные окна, изменения выбранных системных параметров и папок автозагрузки. События доступны в журнале; приложение не выполняет автоматическое удаление или завершение процессов.",
                Color.FromArgb(104, 211, 184)), 0, 1);
            layout.Controls.Add(CreateHomeCard(
                "БРАУЗЕРНЫЙ ADBLOCK",
                "Расширение для Edge и Chrome применяет сетевые и косметические фильтры. В приложении можно управлять списками правил, пользовательскими исключениями и статистикой блокировок.",
                Color.FromArgb(111, 164, 211)), 1, 1);
            var developersCard = CreateHomeCard(
                "РАЗРАБОТЧИКИ",
                "Iconamask",
                Color.FromArgb(163, 139, 211));
            layout.Controls.Add(developersCard, 0, 2);
            layout.SetColumnSpan(developersCard, 2);
            home.Controls.Add(layout);
            return home;
        }

        private Panel CreateSettingsPage()
        {
            var settings = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                Padding = new Padding(30, 26, 30, 24),
                AutoScroll = true
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 10,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));

            var title = new Label
            {
                Text = "Настройки",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold),
                ForeColor = Color.FromArgb(237, 244, 255),
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(title, 0, 0);
            layout.SetColumnSpan(title, 2);

            layout.Controls.Add(CreateSettingsLabel("Оформление"), 0, 1);
            themeSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 240,
                Anchor = AnchorStyles.Left,
                BackColor = Color.FromArgb(18, 26, 45),
                ForeColor = Color.FromArgb(226, 232, 244),
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(6, 0, 0, 0)
            };
            RefreshThemeOptions();
            themeSelector.SelectedIndexChanged += ThemeChanged;
            layout.Controls.Add(themeSelector, 1, 1);

            layout.Controls.Add(CreateSettingsLabel("Цветовые темы"), 0, 2);
            var themeActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 14, 0, 0),
                Margin = new Padding(0)
            };
            var createThemeButton = MakeButton("Создать свою тему", Color.FromArgb(41, 53, 76));
            createThemeButton.Width = 160;
            createThemeButton.Click += CreateCustomTheme;
            var deleteThemeButton = MakeButton("Удалить тему", Color.FromArgb(133, 67, 62));
            deleteThemeButton.Width = 130;
            deleteThemeButton.Click += DeleteCustomTheme;
            themeActions.Controls.Add(createThemeButton);
            themeActions.Controls.Add(deleteThemeButton);
            layout.Controls.Add(themeActions, 1, 2);

            layout.Controls.Add(CreateSettingsLabel("Стиль шапки приложения"), 0, 3);
            headerStyleSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 240,
                Anchor = AnchorStyles.Left,
                BackColor = Color.FromArgb(18, 26, 45),
                ForeColor = Color.FromArgb(226, 232, 244),
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(6, 0, 0, 0)
            };
            headerStyleSelector.Items.AddRange(new object[] { "Windows", "macOS" });
            headerStyleSelector.SelectedIndex = appSettings.HeaderStyle == "MacOS" ? 1 : 0;
            headerStyleSelector.SelectedIndexChanged += HeaderStyleChanged;
            layout.Controls.Add(headerStyleSelector, 1, 3);

            layout.Controls.Add(CreateSettingsLabel("Анимация интерфейса"), 0, 4);
            animationsCheckBox = new CheckBox
            {
                Text = "Включить анимации и заставку при запуске",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Checked = appSettings.Animations,
                ForeColor = Color.FromArgb(226, 232, 244),
                Font = new Font("Segoe UI", 9.5F),
                Margin = new Padding(6, 0, 0, 0)
            };
            animationsCheckBox.CheckedChanged += AnimationsChanged;
            layout.Controls.Add(animationsCheckBox, 1, 4);

            layout.Controls.Add(CreateSettingsLabel("Папка экспорта журнала"), 0, 5);
            var exportRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, 12, 0, 0)
            };
            exportDirectoryLabel = new Label
            {
                Text = GetExportDirectory(),
                Width = 320,
                Height = 34,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(183, 199, 222),
                Margin = new Padding(6, 0, 8, 0)
            };
            var folderButton = MakeButton("Выбрать папку", Color.FromArgb(41, 53, 76));
            folderButton.Width = 134;
            folderButton.Click += ChooseExportDirectory;
            exportRow.Controls.Add(exportDirectoryLabel);
            exportRow.Controls.Add(folderButton);
            layout.Controls.Add(exportRow, 1, 5);

            layout.Controls.Add(CreateSettingsLabel("Журнал"), 0, 6);
            var exportButton = MakeButton("Экспортировать журнал", Color.FromArgb(53, 105, 99));
            exportButton.Width = 190;
            exportButton.Anchor = AnchorStyles.Left;
            exportButton.Click += ExportReport;
            layout.Controls.Add(exportButton, 1, 6);

            layout.Controls.Add(CreateSettingsLabel("Звук в Discord Desktop"), 0, 7);
            var discordAudioButton = MakeButton("Настроить звук", Color.FromArgb(64, 89, 122));
            discordAudioButton.Width = 160;
            discordAudioButton.Anchor = AnchorStyles.Left;
            discordAudioButton.Click += ShowDiscordDesktopGuide;
            layout.Controls.Add(discordAudioButton, 1, 7);

            layout.Controls.Add(CreateSettingsLabel("Microsoft Defender"), 0, 8);
            var defenderButton = MakeButton("Исключения Defender", Color.FromArgb(53, 105, 99));
            defenderButton.Width = 180;
            defenderButton.Anchor = AnchorStyles.Left;
            defenderButton.Click += ShowDefenderExclusions;
            layout.Controls.Add(defenderButton, 1, 8);

            layout.Controls.Add(CreateSettingsLabel("Обновления"), 0, 9);
            var updateRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0, 10, 0, 0)
            };
            checkUpdatesButton = MakeButton("Проверить обновления", Color.FromArgb(64, 89, 122));
            checkUpdatesButton.Width = 180;
            updateStatusLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(500, 42),
                Text = "Источник: GitHub Releases",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(183, 199, 222),
                Margin = new Padding(8, 8, 0, 0)
            };
            checkUpdatesButton.Click += CheckForUpdates;
            updateRow.Controls.Add(checkUpdatesButton);
            updateRow.Controls.Add(updateStatusLabel);
            layout.Controls.Add(updateRow, 1, 9);

            settings.Controls.Add(layout);
            return settings;
        }

        private void ShowDefenderExclusions(object sender, EventArgs e)
        {
            using (var dialog = new DefenderExclusionsForm())
                dialog.ShowDialog(this);
            ScanDefenderExclusions();
        }

        private async void CheckForUpdates(object sender, EventArgs e)
        {
            checkUpdatesButton.Enabled = false;
            updateStatusLabel.Text = "Проверяю GitHub Releases…";
            try
            {
                GitHubRelease release = await Task.Run(() => GitHubUpdateService.CheckLatest());
                Version currentVersion = GetType().Assembly.GetName().Version;
                Version releaseVersion;
                string tag = release.TagName;
                string normalizedTag = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? tag.Substring(1)
                    : tag;

                if (!Version.TryParse(normalizedTag, out releaseVersion))
                {
                    updateStatusLabel.Text = "Найдена версия " + tag + " на GitHub.";
                    if (MessageBox.Show(this, "Доступен релиз " + tag +
                        ". Открыть страницу загрузки GitHub?",
                        "Обновление доступно", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information) == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true });
                }
                else if (releaseVersion > currentVersion)
                {
                    updateStatusLabel.Text = "Доступно обновление: " + release.TagName;
                    if (MessageBox.Show(this, "Доступно обновление " + release.TagName +
                        ". Открыть страницу загрузки GitHub?",
                        "Обновление доступно", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information) == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true });
                }
                else
                {
                    updateStatusLabel.Text = "Установлена последняя версия (" + currentVersion + ").";
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                bool noRelease = response != null && response.StatusCode == HttpStatusCode.NotFound;
                if (response != null)
                    response.Dispose();
                string message = noRelease
                    ? "GitHub не нашёл репозиторий или в нём ещё нет опубликованных Releases."
                    : "Не удалось проверить GitHub Releases: " + ex.Message;
                updateStatusLabel.Text = message;
                MessageBox.Show(this, message + "\r\n\r\n" + GitHubUpdateService.ReleasesUrl,
                    "Проверка обновлений", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                updateStatusLabel.Text = "Проверка обновлений завершилась ошибкой.";
                MessageBox.Show(this, "Не удалось проверить обновления:\r\n\r\n" + ex.Message,
                    "Проверка обновлений", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                checkUpdatesButton.Enabled = true;
            }
        }

        private void CheckRegeditProcesses()
        {
            var activeProcessIds = new HashSet<int>();
            foreach (Process process in Process.GetProcessesByName("regedit"))
            {
                using (process)
                {
                    activeProcessIds.Add(process.Id);
                    if (reportedRegeditProcesses.Add(process.Id))
                    {
                        AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Редактор реестра",
                            "Запущен regedit.exe. Проверьте, что изменение системных параметров ожидаемо.",
                            null, (uint)process.Id);
                    }
                }
            }
            reportedRegeditProcesses.IntersectWith(activeProcessIds);
        }

        private void ScanDefenderExclusions()
        {
            lastDefenderExclusionScan = DateTime.UtcNow;
            if (System.Threading.Interlocked.Exchange(ref defenderScanRunning, 1) != 0)
                return;

            Task.Run(delegate
            {
                try
                {
                    IList<DefenderExclusion> exclusions = DefenderExclusionManager.Read();
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new Action(delegate { ApplyDefenderExclusionSnapshot(exclusions); }));
                }
                catch (Exception ex)
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(new Action(delegate
                        {
                            AddAlertThrottled("defender-preference-read-error",
                                "ОШИБКА", "Исключения Microsoft Defender",
                                "Не удалось проверить исключения через PowerShell: " + ex.Message);
                        }));
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref defenderScanRunning, 0);
                }
            });
        }

        private void ApplyDefenderExclusionSnapshot(IList<DefenderExclusion> exclusions)
        {
            var current = exclusions
                .GroupBy(item => item.Kind + "\0" + item.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Kind + ": " + group.First().Value,
                    StringComparer.OrdinalIgnoreCase);
            if (defenderExclusionBaseline == null)
            {
                defenderExclusionBaseline = current;
                AddAlert("ИНФО", "Исключения Microsoft Defender",
                    "Снимок исключений получен через PowerShell; автоматическое изменение настроек выключено.");
                return;
            }

            foreach (KeyValuePair<string, string> entry in current)
            {
                if (!defenderExclusionBaseline.ContainsKey(entry.Key))
                    AddAlertThrottled("defender-exclusion-added:" + entry.Key,
                        "ПРЕДУПРЕЖДЕНИЕ", "Исключения Microsoft Defender",
                        "Обнаружено новое исключение. Источник изменения не установлен: " + entry.Value);
            }
            foreach (KeyValuePair<string, string> entry in defenderExclusionBaseline)
            {
                if (!current.ContainsKey(entry.Key))
                    AddAlertThrottled("defender-exclusion-removed:" + entry.Key,
                        "ИНФО", "Исключения Microsoft Defender",
                        "Обнаружено удаление исключения. Источник изменения не установлен: " + entry.Value);
            }
            defenderExclusionBaseline = current;
        }

        private void RefreshThemeOptions()
        {
            applyingTheme = true;
            themeSelector.Items.Clear();
            themeSelector.Items.AddRange(new object[] { "Тёмная", "Светлая", "Системная" });
            if (appSettings.CustomThemes == null)
                appSettings.CustomThemes = new List<CustomTheme>();
            foreach (CustomTheme theme in appSettings.CustomThemes)
                themeSelector.Items.Add(theme.Name);

            int selectedIndex = appSettings.Theme == "Light" ? 1 :
                appSettings.Theme == "System" ? 2 : 0;
            if (appSettings.Theme == "Custom")
            {
                int customIndex = appSettings.CustomThemes.FindIndex(theme =>
                    String.Equals(theme.Name, appSettings.CustomThemeName, StringComparison.Ordinal));
                if (customIndex >= 0)
                    selectedIndex = customIndex + 3;
                else
                    appSettings.Theme = "Dark";
            }
            themeSelector.SelectedIndex = selectedIndex;
            applyingTheme = false;
        }

        private void CreateCustomTheme(object sender, EventArgs e)
        {
            using (var editor = new ThemeEditorDialog())
            {
                if (editor.ShowDialog(this) != DialogResult.OK)
                    return;
                string name = editor.ThemeName.Trim();
                if (String.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(this, "Введите название темы.", "Своя тема",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (appSettings.CustomThemes.Any(existingTheme =>
                    String.Equals(existingTheme.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this, "Тема с таким названием уже существует.", "Своя тема",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var theme = new CustomTheme
                {
                    Name = name,
                    Background = ColorTranslator.ToHtml(editor.BackgroundColor),
                    Surface = ColorTranslator.ToHtml(editor.SurfaceColor),
                    Text = ColorTranslator.ToHtml(editor.TextColor),
                    Accent = ColorTranslator.ToHtml(editor.AccentColor)
                };
                appSettings.CustomThemes.Add(theme);
                appSettings.Theme = "Custom";
                appSettings.CustomThemeName = name;
                RefreshThemeOptions();
                PersistSettings();
                ApplyTheme();
            }
        }

        private void DeleteCustomTheme(object sender, EventArgs e)
        {
            if (themeSelector.SelectedIndex < 3)
            {
                MessageBox.Show(this, "Сначала выберите собственную тему.", "Удаление темы",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int themeIndex = themeSelector.SelectedIndex - 3;
            if (themeIndex < 0 || themeIndex >= appSettings.CustomThemes.Count)
                return;
            CustomTheme theme = appSettings.CustomThemes[themeIndex];
            if (MessageBox.Show(this, "Удалить тему «" + theme.Name + "»?",
                "Удаление темы", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            appSettings.CustomThemes.RemoveAt(themeIndex);
            appSettings.Theme = "Dark";
            appSettings.CustomThemeName = String.Empty;
            RefreshThemeOptions();
            PersistSettings();
            ApplyTheme();
        }

        private void HeaderStyleChanged(object sender, EventArgs e)
        {
            if (headerStyleSelector.SelectedIndex < 0)
                return;
            appSettings.HeaderStyle = headerStyleSelector.SelectedIndex == 1 ? "MacOS" : "Windows";
            PersistSettings();
            ApplyWindowHeaderStyle();
        }

        private Panel CreateMacTitleBar()
        {
            var bar = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 26, 45)
            };
            AddMacWindowButton(bar, 16, Color.FromArgb(0, 174, 239), delegate
            {
                WindowState = FormWindowState.Minimized;
            });
            AddMacWindowButton(bar, 40, Color.FromArgb(255, 189, 46), delegate
            {
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
            });
            AddMacWindowButton(bar, 64, Color.FromArgb(255, 95, 86), delegate
            {
                Close();
            });
            var title = new Label
            {
                Text = "Моглокер",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 199, 222),
                BackColor = Color.Transparent
            };
            bar.Controls.Add(title);
            bar.MouseDown += BeginWindowDrag;
            title.MouseDown += BeginWindowDrag;
            return bar;
        }

        private static void AddMacWindowButton(Panel bar, int left, Color color, Action action)
        {
            var dot = new Panel
            {
                Location = new Point(left, 11),
                Size = new Size(14, 14),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            dot.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (var brush = new SolidBrush(color))
                    args.Graphics.FillEllipse(brush, 0, 0, 13, 13);
            };
            dot.Click += delegate { action(); };
            bar.Controls.Add(dot);
            dot.BringToFront();
        }

        private void BeginWindowDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        private void ApplyWindowHeaderStyle()
        {
            bool macStyle = appSettings.HeaderStyle == "MacOS";
            FormBorderStyle = macStyle ? FormBorderStyle.None : FormBorderStyle.Sizable;
            rootLayout.RowStyles[0].Height = macStyle ? 36F : 0F;
            macTitleBar.Visible = macStyle;
            if (macStyle)
                rootLayout.RowStyles[0].SizeType = SizeType.Absolute;
            PerformLayout();
        }

        private static Label CreateSettingsLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 199, 222),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(2, 0, 0, 0)
            };
        }

        private string GetExportDirectory()
        {
            string directory = appSettings.ExportDirectory;
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return directory;
        }

        private void ChooseExportDirectory(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку для экспорта журнала";
                dialog.SelectedPath = GetExportDirectory();
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                appSettings.ExportDirectory = dialog.SelectedPath;
                PersistSettings();
                exportDirectoryLabel.Text = dialog.SelectedPath;
            }
        }

        private void ExportReport(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Экспорт журнала MogLocker";
                dialog.Filter = "Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*";
                dialog.DefaultExt = "txt";
                dialog.AddExtension = true;
                dialog.InitialDirectory = GetExportDirectory();
                dialog.FileName = "MogLocker-журнал-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    File.WriteAllText(dialog.FileName, BuildReportText(), new UTF8Encoding(false));
                    string directory = Path.GetDirectoryName(dialog.FileName);
                    if (!String.IsNullOrEmpty(directory))
                    {
                        appSettings.ExportDirectory = directory;
                        PersistSettings();
                        exportDirectoryLabel.Text = directory;
                    }
                    MessageBox.Show(this, "Журнал сохранён.", "Экспорт завершён",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (IOException ex)
                {
                    MessageBox.Show(this, "Не удалось сохранить журнал: " + ex.Message,
                        "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show(this, "Нет доступа к выбранной папке: " + ex.Message,
                        "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (SecurityException ex)
                {
                    MessageBox.Show(this, "Windows запретила запись файла: " + ex.Message,
                        "Ошибка экспорта", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ThemeChanged(object sender, EventArgs e)
        {
            if (applyingTheme || themeSelector.SelectedIndex < 0)
                return;
            if (themeSelector.SelectedIndex < 3)
            {
                appSettings.Theme = themeSelector.SelectedIndex == 1 ? "Light" :
                    themeSelector.SelectedIndex == 2 ? "System" : "Dark";
                appSettings.CustomThemeName = String.Empty;
            }
            else
            {
                int customIndex = themeSelector.SelectedIndex - 3;
                if (customIndex >= appSettings.CustomThemes.Count)
                    return;
                appSettings.Theme = "Custom";
                appSettings.CustomThemeName = appSettings.CustomThemes[customIndex].Name;
            }
            PersistSettings();
            ApplyTheme();
        }

        private Control GetPageControl(int page)
        {
            switch (page)
            {
                case 0: return homePanel;
                case 1: return adBlockPanel;
                case 2: return monitorPanel;
                case 3: return settingsPanel;
                default: return null;
            }
        }

        private void SetVisiblePage(int page)
        {
            homePanel.Visible = page == 0;
            adBlockPanel.Visible = page == 1;
            monitorPanel.Visible = page == 2;
            settingsPanel.Visible = page == 3;
        }

        private void AnimatePageTransition(object sender, EventArgs e)
        {
            if (transitioningPage == null || outgoingPage == null)
            {
                pageTransitionTimer.Stop();
                return;
            }

            float progress = Math.Min(1F, (float)(DateTime.UtcNow - transitionStartedAt)
                .TotalMilliseconds / PageTransitionDuration);
            float easedProgress = progress * progress * (3F - 2F * progress);
            transitioningPage.Size = pageHost.ClientSize;
            transitioningPage.Location = new Point(
                (int)Math.Round(transitionStartX * (1F - easedProgress)), 0);
            outgoingPage.Size = pageHost.ClientSize;
            outgoingPage.Location = new Point(
                (int)Math.Round(-transitionStartX * easedProgress), 0);
            if (progress >= 1F)
                CompletePageTransition();
        }

        private void CompletePageTransition()
        {
            pageTransitionTimer.Stop();
            if (transitioningPage == null)
                return;
            if (outgoingPage != null)
            {
                outgoingPage.Visible = false;
                outgoingPage.Dock = DockStyle.Fill;
                outgoingPage.Location = Point.Empty;
            }
            transitioningPage.Dock = DockStyle.Fill;
            transitioningPage.Location = Point.Empty;
            transitioningPage.BringToFront();
            transitioningPage = null;
            outgoingPage = null;
        }

        private void AnimationsChanged(object sender, EventArgs e)
        {
            appSettings.Animations = animationsCheckBox.Checked;
            AnimatedButton.AnimationsEnabled = appSettings.Animations;
            PersistSettings();
            pulseTimer.Enabled = appSettings.Animations && isMonitoring;
            if (appSettings.Animations)
            {
                foreach (Panel card in cardRevealProgress.Keys.ToArray())
                    cardRevealProgress[card] = 0F;
                animationTimer.Start();
            }
            else
            {
                CompletePageTransition();
                animationTimer.Stop();
                foreach (Panel card in cardRevealProgress.Keys.ToArray())
                {
                    cardRevealProgress[card] = 1F;
                    card.Invalidate();
                }
                pulseDot.ForeColor = monitorState.ForeColor;
            }
        }

        private void ApplyTheme()
        {
            CaptureOriginalTheme(this);
            RestoreOriginalTheme(this);
            bool light = SettingsStore.ResolveLightTheme(appSettings.Theme);
            activeCustomTheme = null;
            if (appSettings.Theme == "Custom" && appSettings.CustomThemes != null)
                activeCustomTheme = appSettings.CustomThemes.FirstOrDefault(theme =>
                    String.Equals(theme.Name, appSettings.CustomThemeName, StringComparison.Ordinal));
            ApplyThemeToControl(this, light);
            ApplyAlertSeverityStyles(light);
            SetActiveNavigation(selectedPage);
            macTitleBar.Invalidate();
        }

        private void ApplyAlertSeverityStyles(bool light)
        {
            foreach (DataGridViewRow row in alertsGrid.Rows)
            {
                AlertRecord record = row.Tag as AlertRecord;
                if (record == null)
                    continue;
                if (record.Severity == "ПОДОЗРИТЕЛЬНО" ||
                    record.Severity == "ПРЕДУПРЕЖДЕНИЕ" ||
                    record.Severity == "ОШИБКА")
                {
                    row.Cells[1].Style.ForeColor = light
                        ? Color.FromArgb(153, 79, 28)
                        : Color.FromArgb(255, 190, 105);
                    row.Cells[1].Style.BackColor = light
                        ? Color.FromArgb(250, 231, 219)
                        : Color.FromArgb(69, 48, 39);
                }
                else if (record.Severity == "ИНФО")
                {
                    row.Cells[1].Style.ForeColor = light
                        ? Color.FromArgb(32, 120, 102)
                        : Color.FromArgb(121, 211, 190);
                    if (activeCustomTheme != null)
                        row.Cells[1].Style.ForeColor =
                            ColorTranslator.FromHtml(activeCustomTheme.Accent);
                }
            }
        }

        private void CaptureOriginalTheme(Control control)
        {
            if (!originalControlColors.ContainsKey(control))
                originalControlColors.Add(control, new ControlThemeState(control));
            DataGridView grid = control as DataGridView;
            if (grid != null)
            {
                CaptureCellTheme(grid.DefaultCellStyle);
                CaptureCellTheme(grid.AlternatingRowsDefaultCellStyle);
                CaptureCellTheme(grid.ColumnHeadersDefaultCellStyle);
                foreach (DataGridViewColumn column in grid.Columns)
                    CaptureCellTheme(column.DefaultCellStyle);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    CaptureCellTheme(row.DefaultCellStyle);
                    foreach (DataGridViewCell cell in row.Cells)
                        CaptureCellTheme(cell.Style);
                }
            }
            foreach (Control child in control.Controls)
                CaptureOriginalTheme(child);
        }

        private void CaptureCellTheme(DataGridViewCellStyle style)
        {
            if (style != null && !originalCellColors.ContainsKey(style))
                originalCellColors.Add(style, new CellThemeState(style));
        }

        private void RestoreOriginalTheme(Control control)
        {
            ControlThemeState state;
            if (originalControlColors.TryGetValue(control, out state))
                state.Restore(control);
            DataGridView grid = control as DataGridView;
            if (grid != null)
            {
                RestoreCellTheme(grid.DefaultCellStyle);
                RestoreCellTheme(grid.AlternatingRowsDefaultCellStyle);
                RestoreCellTheme(grid.ColumnHeadersDefaultCellStyle);
                foreach (DataGridViewColumn column in grid.Columns)
                    RestoreCellTheme(column.DefaultCellStyle);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    RestoreCellTheme(row.DefaultCellStyle);
                    foreach (DataGridViewCell cell in row.Cells)
                        RestoreCellTheme(cell.Style);
                }
            }
            foreach (Control child in control.Controls)
                RestoreOriginalTheme(child);
        }

        private void RestoreCellTheme(DataGridViewCellStyle style)
        {
            CellThemeState state;
            if (style != null && originalCellColors.TryGetValue(style, out state))
                state.Restore(style);
        }

        private bool PersistSettings()
        {
            try
            {
                SettingsStore.Save(appSettings);
                return true;
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Не удалось сохранить настройки: " + ex.Message,
                    "Ошибка настроек", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, "Нет доступа для сохранения настроек: " + ex.Message,
                    "Ошибка настроек", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (SecurityException ex)
            {
                MessageBox.Show(this, "Windows запретила сохранение настроек: " + ex.Message,
                    "Ошибка настроек", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        internal static void ApplyThemeToControl(Control control, bool light)
        {
            control.BackColor = TranslateThemeColor(control.BackColor, light, false);
            control.ForeColor = TranslateThemeColor(control.ForeColor, light, true);

            var button = control as Button;
            if (button != null)
            {
                button.FlatAppearance.BorderColor =
                    TranslateThemeColor(button.FlatAppearance.BorderColor, light, false);
                button.FlatAppearance.MouseOverBackColor =
                    TranslateThemeColor(button.FlatAppearance.MouseOverBackColor, light, false);
                button.FlatAppearance.MouseDownBackColor =
                    TranslateThemeColor(button.FlatAppearance.MouseDownBackColor, light, false);
            }
            var grid = control as DataGridView;
            if (grid != null)
            {
                TranslateCellStyle(grid.DefaultCellStyle, light);
                TranslateCellStyle(grid.AlternatingRowsDefaultCellStyle, light);
                TranslateCellStyle(grid.ColumnHeadersDefaultCellStyle, light);
                grid.BackgroundColor = TranslateThemeColor(grid.BackgroundColor, light, false);
                grid.GridColor = TranslateThemeColor(grid.GridColor, light, false);
                foreach (DataGridViewColumn column in grid.Columns)
                    TranslateCellStyle(column.DefaultCellStyle, light);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    TranslateCellStyle(row.DefaultCellStyle, light);
                    foreach (DataGridViewCell cell in row.Cells)
                        TranslateCellStyle(cell.Style, light);
                }
            }
            foreach (Control child in control.Controls)
                ApplyThemeToControl(child, light);
        }

        private static void TranslateCellStyle(DataGridViewCellStyle style, bool light)
        {
            style.BackColor = TranslateThemeColor(style.BackColor, light, false);
            style.ForeColor = TranslateThemeColor(style.ForeColor, light, true);
            style.SelectionBackColor = TranslateThemeColor(style.SelectionBackColor, light, false);
            style.SelectionForeColor = TranslateThemeColor(style.SelectionForeColor, light, true);
        }

        internal static Color TranslateThemeColor(Color color, bool light, bool foreground)
        {
            int value = color.ToArgb();
            if (activeCustomTheme != null)
            {
                Color custom = TranslateCustomThemeColor(color, foreground);
                if (!custom.IsEmpty)
                    return custom;
            }
            if (light && foreground && value == Color.White.ToArgb())
                return Color.FromArgb(31, 41, 55);
            if (!light && foreground && value == unchecked((int)0xFFE2E9F2))
                return Color.FromArgb(225, 233, 246);
            if (light)
            {
                switch (value)
                {
                    case unchecked((int)0xFF0C111F): return Color.FromArgb(246, 248, 251);
                    case unchecked((int)0xFF0A0F1C): return Color.FromArgb(245, 247, 250);
                    case unchecked((int)0xFF0E1524): return Color.FromArgb(244, 247, 250);
                    case unchecked((int)0xFF0F1626): return Color.FromArgb(239, 243, 248);
                    case unchecked((int)0xFF0F1627): return Color.FromArgb(237, 242, 247);
                    case unchecked((int)0xFF0F1726): return Color.FromArgb(239, 243, 248);
                    case unchecked((int)0xFF11192A): return Color.FromArgb(244, 247, 250);
                    case unchecked((int)0xFF121A2D): return Color.White;
                    case unchecked((int)0xFF182338): return Color.FromArgb(232, 237, 244);
                    case unchecked((int)0xFF192338): return Color.FromArgb(234, 239, 245);
                    case unchecked((int)0xFF233146): return Color.FromArgb(226, 233, 242);
                    case unchecked((int)0xFF132730): return Color.FromArgb(228, 243, 239);
                    case unchecked((int)0xFF29354C): return Color.FromArgb(210, 221, 233);
                    case unchecked((int)0xFF293D56): return Color.FromArgb(202, 224, 218);
                    case unchecked((int)0xFF294158): return Color.FromArgb(202, 224, 218);
                    case unchecked((int)0xFF252F47): return Color.FromArgb(214, 222, 233);
                    case unchecked((int)0xFF273449): return Color.FromArgb(215, 223, 234);
                    case unchecked((int)0xFF2B384F): return Color.FromArgb(214, 222, 233);
                    case unchecked((int)0xFF384C65): return Color.FromArgb(198, 210, 224);
                    case unchecked((int)0xFF394964): return Color.FromArgb(198, 210, 224);
                    case unchecked((int)0xFF2E4259): return Color.FromArgb(215, 224, 235);
                    case unchecked((int)0xFF34465E): return Color.FromArgb(198, 210, 224);
                    case unchecked((int)0xFF40597A): return Color.FromArgb(220, 232, 245);
                    case unchecked((int)0xFF435B7A): return Color.FromArgb(226, 235, 246);
                    case unchecked((int)0xFF453027): return Color.FromArgb(250, 231, 219);
                    case unchecked((int)0xFF853F3E): return Color.FromArgb(249, 226, 224);
                    case unchecked((int)0xFF356963): return Color.FromArgb(216, 239, 232);
                    case unchecked((int)0xFFE2E8F4): return Color.FromArgb(31, 41, 55);
                    case unchecked((int)0xFFE1E9F6): return Color.FromArgb(31, 41, 55);
                    case unchecked((int)0xFFDEE5F2): return Color.FromArgb(31, 41, 55);
                    case unchecked((int)0xFFD8E1F0): return Color.FromArgb(55, 65, 81);
                    case unchecked((int)0xFFE2E9F2): return Color.FromArgb(31, 41, 55);
                    case unchecked((int)0xFFEDF4FF): return Color.FromArgb(20, 30, 45);
                    case unchecked((int)0xFFC5D1E4): return Color.FromArgb(55, 65, 81);
                    case unchecked((int)0xFFB7C7DE): return Color.FromArgb(71, 85, 105);
                    case unchecked((int)0xFF9DB0CC): return Color.FromArgb(91, 105, 125);
                    case unchecked((int)0xFF8EA0B9): return Color.FromArgb(107, 120, 139);
                    case unchecked((int)0xFF8597B1): return Color.FromArgb(107, 120, 139);
                    case unchecked((int)0xFF8F9EB5): return Color.FromArgb(107, 120, 139);
                    case unchecked((int)0xFFA0AEC4): return Color.FromArgb(107, 120, 139);
                }
            }
            else
            {
                switch (value)
                {
                    case unchecked((int)0xFFF6F8FB): return Color.FromArgb(12, 17, 31);
                    case unchecked((int)0xFFF5F7FA): return Color.FromArgb(10, 15, 28);
                    case unchecked((int)0xFFF3F6F9): return Color.FromArgb(14, 21, 36);
                    case unchecked((int)0xFFEFF3F8): return Color.FromArgb(15, 22, 38);
                    case unchecked((int)0xFFEDF2F7): return Color.FromArgb(15, 22, 39);
                    case unchecked((int)0xFFF0F3F8): return Color.FromArgb(15, 23, 38);
                    case unchecked((int)0xFFF4F7FA): return Color.FromArgb(17, 25, 42);
                    case unchecked((int)0xFFE4F3EF): return Color.FromArgb(19, 39, 48);
                    case unchecked((int)0xFFFFFFFF): return Color.FromArgb(18, 26, 45);
                    case unchecked((int)0xFFE8EDF4): return Color.FromArgb(24, 35, 56);
                    case unchecked((int)0xFFEAEFF5): return Color.FromArgb(25, 35, 56);
                    case unchecked((int)0xFFD2DDE9): return Color.FromArgb(41, 53, 76);
                    case unchecked((int)0xFFCAE0DA): return Color.FromArgb(41, 65, 88);
                    case unchecked((int)0xFFD6DEE9): return Color.FromArgb(37, 49, 71);
                    case unchecked((int)0xFFD7DFEA): return Color.FromArgb(39, 52, 73);
                    case unchecked((int)0xFFC6D2E0): return Color.FromArgb(56, 76, 101);
                    case unchecked((int)0xFFD7E0EB): return Color.FromArgb(46, 66, 89);
                    case unchecked((int)0xFFDCE8F5): return Color.FromArgb(64, 89, 122);
                    case unchecked((int)0xFFE2EBF6): return Color.FromArgb(67, 91, 122);
                    case unchecked((int)0xFFFAE7DB): return Color.FromArgb(69, 48, 39);
                    case unchecked((int)0xFFF9E2E0): return Color.FromArgb(133, 67, 62);
                    case unchecked((int)0xFFD8EFE8): return Color.FromArgb(53, 105, 99);
                    case unchecked((int)0xFF1F2937): return Color.FromArgb(226, 232, 244);
                    case unchecked((int)0xFF141E2D): return Color.FromArgb(237, 244, 255);
                    case unchecked((int)0xFF475569): return Color.FromArgb(183, 199, 222);
                    case unchecked((int)0xFF5B697D): return Color.FromArgb(157, 176, 204);
                    case unchecked((int)0xFF6B788B): return Color.FromArgb(142, 160, 185);
                    case unchecked((int)0xFF374151): return Color.FromArgb(197, 209, 228);
                }
            }
            return color;
        }

        private static Color TranslateCustomThemeColor(Color color, bool foreground)
        {
            Color background = ColorTranslator.FromHtml(activeCustomTheme.Background);
            Color surface = ColorTranslator.FromHtml(activeCustomTheme.Surface);
            Color accent = ColorTranslator.FromHtml(activeCustomTheme.Accent);
            Color text = ColorTranslator.FromHtml(activeCustomTheme.Text);
            int value = color.ToArgb();
            if (value == Color.FromArgb(12, 17, 31).ToArgb() ||
                value == Color.FromArgb(10, 15, 28).ToArgb())
                return background;
            if (value == Color.FromArgb(15, 22, 38).ToArgb() ||
                value == Color.FromArgb(15, 23, 38).ToArgb() ||
                value == Color.FromArgb(14, 21, 36).ToArgb())
                return MixColors(background, surface, 0.45F);
            if (value == Color.FromArgb(18, 26, 45).ToArgb() ||
                value == Color.FromArgb(17, 25, 42).ToArgb() ||
                value == Color.FromArgb(24, 35, 56).ToArgb() ||
                value == Color.FromArgb(25, 35, 56).ToArgb())
                return surface;
            if (foreground && (value == Color.FromArgb(226, 232, 244).ToArgb() ||
                value == Color.FromArgb(225, 233, 246).ToArgb() ||
                value == Color.FromArgb(237, 244, 255).ToArgb() ||
                value == Color.FromArgb(197, 209, 228).ToArgb() ||
                value == Color.FromArgb(183, 199, 222).ToArgb() ||
                value == Color.FromArgb(157, 176, 204).ToArgb() ||
                value == Color.FromArgb(142, 160, 185).ToArgb() ||
                value == Color.FromArgb(133, 151, 177).ToArgb() ||
                value == Color.FromArgb(143, 158, 181).ToArgb() ||
                value == Color.FromArgb(160, 174, 196).ToArgb()))
                return text;
            if (value == Color.FromArgb(104, 211, 184).ToArgb() ||
                value == Color.FromArgb(121, 211, 190).ToArgb() ||
                value == Color.FromArgb(126, 224, 196).ToArgb())
                return accent;
            return Color.Empty;
        }

        private static Color MixColors(Color first, Color second, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                first.R + (int)((second.R - first.R) * amount),
                first.G + (int)((second.G - first.G) * amount),
                first.B + (int)((second.B - first.B) * amount));
        }

        private static Panel CreateHomeCard(string title, string description, Color accent)
        {
            var card = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 26, 45),
                Margin = new Padding(8),
                Padding = new Padding(20),
                BorderStyle = BorderStyle.FixedSingle
            };
            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 199, 222)
            };
            var descriptionLabel = new Label
            {
                Text = description,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(197, 209, 228),
                Padding = new Padding(0, 10, 0, 0)
            };
            card.Controls.Add(descriptionLabel);
            card.Controls.Add(titleLabel);
            card.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (var accentPen = new Pen(accent, 2F))
                    args.Graphics.DrawLine(accentPen, 1, 1, Math.Max(1, card.ClientSize.Width - 2), 1);
            };
            return card;
        }

        private void ToggleNavigation(object sender, EventArgs e)
        {
            menuExpanded = !menuExpanded;
            float sidebarWidth = menuExpanded ? 218F : 68F;
            bodyLayout.SuspendLayout();
            bodyLayout.ColumnStyles[0].Width = sidebarWidth;
            float buttonWidth = Math.Max(44F, sidebarWidth - 16F);
            SetControlWidth(homeNavButton, (int)buttonWidth);
            SetControlWidth(adBlockNavButton, (int)buttonWidth);
            SetControlWidth(monitorNavButton, (int)buttonWidth);
            SetControlWidth(settingsNavButton, (int)buttonWidth);
            SetControlWidth(menuToggleButton, (int)Math.Max(44F, sidebarWidth - 24F));
            SetNavigationLabels();
            bodyLayout.ResumeLayout(true);
            bodyLayout.PerformLayout();
            bodyLayout.Invalidate(true);
        }

        private void ShowPage(int page)
        {
            if (page < 0 || page > 3)
                return;
            if (transitioningPage != null)
                CompletePageTransition();
            if (page == selectedPage)
            {
                if (page == 1)
                    adBlockPanel.Initialize();
                SetActiveNavigation(page);
                return;
            }

            int previousPageIndex = selectedPage;
            Control previousPage = GetPageControl(previousPageIndex);
            selectedPage = page;
            if (page == 1)
                adBlockPanel.Initialize();
            SetActiveNavigation(page);

            if (!appSettings.Animations || previousPage == null ||
                pageHost.ClientSize.Width <= 0 || pageHost.ClientSize.Height <= 0)
            {
                SetVisiblePage(page);
                return;
            }

            Control nextPage = GetPageControl(page);
            previousPage.Dock = DockStyle.None;
            previousPage.Size = pageHost.ClientSize;
            previousPage.Location = Point.Empty;
            previousPage.Visible = true;
            nextPage.Dock = DockStyle.None;
            nextPage.Size = pageHost.ClientSize;
            transitionStartX = page > previousPageIndex
                ? pageHost.ClientSize.Width
                : -pageHost.ClientSize.Width;
            nextPage.Location = new Point(transitionStartX, 0);
            nextPage.Visible = true;
            nextPage.BringToFront();
            transitioningPage = nextPage;
            outgoingPage = previousPage;
            transitionStartedAt = DateTime.UtcNow;
            pageTransitionTimer.Start();
        }

        private void SetActiveNavigation(int page)
        {
            Button[] buttons = { homeNavButton, adBlockNavButton, monitorNavButton, settingsNavButton };
            bool lightTheme = SettingsStore.ResolveLightTheme(appSettings.Theme);
            for (int index = 0; index < buttons.Length; index++)
            {
                bool active = index == page;
                buttons[index].BackColor = TranslateThemeColor(active
                    ? Color.FromArgb(19, 39, 48)
                    : Color.FromArgb(15, 22, 38), lightTheme, false);
                buttons[index].ForeColor = TranslateThemeColor(active
                    ? Color.FromArgb(126, 224, 196)
                    : Color.FromArgb(143, 158, 181), lightTheme, true);
            }
            navigationIndicator.Top = buttons[page].Top;
            navigationIndicator.BringToFront();
        }

        private static Label MakeSectionTitle(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 199, 222)
            };
        }

        private static Label MakeStatusLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(216, 225, 240),
                Margin = new Padding(0, 5, 0, 5)
            };
        }

        private static AnimatedButton MakeButton(string text, Color color)
        {
            var button = new AnimatedButton
            {
                Text = text,
                Width = 100,
                Height = 34,
                BackColor = color,
                ForeColor = Color.FromArgb(225, 233, 246),
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(0, 3, 7, 3),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(57, 73, 100);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                Math.Min(color.R + 14, 255),
                Math.Min(color.G + 14, 255),
                Math.Min(color.B + 14, 255));
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(
                Math.Max(color.R - 12, 0),
                Math.Max(color.G - 12, 0),
                Math.Max(color.B - 12, 0));
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private void ScanTimerTick(object sender, EventArgs e)
        {
            ScanFullscreenWindows();
            PurgeExpiredSignals();
            if ((DateTime.UtcNow - lastSystemStateScan).TotalSeconds >= 5)
            {
                CheckSystemState();
                CheckRegeditProcesses();
                lastSystemStateScan = DateTime.UtcNow;
            }
            if ((DateTime.UtcNow - lastRegistryScan).TotalSeconds >= 6)
            {
                CheckRegistryChanges();
                lastRegistryScan = DateTime.UtcNow;
            }
            if ((DateTime.UtcNow - lastDefenderExclusionScan).TotalSeconds >= 30)
                ScanDefenderExclusions();
        }

        private void PurgeExpiredSignals()
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-120);
            lock (recentSignalsLock)
            {
                foreach (string key in recentSignals.Keys
                    .Where(k => !k.StartsWith("window:", StringComparison.Ordinal)
                        && recentSignals[k] < cutoff)
                    .ToArray())
                    recentSignals.Remove(key);
            }
        }

        private void AnimateInterface(object sender, EventArgs e)
        {
            bool cardAnimating = false;
            foreach (Panel card in cardRevealProgress.Keys.ToArray())
            {
                float progress = cardRevealProgress[card];
                if (progress >= 1F)
                    continue;
                cardRevealProgress[card] = Math.Min(1F, progress + 0.045F);
                card.Invalidate();
                cardAnimating = true;
            }

            if (!cardAnimating)
                animationTimer.Stop();
        }

        private void AnimatePulse(object sender, EventArgs e)
        {
            if (!isMonitoring)
                return;
            pulseStep = (pulseStep + 1) % 8;
            double pulse = (Math.Sin(pulseStep * Math.PI / 4.0) + 1.0) / 2.0;
            pulseDot.ForeColor = Color.FromArgb(
                76 + (int)(28 * pulse), 174 + (int)(42 * pulse), 151 + (int)(34 * pulse));
        }

        private static void SetControlWidth(Control control, int width)
        {
            if (control.Width != width)
                control.Width = width;
        }

        private void SetNavigationLabels()
        {
            SetControlText(menuToggleButton, menuExpanded ? "☰   МЕНЮ" : "☰");
            menuToggleButton.TextAlign = menuExpanded
                ? ContentAlignment.MiddleLeft
                : ContentAlignment.MiddleCenter;
            SetControlText(homeNavButton, menuExpanded ? "⌂   Главная" : "⌂");
            SetControlText(adBlockNavButton, menuExpanded ? "◉   AdBlock" : "◉");
            SetControlText(monitorNavButton, menuExpanded ? "◈   Мониторинг" : "◈");
            SetControlText(settingsNavButton, menuExpanded ? "⚙   Настройки" : "⚙");
        }

        private static void SetControlText(Control control, string text)
        {
            if (!String.Equals(control.Text, text, StringComparison.Ordinal))
                control.Text = text;
        }

        private void ScanFullscreenWindows()
        {
            var windows = new List<WindowInfo>();
            Screen[] screens = Screen.AllScreens;
            int currentProcessId;
            using (Process currentProcess = Process.GetCurrentProcess())
                currentProcessId = currentProcess.Id;
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr unused)
            {
                if (!NativeMethods.IsWindowVisible(handle))
                    return true;

                uint processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                if (processId == 0 || processId == (uint)currentProcessId)
                    return true;

                NativeMethods.Rect rect;
                if (!NativeMethods.GetWindowRect(handle, out rect))
                    return true;
                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (!CoversMostOfAnyScreen(bounds, screens))
                    return true;

                var titleText = new StringBuilder(512);
                NativeMethods.GetWindowText(handle, titleText, titleText.Capacity);
                int extendedStyle = NativeMethods.GetWindowLong32(handle, GwlExStyle);
                bool hasCloseCommand = HasEnabledCloseCommand(handle);

                var info = new WindowInfo
                {
                    Handle = handle,
                    ProcessId = processId,
                    Title = titleText.ToString().Trim(),
                    IsTopmost = (extendedStyle & WsExTopmost) != 0,
                    Bounds = bounds
                };
                ReadProcessInfo(info);
                if (IsSystemProcess(info.ProcessName, info.ProcessPath))
                    return true;
                info.IsBrowser = IsBrowserProcess(info.ProcessName);
                if (info.IsBrowser && (NativeMethods.GetWindowLong32(handle, GwlStyle) & WsCaption) != 0)
                    return true;
                if (hasCloseCommand && !info.IsBrowser)
                    return true;
                windows.Add(info);
                return true;
            }, IntPtr.Zero);

            if (windows.Count > 0)
            {
                SetLabelState(screenState,
                    "Полноэкранные окна для проверки — " + windows.Count,
                    Color.FromArgb(255, 190, 105));
                var activeWindowKeys = new HashSet<string>(windows.Select(
                    window => "window:" + window.ProcessId + ":" + window.Handle));
                lock (recentSignalsLock)
                {
                    foreach (string key in recentSignals.Keys
                        .Where(key => key.StartsWith("window:", StringComparison.Ordinal) &&
                            !activeWindowKeys.Contains(key)).ToArray())
                        recentSignals.Remove(key);
                }
                foreach (WindowInfo window in windows)
                {
                    string key = "window:" + window.ProcessId + ":" + window.Handle;
                    string details = window.IsBrowser
                        ? "Браузер занимает почти весь экран; это может быть обычный полноэкранный режим. Проверьте страницу вручную; содержимое вкладки не анализируется."
                        : "Полноэкранное окно без доступной команды закрытия";
                    if (!String.IsNullOrEmpty(window.Title))
                        details += "; заголовок: " + window.Title;
                    if (!String.IsNullOrEmpty(window.ProcessName))
                        details += "; процесс: " + window.ProcessName;
                    if (window.IsTopmost)
                        details += "; поверх остальных окон";
                    AddWindowAlertOnce(key, "ПРЕДУПРЕЖДЕНИЕ",
                        details, window.ProcessPath, window.ProcessId, window.Handle);
                }
            }
            else
            {
                lock (recentSignalsLock)
                {
                    foreach (string key in recentSignals.Keys
                        .Where(key => key.StartsWith("window:", StringComparison.Ordinal)).ToArray())
                        recentSignals.Remove(key);
                }
                SetLabelState(screenState, "Экранные окна: подозрительных признаков не найдено.",
                    Color.FromArgb(104, 211, 184));
            }
        }

        private static bool CoversMostOfAnyScreen(Rectangle window)
        {
            return CoversMostOfAnyScreen(window, Screen.AllScreens);
        }

        private static bool CoversMostOfAnyScreen(Rectangle window, Screen[] screens)
        {
            foreach (Screen screen in screens)
            {
                Rectangle bounds = screen.Bounds;
                Rectangle intersection = Rectangle.Intersect(window, bounds);
                if (intersection.Width >= bounds.Width * 0.90 &&
                    intersection.Height >= bounds.Height * 0.90)
                    return true;
            }
            return false;
        }

        private static bool HasEnabledCloseCommand(IntPtr handle)
        {
            if ((NativeMethods.GetWindowLong32(handle, GwlStyle) & WsSysMenu) == 0)
                return false;

            IntPtr menu = NativeMethods.GetSystemMenu(handle, false);
            if (menu == IntPtr.Zero)
                return false;

            uint state = NativeMethods.GetMenuState(menu, ScClose, MfByCommand);
            return state != 0xFFFFFFFF && (state & (MfDisabled | MfGrayed)) == 0;
        }

        private static bool IsBrowserProcess(string processName)
        {
            string[] browserNames =
            {
                "arc", "brave", "browser", "chrome", "chromium", "falkon", "firefox",
                "floorp", "iridium", "librewolf", "maxthon", "msedge", "opera", "palemoon",
                "slimjet", "thorium", "torbrowser", "ungoogled-chromium", "vivaldi",
                "waterfox", "yandexbrowser", "zen"
            };
            return browserNames.Contains(processName, StringComparer.OrdinalIgnoreCase);
        }

        private void AddWindowAlertOnce(
            string key,
            string severity,
            string details,
            string path,
            uint processId,
            IntPtr windowHandle)
        {
            lock (recentSignalsLock)
            {
                if (recentSignals.ContainsKey(key))
                    return;
                recentSignals[key] = DateTime.UtcNow;
            }
            AddAlert(severity, "Полноэкранное окно", details, path, processId, windowHandle);
        }

        private static void ReadProcessInfo(WindowInfo info)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)info.ProcessId))
                {
                    info.ProcessName = process.ProcessName;
                    try
                    {
                        info.ProcessPath = process.MainModule.FileName;
                    }
                    catch (Win32Exception)
                    {
                        info.ProcessPath = null;
                    }
                    catch (InvalidOperationException)
                    {
                        info.ProcessPath = null;
                    }
                }
            }
            catch (ArgumentException)
            {
                info.ProcessName = "(процесс уже завершён)";
            }
            catch (InvalidOperationException)
            {
                info.ProcessName = "(процесс недоступен)";
            }
        }

        private void CheckSystemState()
        {
            Process[] explorerProcesses = Process.GetProcessesByName("explorer");
            bool explorerRunning = explorerProcesses.Length > 0;
            foreach (Process process in explorerProcesses)
                process.Dispose();
            SetLabelState(explorerState,
                explorerRunning ? "Проводник: запущен" : "Проводник: не запущен",
                explorerRunning
                ? Color.FromArgb(104, 211, 184)
                : Color.FromArgb(255, 190, 105));
            if (explorerWasRunning.HasValue && explorerWasRunning.Value && !explorerRunning)
                AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Проводник Windows", "Процесс explorer.exe завершился.");
            explorerWasRunning = explorerRunning;

            bool networkAvailable = NetworkInterface.GetIsNetworkAvailable();
            SetLabelState(networkState,
                networkAvailable ? "Сеть: доступна" : "Сеть: соединение недоступно",
                networkAvailable
                ? Color.FromArgb(104, 211, 184)
                : Color.FromArgb(160, 174, 196));
            if (networkWasAvailable.HasValue && networkWasAvailable.Value != networkAvailable)
                AddAlert("ИНФО", "Сеть", networkAvailable
                    ? "Сетевое соединение появилось."
                    : "Сетевое соединение пропало.");
            networkWasAvailable = networkAvailable;
        }

        private static void SetLabelState(Label label, string text, Color color)
        {
            if (!String.Equals(label.Text, text, StringComparison.Ordinal))
                label.Text = text;
            if (label.ForeColor != color)
                label.ForeColor = color;
        }

        private void CheckRegistryChanges()
        {
            Dictionary<string, string> current = ReadRegistrySnapshot();
            foreach (KeyValuePair<string, string> entry in current)
            {
                string oldValue;
                if (!registryBaseline.TryGetValue(entry.Key, out oldValue))
                    AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Системный реестр", "Добавлен параметр: " + entry.Key);
                else if (!String.Equals(oldValue, entry.Value, StringComparison.Ordinal))
                    AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Системный реестр", "Изменён параметр: " + entry.Key);
            }
            foreach (string oldKey in registryBaseline.Keys)
            {
                if (!current.ContainsKey(oldKey))
                    AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Системный реестр", "Удалён или стал недоступен параметр: " + oldKey);
            }
            registryBaseline = current;
        }

        private static Dictionary<string, string> ReadRegistrySnapshot()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ReadRegistryValues(result, RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run");
            ReadRegistryValues(result, RegistryHive.LocalMachine, RegistryView.Registry64,
                @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM64\\Run");
            ReadRegistryValues(result, RegistryHive.LocalMachine, RegistryView.Registry32,
                @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM32\\Run");
            ReadRegistryValues(result, RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU\\RunOnce");
            ReadRegistryValues(result, RegistryHive.LocalMachine, RegistryView.Registry64,
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM64\\RunOnce");
            ReadRegistryValues(result, RegistryHive.LocalMachine, RegistryView.Registry32,
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM32\\RunOnce");
            ReadRegistryValues(result, RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Microsoft\Windows\CurrentVersion\RunServices", "HKCU\\RunServices");
            ReadRegistryValues(result, RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "HKLM64\\Winlogon",
                new[] { "Shell", "Userinit" });
            ReadRegistryValues(result, RegistryHive.LocalMachine, RegistryView.Registry32,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "HKLM32\\Winlogon",
                new[] { "Shell", "Userinit" });
            ReadRegistryValues(result, RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "HKCU\\Policies\\System",
                new[] { "DisableRegistryTools", "DisableTaskMgr" });
            ReadRegistryValues(result, RegistryHive.LocalMachine, RegistryView.Registry64,
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System", "HKLM64\\Policies\\System",
                new[] { "DisableRegistryTools", "DisableTaskMgr" });
            ReadRegistryValues(result, RegistryHive.CurrentUser, RegistryView.Default,
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "HKCU\\Policies\\Explorer",
                new[] { "NoRun" });
            return result;
        }

        private static void ReadRegistryValues(
            Dictionary<string, string> result,
            RegistryHive hive,
            RegistryView view,
            string subKeyPath,
            string displayPath,
            string[] selectedNames = null)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey key = baseKey.OpenSubKey(subKeyPath, false))
            {
                if (key == null)
                    return;
                string[] valueNames = selectedNames ?? key.GetValueNames();
                foreach (string valueName in valueNames)
                {
                    object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (value == null)
                        continue;
                    string fingerprint = RegistryValueToString(value);
                    result[displayPath + "\\" + valueName] = fingerprint;
                }
            }
        }

        private static string RegistryValueToString(object value)
        {
            string[] strings = value as string[];
            if (strings != null)
                return String.Join("|", strings);
            byte[] bytes = value as byte[];
            if (bytes != null)
                return BitConverter.ToString(bytes);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
        }

        private void ConfigureStartupWatchers()
        {
            string[] paths =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (String.IsNullOrEmpty(path) || !Directory.Exists(path))
                    continue;
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };
                watcher.Created += StartupFileChanged;
                watcher.Changed += StartupFileChanged;
                watcher.Deleted += StartupFileChanged;
                watcher.Renamed += StartupFileRenamed;
                watcher.Error += StartupWatcherError;
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
        }

        private void StartupFileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                lock (recentSignalsLock)
                    recentSignals.Remove("file:Created:" + e.FullPath);
            }

            AddAlertThrottled("file:" + e.ChangeType + ":" + e.FullPath, "ПРЕДУПРЕЖДЕНИЕ",
                "Автозагрузка", e.ChangeType + ": " + e.Name, e.FullPath);
        }

        private void StartupFileRenamed(object sender, RenamedEventArgs e)
        {
            AddAlertThrottled("rename:" + e.FullPath, "ПРЕДУПРЕЖДЕНИЕ", "Автозагрузка",
                "Переименован файл: " + e.OldName + " → " + e.Name, e.FullPath);
        }

        private void StartupWatcherError(object sender, ErrorEventArgs e)
        {
            Exception error = e.GetException();
            AddAlert("ОШИБКА", "Наблюдение автозагрузки",
                error == null
                    ? "Наблюдатель файловой системы завершил работу. Попытка перезапуска."
                    : "Наблюдатель файловой системы: " + error.Message + ". Попытка перезапуска.");

            var watcher = sender as FileSystemWatcher;
            if (watcher == null || !isMonitoring)
                return;
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.EnableRaisingEvents = true;
                AddAlert("ИНФО", "Наблюдение автозагрузки", "Наблюдатель перезапущен.");
            }
            catch (Exception ex)
            {
                AddAlert("ОШИБКА", "Наблюдение автозагрузки",
                    "Не удалось перезапустить наблюдатель: " + ex.Message);
            }
        }

        private void AddAlertThrottled(
            string key,
            string severity,
            string source,
            string details,
            string path = null,
            uint processId = 0,
            IntPtr windowHandle = default(IntPtr))
        {
            if (key.StartsWith("file:Deleted:", StringComparison.OrdinalIgnoreCase))
            {
                lock (recentSignalsLock)
                    recentSignals.Remove("file:Created:" + path);
            }

            DateTime now = DateTime.UtcNow;
            lock (recentSignalsLock)
            {
                DateTime previous;
                if (recentSignals.TryGetValue(key, out previous) && (now - previous).TotalSeconds < 60)
                    return;
                recentSignals[key] = now;
            }
            AddAlert(severity, source, details, path, processId, windowHandle);
        }

        private void AddAlert(
            string severity,
            string source,
            string details,
            string path = null,
            uint processId = 0,
            IntPtr windowHandle = default(IntPtr))
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, string, string, string, uint, IntPtr>(AddAlert),
                    severity, source, details, path, processId, windowHandle);
                return;
            }

            var record = new AlertRecord
            {
                Time = DateTime.Now,
                Severity = severity,
                Source = source,
                Details = details,
                Path = path,
                ProcessId = processId,
                WindowHandle = windowHandle
            };
            records.Add(record);
            int rowIndex = alertsGrid.Rows.Add(record.Time.ToString("HH:mm:ss"), severity, source, details);
            alertsGrid.Rows[rowIndex].Tag = record;
            ApplyAlertSeverityStyles(SettingsStore.ResolveLightTheme(appSettings.Theme));
            if (alertsGrid.IsHandleCreated && alertsGrid.Visible &&
                alertsGrid.DisplayedRowCount(false) > 0)
            {
                try
                {
                    alertsGrid.FirstDisplayedScrollingRowIndex = rowIndex;
                }
                catch (InvalidOperationException)
                {
                    // The journal can receive startup events before its tab is first displayed.
                }
            }
            while (alertsGrid.Rows.Count > 500)
            {
                alertsGrid.Rows.RemoveAt(0);
                records.RemoveAt(0);
            }
            if (currentFilter != null)
                ApplyLogFilter(null, EventArgs.Empty);
            else
                logCountLabel.Text = "Событий: " + alertsGrid.Rows.Count;

            if (severity == "ПОДОЗРИТЕЛЬНО")
            {
                if (WindowState == FormWindowState.Minimized && trayIcon != null)
                {
                    trayIcon.ShowBalloonTip(4000, "⚠ Моглокер",
                        "Обнаружено подозрительное окно: " + source, ToolTipIcon.Warning);
                }
            }
            else if (severity == "ПРЕДУПРЕЖДЕНИЕ" && !records.Any(r =>
                String.Equals(r.Severity, severity, StringComparison.Ordinal) &&
                String.Equals(r.Source, source, StringComparison.Ordinal) &&
                r.Time >= DateTime.Now.AddMinutes(-1)))
            {
                if (WindowState == FormWindowState.Minimized && trayIcon != null)
                {
                    trayIcon.ShowBalloonTip(4000, "⚠ Моглокер",
                        "Обнаружено предупреждение: " + source, ToolTipIcon.Warning);
                }
            }
        }

        private void ApplyLogFilter(object sender, EventArgs e)
        {
            if (logFilterSelector == null)
                return;

            currentFilter = logFilterSelector.SelectedIndex <= 0
                ? null
                : logFilterSelector.SelectedItem as string;

            int visibleCount = 0;
            foreach (DataGridViewRow row in alertsGrid.Rows)
            {
                bool shouldShow = currentFilter == null ||
                    String.Equals(row.Cells[1].Value as string, currentFilter, StringComparison.Ordinal);
                row.Visible = shouldShow;
                if (shouldShow)
                    visibleCount++;
            }
            logCountLabel.Text = "Событий: " + visibleCount;
        }

        private void ToggleMonitoring(object sender, EventArgs e)
        {
            isMonitoring = !isMonitoring;
            scanTimer.Enabled = isMonitoring;
            pulseTimer.Enabled = appSettings.Animations && isMonitoring;
            foreach (FileSystemWatcher watcher in watchers)
                watcher.EnableRaisingEvents = isMonitoring;
            pauseButton.Text = isMonitoring ? "Пауза" : "Продолжить";
            monitorState.Text = isMonitoring ? "МОНИТОРИНГ ВКЛЮЧЁН" : "МОНИТОРИНГ ПРИОСТАНОВЛЕН";
            monitorState.ForeColor = isMonitoring
                ? Color.FromArgb(104, 211, 184)
                : Color.FromArgb(255, 190, 105);
            pulseDot.ForeColor = monitorState.ForeColor;
            AddAlert("ИНФО", "Монитор", isMonitoring
                ? "Наблюдение возобновлено."
                : "Наблюдение приостановлено пользователем.");
        }

        private void ShowSelectedDetails(object sender, EventArgs e)
        {
            if (alertsGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "Выберите событие в журнале.", "Сведения",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var record = alertsGrid.SelectedRows[0].Tag as AlertRecord;
            if (record == null)
                return;
            string message = record.Time.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                record.Severity + " — " + record.Source + Environment.NewLine + Environment.NewLine +
                record.Details + Environment.NewLine;
            if (!String.IsNullOrEmpty(record.Path))
                message += Environment.NewLine + "Путь: " + record.Path;
            message += Environment.NewLine + Environment.NewLine +
                "Это эвристическое предупреждение, а не подтверждение заражения. Завершение доступно только отдельно, после проверки и подтверждения.";
            MessageBox.Show(this, message, "Сведения о событии",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void TerminateSelectedProcess(object sender, EventArgs e)
        {
            if (alertsGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "Сначала выберите в журнале событие о подозрительном полноэкранном окне.",
                    "Завершение программы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var record = alertsGrid.SelectedRows[0].Tag as AlertRecord;
            if (record == null || record.ProcessId == 0 || record.WindowHandle == IntPtr.Zero)
            {
                MessageBox.Show(this, "У выбранного события нет процесса, который можно безопасно проверить.",
                    "Завершение программы", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (String.IsNullOrEmpty(record.Path))
            {
                MessageBox.Show(this, "У выбранного процесса неизвестен путь к исполняемому файлу. Завершение отменено.",
                    "Не удалось проверить процесс", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            uint actualProcessId;
            if (!NativeMethods.IsWindowVisible(record.WindowHandle) ||
                NativeMethods.GetWindowThreadProcessId(record.WindowHandle, out actualProcessId) == 0 ||
                actualProcessId != record.ProcessId)
            {
                MessageBox.Show(this, "Это окно уже закрыто или изменило владельца. Обновите журнал и выберите актуальное событие.",
                    "Окно больше не доступно", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool isBrowser = IsBrowserProcess(Path.GetFileNameWithoutExtension(record.Path));
            NativeMethods.Rect rect;
            if (!NativeMethods.GetWindowRect(record.WindowHandle, out rect) ||
                !CoversMostOfAnyScreen(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)) ||
                (isBrowser && (NativeMethods.GetWindowLong32(record.WindowHandle, GwlStyle) & WsCaption) != 0) ||
                (HasEnabledCloseCommand(record.WindowHandle) && !isBrowser))
            {
                MessageBox.Show(this, "Выбранное окно уже не соответствует признакам блокирующего окна. Завершение отменено.",
                    "Окно изменилось", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                using (Process process = Process.GetProcessById((int)record.ProcessId))
                {
                    if (process.Id == Process.GetCurrentProcess().Id)
                        throw new InvalidOperationException("Нельзя завершить сам Моглокер.");

                    string processPath;
                    try
                    {
                        processPath = process.MainModule.FileName;
                    }
                    catch (Win32Exception)
                    {
                        processPath = null;
                    }
                    catch (InvalidOperationException)
                    {
                        processPath = null;
                    }
                    catch (SecurityException)
                    {
                        processPath = null;
                    }

                    if (String.IsNullOrEmpty(processPath) ||
                        !String.Equals(Path.GetFullPath(processPath), Path.GetFullPath(record.Path ?? String.Empty),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(this, "Не удалось подтвердить путь к исходной программе. Завершение отменено.",
                            "Не удалось проверить процесс", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (IsProtectedProcess(process, processPath))
                    {
                        MessageBox.Show(this, "Этот процесс защищён от завершения, чтобы не повредить Windows или её оболочку.",
                            "Системный процесс", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    isBrowser = IsBrowserProcess(process.ProcessName);
                    string prompt = "Завершить только выбранный процесс?\r\n\r\n" +
                        "Программа: " + process.ProcessName + "\r\n" +
                        "PID: " + process.Id + "\r\n" +
                        "Файл: " + processPath + "\r\n\r\n" +
                        (isBrowser
                            ? "Браузер может закрыть вкладки и потерять несохранённые данные. Другие процессы браузера могут остаться запущенными."
                            : "Несохранённые данные этой программы могут быть потеряны. Другие процессы затронуты не будут.");
                    if (MessageBox.Show(this, prompt, "Подтвердите завершение",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        return;

                    NativeMethods.GetWindowThreadProcessId(record.WindowHandle, out actualProcessId);
                    bool stillFullscreen = NativeMethods.GetWindowRect(record.WindowHandle, out rect) &&
                        CoversMostOfAnyScreen(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                    if (!NativeMethods.IsWindowVisible(record.WindowHandle) ||
                        actualProcessId != record.ProcessId || process.HasExited ||
                        !stillFullscreen ||
                        (isBrowser && (NativeMethods.GetWindowLong32(record.WindowHandle, GwlStyle) & WsCaption) != 0) ||
                        (HasEnabledCloseCommand(record.WindowHandle) && !isBrowser))
                    {
                        MessageBox.Show(this, "Окно или процесс изменились после подтверждения. Ничего не завершено.",
                            "Процесс изменился", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    process.Kill();
                    if (!process.WaitForExit(5000) && !process.HasExited)
                    {
                        AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Завершение процесса",
                            "Windows приняла запрос на завершение, но процесс пока остаётся активным.",
                            processPath, record.ProcessId, record.WindowHandle);
                        return;
                    }

                    AddAlert("ИНФО", "Завершение процесса",
                        "По подтверждению пользователя завершён процесс " + process.ProcessName +
                        " (PID " + process.Id + ").", processPath);
                    MessageBox.Show(this, "Выбранный процесс завершён.", "Готово",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (ArgumentException)
            {
                MessageBox.Show(this, "Процесс уже завершился.", "Процесс не найден",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Win32Exception ex)
            {
                MessageBox.Show(this, "Windows отказала в завершении процесса: " + ex.Message +
                    "\r\nПопробуйте закрыть его обычным способом или обратитесь к администратору.",
                    "Не удалось завершить процесс", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, "Процесс не удалось завершить: " + ex.Message,
                    "Не удалось завершить процесс", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool IsProtectedProcess(Process process, string processPath)
        {
            if (String.IsNullOrEmpty(processPath))
                return IsProtectedProcessName(process.ProcessName);

            if (IsProtectedProcessName(process.ProcessName) && IsWindowsPath(processPath))
                return true;

            return IsWindowsPath(processPath) && IsProtectedProcessName(process.ProcessName);
        }

        private static bool IsSystemProcess(string processName, string processPath)
        {
            if (String.IsNullOrEmpty(processPath))
                return true;

            if (IsWindowsPath(processPath) && IsProtectedProcessName(processName))
                return true;

            return false;
        }

        private static bool IsProtectedProcessName(string name)
        {
            string[] protectedNames =
            {
                "csrss", "dwm", "explorer", "lsass", "moglocker", "registry", "services",
                "smss", "system", "wininit", "winlogon", "svchost"
            };
            return protectedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsWindowsPath(string processPath)
        {
            if (String.IsNullOrEmpty(processPath))
                return false;

            string windowsDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(processPath);
            return fullPath.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string FindDefenderCommand()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string standardPath = Path.Combine(programFiles, "Windows Defender", "MpCmdRun.exe");
            if (File.Exists(standardPath))
                return standardPath;

            string platformDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Platform");
            if (!Directory.Exists(platformDirectory))
                return null;

            return Directory.GetDirectories(platformDirectory)
                .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path))
                .Select(path => Path.Combine(path, "MpCmdRun.exe"))
                .FirstOrDefault(File.Exists);
        }

        private static string BuildDefenderArguments(string filePath)
        {
            return "-Scan -ScanType 3 -File \"" + filePath + "\" -DisableRemediation";
        }

        private void ScanSelectedFileWithDefender(object sender, EventArgs e)
        {
            if (alertsGrid.SelectedRows.Count == 0)
            {
                MessageBox.Show(this, "Выберите событие о подозрительном стороннем полноэкранном окне.",
                    "Проверка Microsoft Defender", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var record = alertsGrid.SelectedRows[0].Tag as AlertRecord;
            if (record == null ||
                String.IsNullOrEmpty(record.Path) ||
                !File.Exists(record.Path) ||
                IsWindowsPath(record.Path))
            {
                MessageBox.Show(this, "Для выбранного события нет доступного стороннего файла для проверки.",
                    "Файл недоступен", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string prompt = "Запустить проверку этого файла Microsoft Defender?\r\n\r\n" +
                record.Path + "\r\n\r\n" +
                "Windows может запросить подтверждение администратора. Проверка не удаляет файл автоматически.";
            if (MessageBox.Show(this, prompt, "Подтвердите проверку",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            Process scanProcess = null;
            try
            {
                string defenderCommand = FindDefenderCommand();
                if (String.IsNullOrEmpty(defenderCommand))
                {
                    MessageBox.Show(this, "Не удалось найти MpCmdRun.exe — средство командной строки Microsoft Defender. " +
                        "Проверьте файл через приложение «Безопасность Windows» или установленный антивирус.",
                        "Microsoft Defender недоступен", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = defenderCommand,
                    Arguments = BuildDefenderArguments(record.Path),
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(defenderCommand)
                };
                scanProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                Process startedProcess = scanProcess;
                startedProcess.Exited += delegate
                {
                    int exitCode;
                    try
                    {
                        exitCode = startedProcess.ExitCode;
                    }
                    catch (InvalidOperationException)
                    {
                        exitCode = -1;
                    }
                    startedProcess.Dispose();

                    if (IsDisposed || !IsHandleCreated)
                        return;
                    BeginInvoke(new Action(delegate
                    {
                        AddAlert(exitCode == 0 ? "ИНФО" : "ПРЕДУПРЕЖДЕНИЕ",
                            "Проверка Microsoft Defender",
                            exitCode == 0
                                ? "Проверка завершена без ошибок. См. вывод Microsoft Defender; автоматическое исправление отключено."
                                : "Microsoft Defender вернул код " + exitCode +
                                  " (обнаружение или ошибка сканирования). См. вывод окна проверки; автоматическое исправление отключено.",
                            record.Path);
                    }));
                };

                if (!startedProcess.Start())
                    throw new InvalidOperationException("Windows не запустила Microsoft Defender.");

                AddAlert("ИНФО", "Проверка Microsoft Defender",
                    "Проверка файла запущена по запросу пользователя.", record.Path);
                scanProcess = null;
            }
            catch (Win32Exception ex)
            {
                string message = ex.NativeErrorCode == 1223
                    ? "Запрос контроля учётных записей был отменён. Проверка не запускалась."
                    : "Windows не смогла запустить Microsoft Defender: " + ex.Message;
                AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Проверка Microsoft Defender", message, record.Path);
                MessageBox.Show(this, message, "Не удалось запустить проверку",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (InvalidOperationException ex)
            {
                AddAlert("ОШИБКА", "Проверка Microsoft Defender", ex.Message, record.Path);
                MessageBox.Show(this, ex.Message, "Не удалось запустить проверку",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (IOException ex)
            {
                AddAlert("ОШИБКА", "Проверка Microsoft Defender", ex.Message, record.Path);
                MessageBox.Show(this, "Не удалось проверить файл или найти средство Microsoft Defender: " +
                    ex.Message, "Ошибка проверки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                AddAlert("ОШИБКА", "Проверка Microsoft Defender", ex.Message, record.Path);
                MessageBox.Show(this, "Недостаточно прав для проверки файла: " + ex.Message,
                    "Ошибка проверки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (SecurityException ex)
            {
                AddAlert("ОШИБКА", "Проверка Microsoft Defender", ex.Message, record.Path);
                MessageBox.Show(this, "Windows запретила доступ к файлу для проверки: " + ex.Message,
                    "Ошибка проверки", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (scanProcess != null)
                    scanProcess.Dispose();
            }
        }

        private void CopyReport(object sender, EventArgs e)
        {
            string report = BuildReport();
            try
            {
                Clipboard.SetText(report);
                MessageBox.Show(this, "Журнал скопирован в буфер обмена.", "Готово",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (ExternalException ex)
            {
                MessageBox.Show(this, "Не удалось скопировать журнал: " + ex.Message, "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveReport(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Сохранить журнал событий";
                dialog.Filter = "Текстовый файл|*.txt|Все файлы|*.*";
                dialog.FileName = "MogLocker-" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    string report = BuildReport();
                    File.WriteAllText(dialog.FileName, report, Encoding.UTF8);
                    AddAlert("ИНФО", "Журнал", "Журнал сохранён: " + dialog.FileName);
                    MessageBox.Show(this, "Журнал сохранён:\r\n" + dialog.FileName, "Готово",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось сохранить журнал: " + ex.Message,
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private string BuildReport()
        {
            var report = new StringBuilder();
            report.AppendLine("Моглокер — локальный журнал обнаружения");
            report.AppendLine("Автоматическая очистка не выполнялась.");
            foreach (AlertRecord record in records)
            {
                report.Append(record.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                report.Append(" | ");
                report.Append(record.Severity);
                report.Append(" | ");
                report.Append(record.Source);
                report.Append(" | ");
                report.AppendLine(record.Details);
                if (!String.IsNullOrEmpty(record.Path))
                    report.AppendLine("Путь: " + record.Path);
            }
            return report.ToString();
        }

        private string BuildReportText()
        {
            return BuildReport();
        }

        private void InitializeSoundPad()
        {
            soundPadEntries.Clear();
            var searchRoots = new List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..")),
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..")),
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..")),
                Environment.CurrentDirectory,
                Path.GetDirectoryName(Application.ExecutablePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "moglocker"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "moglocker")
            };

            foreach (string root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (String.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;

                var soundPadRoot = Path.Combine(root, "soundpad");
                if (File.Exists(Path.Combine(soundPadRoot, "index.html")))
                {
                    soundPadEntries.Add(new SoundPadEntry
                    {
                        Name = "soundpad",
                        Path = Path.Combine(soundPadRoot, "index.html")
                    });
                    break;
                }

                var candidate = Directory.EnumerateFiles(root, "index.html", SearchOption.AllDirectories)
                    .FirstOrDefault(file => Path.GetDirectoryName(file) != null &&
                        String.Equals(Path.GetFileName(Path.GetDirectoryName(file)), "soundpad", StringComparison.OrdinalIgnoreCase));
                if (!String.IsNullOrEmpty(candidate))
                {
                    soundPadEntries.Add(new SoundPadEntry
                    {
                        Name = "soundpad",
                        Path = candidate
                    });
                    break;
                }
            }

            if (soundPadEntries.Count == 0)
            {
                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundpad", "index.html");
                if (File.Exists(fallback))
                {
                    soundPadEntries.Add(new SoundPadEntry { Name = "soundpad", Path = fallback });
                }
            }
        }

        private void ShowSoundPadDialog()
        {
            if (soundPadDialog == null || soundPadDialog.IsDisposed)
            {
                soundPadDialog = new Form
                {
                    Text = "SoundPad",
                    StartPosition = FormStartPosition.CenterParent,
                    Width = 980,
                    Height = 660,
                    MinimumSize = new Size(820, 560),
                    Icon = Icon,
                    ShowInTaskbar = true,
                    FormBorderStyle = FormBorderStyle.Sizable,
                    MaximizeBox = true,
                    MinimizeBox = true
                };

                var browser = new WebBrowser
                {
                    Dock = DockStyle.Fill,
                    IsWebBrowserContextMenuEnabled = false,
                    ScriptErrorsSuppressed = true,
                    WebBrowserShortcutsEnabled = false
                };

                soundPadDialog.Controls.Add(browser);
                soundPadDialog.FormClosed += (s, args) =>
                {
                    browser.Dispose();
                };

                var firstEntry = soundPadEntries.FirstOrDefault();
                string page = firstEntry != null ? firstEntry.Path : "about:blank";
                if (!String.IsNullOrEmpty(page) && !String.Equals(page, "about:blank", StringComparison.OrdinalIgnoreCase) && File.Exists(page))
                    browser.Navigate(new Uri(page).AbsoluteUri);
                else if (!String.IsNullOrEmpty(page) && !String.Equals(page, "about:blank", StringComparison.OrdinalIgnoreCase))
                    browser.Navigate(page);
                else
                    browser.Navigate("about:blank");
            }

            if (soundPadDialog.WindowState == FormWindowState.Minimized)
                soundPadDialog.WindowState = FormWindowState.Normal;

            if (soundPadDialog.Visible)
                soundPadDialog.Activate();
            else
                soundPadDialog.Show(this);
        }

        private void OpenSoundPad(object sender, EventArgs e)
        {
            string page = null;
            var firstEntry = soundPadEntries.FirstOrDefault();
            if (firstEntry != null)
                page = firstEntry.Path;
            if (String.IsNullOrEmpty(page))
            {
                InitializeSoundPad();
                firstEntry = soundPadEntries.FirstOrDefault();
                if (firstEntry != null)
                    page = firstEntry.Path;
            }

            try
            {
                if (!String.IsNullOrEmpty(page) && File.Exists(page))
                {
                    var uri = new Uri(page).AbsoluteUri;
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    AddAlert("ИНФО", "SoundPad", "Локальный саундпад открыт в браузере по умолчанию.");
                    return;
                }
            }
            catch (Exception ex)
            {
                AddAlert("ПРЕДУПРЕЖДЕНИЕ", "SoundPad", "Не удалось открыть файл саундпада в браузере по умолчанию: " + ex.Message);
            }

            ShowSoundPadDialog();
        }

        private void ShowDiscordDesktopGuide(object sender, EventArgs e)
        {
            var guide = new Form
            {
                Text = "Саундпад в Discord Desktop",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(620, 490),
                BackColor = Color.FromArgb(18, 26, 45),
                ForeColor = Color.FromArgb(226, 232, 244),
                Font = new Font("Segoe UI", 9F)
            };

            var explanation = new Label
            {
                Location = new Point(22, 18),
                Size = new Size(576, 92),
                ForeColor = Color.FromArgb(226, 232, 244),
                Text = "Discord Desktop не принимает звук из браузерного саундпада автоматически. " +
                    "Нужен виртуальный аудиокабель: саундпад отправляет звук в его вход, " +
                    "а Discord использует соответствующий выход как микрофон."
            };

            var deviceStatus = new Label
            {
                Location = new Point(22, 116),
                Size = new Size(576, 56),
                ForeColor = Color.FromArgb(255, 190, 105),
                Text = String.Empty
            };

            var instructions = new Label
            {
                Location = new Point(22, 180),
                Size = new Size(576, 190),
                ForeColor = Color.FromArgb(226, 232, 244),
                Text =
                    "1. Установите VB-CABLE с официального сайта vb-audio.com/Cable и следуйте инструкциям установщика Windows.\r\n\r\n" +
                    "2. Откройте саундпад и в его настройках выберите CABLE Input как второй аудиовыход. Основной выход оставьте на наушниках для мониторинга.\r\n\r\n" +
                    "3. В Discord Desktop откройте Настройки пользователя → Голос и видео → Устройство ввода и выберите CABLE Output.\r\n\r\n" +
                    "4. Нажмите тест микрофона Discord и воспроизведите плитку. Если индикатор не реагирует, проверьте оба устройства и временно отключите Krisp/подавление шума."
            };

            var refreshButton = MakeButton("Проверить VB-CABLE", Color.FromArgb(41, 53, 76));
            refreshButton.Width = 170;
            refreshButton.Location = new Point(22, 395);
            refreshButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            refreshButton.Click += delegate
            {
                deviceStatus.Text = GetDiscordCableStatus();
                deviceStatus.ForeColor = deviceStatus.Text.IndexOf("не найден", StringComparison.OrdinalIgnoreCase) >= 0
                    ? Color.FromArgb(255, 190, 105)
                    : Color.FromArgb(104, 211, 184);
            };

            var openSoundPadButton = MakeButton("Открыть саундпад", Color.FromArgb(53, 105, 99));
            openSoundPadButton.Width = 165;
            openSoundPadButton.Location = new Point(220, 395);
            openSoundPadButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            openSoundPadButton.Click += delegate
            {
                guide.Close();
                OpenSoundPad(sender, EventArgs.Empty);
            };

            var closeButton = MakeButton("Закрыть", Color.FromArgb(41, 53, 76));
            closeButton.Width = 100;
            closeButton.Location = new Point(498, 395);
            closeButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            closeButton.Click += delegate { guide.Close(); };

            guide.Controls.Add(explanation);
            guide.Controls.Add(deviceStatus);
            guide.Controls.Add(instructions);
            guide.Controls.Add(refreshButton);
            guide.Controls.Add(openSoundPadButton);
            guide.Controls.Add(closeButton);
            deviceStatus.Text = GetDiscordCableStatus();
            deviceStatus.ForeColor = deviceStatus.Text.IndexOf("не найден", StringComparison.OrdinalIgnoreCase) >= 0
                ? Color.FromArgb(255, 190, 105)
                : Color.FromArgb(104, 211, 184);
            guide.ShowDialog(this);
        }

        private static string GetDiscordCableStatus()
        {
            bool playbackFound;
            bool captureFound;
            string playbackError;
            string captureError;
            playbackFound = FindWaveDevice(true, "CABLE Input", out playbackError);
            captureFound = FindWaveDevice(false, "CABLE Output", out captureError);

            var status = new StringBuilder();
            status.Append("Проверка устройств Windows: ");
            status.Append(playbackFound ? "CABLE Input найден" : "CABLE Input не найден");
            status.Append("; ");
            status.Append(captureFound ? "CABLE Output найден." : "CABLE Output не найден.");
            if (!String.IsNullOrEmpty(playbackError))
                status.Append(" Ошибка проверки выхода: ").Append(playbackError);
            if (!String.IsNullOrEmpty(captureError))
                status.Append(" Ошибка проверки микрофона: ").Append(captureError);
            if (!playbackFound || !captureFound)
                status.Append(" Установите/проверьте VB-CABLE и нажмите проверку ещё раз.");
            return status.ToString();
        }

        private static bool FindWaveDevice(bool output, string expectedName, out string error)
        {
            error = null;
            try
            {
                uint count = output
                    ? NativeMethods.GetWaveOutputDeviceCount()
                    : NativeMethods.GetWaveInputDeviceCount();
                for (uint index = 0; index < count; index++)
                {
                    string productName;
                    if (output)
                    {
                        NativeMethods.WaveOutputCaps caps;
                        uint result = NativeMethods.GetWaveOutputDeviceCaps(new IntPtr(index), out caps,
                            (uint)Marshal.SizeOf(typeof(NativeMethods.WaveOutputCaps)));
                        if (result != 0)
                        {
                            error = "WinMM код " + result + ".";
                            return false;
                        }
                        productName = caps.ProductName;
                    }
                    else
                    {
                        NativeMethods.WaveInputCaps caps;
                        uint result = NativeMethods.GetWaveInputDeviceCaps(new IntPtr(index), out caps,
                            (uint)Marshal.SizeOf(typeof(NativeMethods.WaveInputCaps)));
                        if (result != 0)
                        {
                            error = "WinMM код " + result + ".";
                            return false;
                        }
                        productName = caps.ProductName;
                    }
                    if (!String.IsNullOrEmpty(productName) &&
                        productName.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return false;
        }

        private void ShowRecoveryGuide(object sender, EventArgs e)
        {
            var guide = new Form
            {
                Text = "Помощь при блокировке браузера",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(540, 430),
                BackColor = Color.FromArgb(18, 26, 45),
                ForeColor = Color.FromArgb(226, 232, 244),
                Font = new Font("Segoe UI", 9F),
                TopMost = true
            };
            var instructions = new Label
            {
                Location = new Point(22, 18),
                Size = new Size(496, 320),
                ForeColor = Color.FromArgb(226, 232, 244),
                Text =
                    "Страница по ссылке похожа на мошенническое браузерное предупреждение, а не на сообщение полиции Windows. Не платите штраф, не вводите личные данные и не скачивайте предложенные файлы.\r\n\r\n" +
                    "Сочетания Windows:\r\n" +
                    "• Alt+Tab — переключиться на другое окно.\r\n" +
                    "• Ctrl+Shift+Esc — открыть Диспетчер задач.\r\n" +
                    "• Alt+F4 — закрыть активное окно; сначала выберите браузер, иначе закроется Моглокер.\r\n" +
                    "• Ctrl+Alt+Delete — системный экран безопасности Windows; выберите Диспетчер задач.\r\n\r\n" +
                    "Если блокировка мешает, Ctrl+Alt+M сворачивает Моглокер. После этого попробуйте Esc/F11, затем Ctrl+W для вкладки или Alt+F4 для окна браузера. Ctrl+Alt+Delete — защищённая комбинация Windows: обычное приложение не может перехватить или подделать её.\r\n\r\n" +
                    "Завершение браузера через Диспетчер задач может закрыть все окна и потерять несохранённые данные."
            };
            var switchButton = MakeButton("Свернуть и перейти к браузеру", Color.FromArgb(41, 53, 76));
            switchButton.Width = 220;
            switchButton.Location = new Point(22, 360);
            switchButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            switchButton.Click += delegate
            {
                guide.Close();
                WindowState = FormWindowState.Minimized;
                AddAlert("ИНФО", "Помощь при блокировке", "Монитор свёрнут для ручного выхода из подозрительной страницы браузера.");
            };

            var taskManagerButton = MakeButton("Открыть Диспетчер задач", Color.FromArgb(64, 89, 122));
            taskManagerButton.Width = 205;
            taskManagerButton.Location = new Point(270, 360);
            taskManagerButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            taskManagerButton.Click += delegate
            {
                guide.Close();
                WindowState = FormWindowState.Minimized;
                try
                {
                    string taskManagerPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "Taskmgr.exe");
                    Process.Start(taskManagerPath);
                    AddAlert("ИНФО", "Помощь при блокировке", "Диспетчер задач открыт по запросу пользователя.");
                }
                catch (Exception ex)
                {
                    WindowState = FormWindowState.Normal;
                    MessageBox.Show(this, "Не удалось открыть Диспетчер задач: " + ex.Message,
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            guide.Controls.Add(instructions);
            guide.Controls.Add(switchButton);
            guide.Controls.Add(taskManagerButton);
            guide.AcceptButton = switchButton;
            guide.ShowDialog(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            adBlockPanel.Initialize();
            emergencyHotKeyRegistered = NativeMethods.RegisterHotKey(
                Handle, EmergencyHotKeyId, ModControl | ModAlt, VirtualKeyM);
            if (!emergencyHotKeyRegistered)
            {
                int error = Marshal.GetLastWin32Error();
                AddAlert("ПРЕДУПРЕЖДЕНИЕ", "Горячая клавиша",
                    "Не удалось зарегистрировать Ctrl+Alt+M (код Windows " + error +
                    "). Используйте кнопку «Помощь при блокировке» или обычные сочетания Windows.");
            }
            else
            {
                AddAlert("ИНФО", "Горячая клавиша",
                    "Ctrl+Alt+M сворачивает Моглокер или возвращает его поверх окон.");
            }
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Activate();
            trayIcon.Visible = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                ShowInTaskbar = false;
                if (trayIcon != null)
                {
                    trayIcon.Visible = true;
                    trayIcon.ShowBalloonTip(2000, "Моглокер",
                        "Монитор свёрнут и продолжает работу.", ToolTipIcon.Info);
                }
            }
            else if (trayIcon != null)
            {
                trayIcon.Visible = false;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotKey && message.WParam.ToInt32() == EmergencyHotKeyId)
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    WindowState = FormWindowState.Normal;
                    TopMost = true;
                    Activate();
                    BringToFront();
                    AddAlert("ИНФО", "Горячая клавиша", "Моглокер восстановлен поверх окон по Ctrl+Alt+M.");
                }
                else
                {
                    WindowState = FormWindowState.Minimized;
                    AddAlert("ИНФО", "Горячая клавиша", "Моглокер свёрнут по Ctrl+Alt+M.");
                }
                return;
            }
            if (message.Msg == 0x0084 && appSettings.HeaderStyle == "MacOS" &&
                WindowState == FormWindowState.Normal)
            {
                Point cursor = Cursor.Position;
                Rectangle bounds = Bounds;
                const int edge = 8;
                bool left = cursor.X < bounds.Left + edge;
                bool right = cursor.X >= bounds.Right - edge;
                bool top = cursor.Y < bounds.Top + edge;
                bool bottom = cursor.Y >= bounds.Bottom - edge;
                if (top && left)
                    message.Result = new IntPtr(13);
                else if (top && right)
                    message.Result = new IntPtr(14);
                else if (bottom && left)
                    message.Result = new IntPtr(16);
                else if (bottom && right)
                    message.Result = new IntPtr(17);
                else if (left)
                    message.Result = new IntPtr(10);
                else if (right)
                    message.Result = new IntPtr(11);
                else if (top)
                    message.Result = new IntPtr(12);
                else if (bottom)
                    message.Result = new IntPtr(15);
                else
                    base.WndProc(ref message);
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            scanTimer.Stop();
            animationTimer.Stop();
            pageTransitionTimer.Stop();
            pulseTimer.Stop();
            if (emergencyHotKeyRegistered)
            {
                NativeMethods.UnregisterHotKey(Handle, EmergencyHotKeyId);
                emergencyHotKeyRegistered = false;
            }
            foreach (FileSystemWatcher watcher in watchers)
                watcher.Dispose();
            if (trayIcon != null)
                trayIcon.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal sealed class AppSettings
    {
        public string Theme { get; set; }
        public string CustomThemeName { get; set; }
        public List<CustomTheme> CustomThemes { get; set; }
        public string HeaderStyle { get; set; }
        public bool Animations { get; set; }
        public string ExportDirectory { get; set; }

        public AppSettings()
        {
            Theme = "Dark";
            CustomThemeName = String.Empty;
            CustomThemes = new List<CustomTheme>();
            HeaderStyle = "Windows";
            Animations = true;
            ExportDirectory = String.Empty;
        }
    }

    internal sealed class CustomTheme
    {
        public string Name { get; set; }
        public string Background { get; set; }
        public string Surface { get; set; }
        public string Text { get; set; }
        public string Accent { get; set; }
    }

    internal sealed class ControlThemeState
    {
        private readonly Color backColor;
        private readonly Color foreColor;
        private readonly Color borderColor;
        private readonly Color mouseOverColor;
        private readonly Color mouseDownColor;

        public ControlThemeState(Control control)
        {
            backColor = control.BackColor;
            foreColor = control.ForeColor;
            Button button = control as Button;
            if (button != null)
            {
                borderColor = button.FlatAppearance.BorderColor;
                mouseOverColor = button.FlatAppearance.MouseOverBackColor;
                mouseDownColor = button.FlatAppearance.MouseDownBackColor;
            }
        }

        public void Restore(Control control)
        {
            control.BackColor = backColor;
            control.ForeColor = foreColor;
            Button button = control as Button;
            if (button != null)
            {
                button.FlatAppearance.BorderColor = borderColor;
                button.FlatAppearance.MouseOverBackColor = mouseOverColor;
                button.FlatAppearance.MouseDownBackColor = mouseDownColor;
            }
        }
    }

    internal sealed class CellThemeState
    {
        private readonly Color backColor;
        private readonly Color foreColor;
        private readonly Color selectionBackColor;
        private readonly Color selectionForeColor;

        public CellThemeState(DataGridViewCellStyle style)
        {
            backColor = style.BackColor;
            foreColor = style.ForeColor;
            selectionBackColor = style.SelectionBackColor;
            selectionForeColor = style.SelectionForeColor;
        }

        public void Restore(DataGridViewCellStyle style)
        {
            style.BackColor = backColor;
            style.ForeColor = foreColor;
            style.SelectionBackColor = selectionBackColor;
            style.SelectionForeColor = selectionForeColor;
        }
    }

    internal sealed class ThemeEditorDialog : Form
    {
        private readonly TextBox nameTextBox;
        private readonly Label[] colorLabels = new Label[4];
        private readonly Color[] colors =
        {
            Color.FromArgb(12, 17, 31),
            Color.FromArgb(18, 26, 45),
            Color.FromArgb(226, 232, 244),
            Color.FromArgb(104, 211, 184)
        };
        private readonly string[] colorNames = { "Фон", "Поверхность", "Текст", "Акцент" };

        public ThemeEditorDialog()
        {
            Text = "Создать цветовую тему";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 330);
            BackColor = Color.FromArgb(18, 26, 45);
            ForeColor = Color.FromArgb(226, 232, 244);
            Font = new Font("Segoe UI", 9F);

            var heading = new Label
            {
                Text = "Новая тема",
                Location = new Point(22, 18),
                Size = new Size(390, 32),
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(237, 244, 255)
            };
            var nameLabel = new Label
            {
                Text = "Название",
                Location = new Point(24, 66),
                Size = new Size(120, 24),
                ForeColor = ForeColor
            };
            nameTextBox = new TextBox
            {
                Location = new Point(158, 62),
                Size = new Size(250, 26),
                Text = "Моя тема",
                BackColor = Color.FromArgb(12, 17, 31),
                ForeColor = ForeColor
            };
            Controls.Add(heading);
            Controls.Add(nameLabel);
            Controls.Add(nameTextBox);

            for (int index = 0; index < colorNames.Length; index++)
            {
                int colorIndex = index;
                var label = new Label
                {
                    Text = colorNames[index],
                    Location = new Point(24, 106 + index * 42),
                    Size = new Size(120, 28),
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = ForeColor
                };
                colorLabels[index] = new Label
                {
                    Text = ColorTranslator.ToHtml(colors[index]),
                    Location = new Point(158, 106 + index * 42),
                    Size = new Size(120, 28),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = colors[index],
                    ForeColor = index == 2 ? Color.FromArgb(31, 41, 55) : Color.White,
                    BorderStyle = BorderStyle.FixedSingle
                };
                var pickButton = new Button
                {
                    Text = "Выбрать цвет",
                    Location = new Point(294, 106 + index * 42),
                    Size = new Size(114, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(35, 49, 70),
                    ForeColor = Color.FromArgb(225, 233, 246),
                    Cursor = Cursors.Hand
                };
                pickButton.Click += delegate { PickColor(colorIndex); };
                Controls.Add(label);
                Controls.Add(colorLabels[index]);
                Controls.Add(pickButton);
            }

            var saveButton = new Button
            {
                Text = "Создать",
                DialogResult = DialogResult.OK,
                Location = new Point(248, 282),
                Size = new Size(78, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(53, 105, 99),
                ForeColor = Color.White
            };
            var cancelButton = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Location = new Point(330, 282),
                Size = new Size(78, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 49, 70),
                ForeColor = ForeColor
            };
            Controls.Add(saveButton);
            Controls.Add(cancelButton);
            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        public string ThemeName { get { return nameTextBox.Text; } }
        public Color BackgroundColor { get { return colors[0]; } }
        public Color SurfaceColor { get { return colors[1]; } }
        public Color TextColor { get { return colors[2]; } }
        public Color AccentColor { get { return colors[3]; } }

        private void PickColor(int index)
        {
            using (var dialog = new ColorDialog
            {
                Color = colors[index],
                FullOpen = true,
                AnyColor = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                colors[index] = dialog.Color;
                colorLabels[index].Text = ColorTranslator.ToHtml(dialog.Color);
                colorLabels[index].BackColor = dialog.Color;
                colorLabels[index].ForeColor =
                    dialog.Color.GetBrightness() > 0.62F ? Color.FromArgb(31, 41, 55) : Color.White;
            }
        }
    }

    internal static class SettingsStore
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MogLocker");
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static AppSettings Load()
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            AppSettings settings = Serializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (settings == null)
                throw new InvalidDataException("Файл настроек MogLocker пуст.");
            if (settings.Theme != "Dark" && settings.Theme != "Light" &&
                settings.Theme != "System" && settings.Theme != "Custom")
                settings.Theme = "Dark";
            if (settings.CustomThemeName == null)
                settings.CustomThemeName = String.Empty;
            if (settings.CustomThemes == null)
                settings.CustomThemes = new List<CustomTheme>();
            foreach (CustomTheme theme in settings.CustomThemes)
            {
                if (theme == null || String.IsNullOrWhiteSpace(theme.Name) ||
                    !IsHtmlColor(theme.Background) || !IsHtmlColor(theme.Surface) ||
                    !IsHtmlColor(theme.Text) || !IsHtmlColor(theme.Accent))
                    throw new InvalidDataException("Файл настроек содержит некорректную пользовательскую тему.");
            }
            if (settings.Theme == "Custom" && !settings.CustomThemes.Any(theme =>
                String.Equals(theme.Name, settings.CustomThemeName, StringComparison.Ordinal)))
                settings.Theme = "Dark";
            if (settings.HeaderStyle != "MacOS" && settings.HeaderStyle != "Windows")
                settings.HeaderStyle = "Windows";
            if (settings.ExportDirectory == null)
                settings.ExportDirectory = String.Empty;
            return settings;
        }

        private static bool IsHtmlColor(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$"))
                return true;
            return Color.FromName(value).IsKnownColor;
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            Directory.CreateDirectory(SettingsDirectory);
            string temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, Serializer.Serialize(settings), new UTF8Encoding(false));
            try
            {
                if (File.Exists(SettingsPath))
                    File.Replace(temporaryPath, SettingsPath, null);
                else
                    File.Move(temporaryPath, SettingsPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public static bool ResolveLightTheme(string theme)
        {
            if (String.Equals(theme, "Light", StringComparison.Ordinal))
                return true;
            if (!String.Equals(theme, "System", StringComparison.Ordinal))
                return false;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
            {
                object value = key == null ? null : key.GetValue("AppsUseLightTheme", 0);
                return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
            }
        }
    }

    internal sealed class StartupSplash : Form
    {
        private const int Duration = 2600;
        private readonly Timer timer;
        private readonly bool lightTheme;
        private readonly Color backgroundColor;
        private readonly Color surfaceColor;
        private readonly Color textColor;
        private readonly Color accentColor;
        private int elapsed;

        public StartupSplash(AppSettings settings)
        {
            lightTheme = SettingsStore.ResolveLightTheme(settings.Theme);
            backgroundColor = lightTheme
                ? Color.FromArgb(246, 248, 251)
                : Color.FromArgb(12, 17, 31);
            surfaceColor = lightTheme
                ? Color.White
                : Color.FromArgb(19, 30, 49);
            textColor = lightTheme
                ? Color.FromArgb(31, 41, 55)
                : Color.FromArgb(226, 232, 244);
            accentColor = Color.FromArgb(104, 211, 184);
            if (settings.Theme == "Custom" && settings.CustomThemes != null)
            {
                CustomTheme theme = settings.CustomThemes.FirstOrDefault(item =>
                    String.Equals(item.Name, settings.CustomThemeName, StringComparison.Ordinal));
                if (theme != null)
                {
                    backgroundColor = ColorTranslator.FromHtml(theme.Background);
                    surfaceColor = ColorTranslator.FromHtml(theme.Surface);
                    textColor = ColorTranslator.FromHtml(theme.Text);
                    accentColor = ColorTranslator.FromHtml(theme.Accent);
                }
            }
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 330);
            BackColor = backgroundColor;
            ForeColor = textColor;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            timer = new Timer { Interval = 30 };
            timer.Tick += AdvanceAnimation;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var background = new LinearGradientBrush(ClientRectangle,
                surfaceColor, backgroundColor, 110F))
                graphics.FillRectangle(background, ClientRectangle);
            using (var frame = new Pen(Color.FromArgb(90, accentColor)))
                graphics.DrawRectangle(frame, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            DrawLogo(graphics);
            DrawText(graphics);
            DrawProgress(graphics);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            timer.Dispose();
            base.OnFormClosed(e);
        }

        private void AdvanceAnimation(object sender, EventArgs e)
        {
            elapsed = Math.Min(Duration, elapsed + timer.Interval);
            Invalidate();
            if (elapsed >= Duration)
                Close();
        }

        private void DrawLogo(Graphics graphics)
        {
            float phase = elapsed / 700F;
            float centerX = ClientSize.Width / 2F;
            float centerY = 94F;
            float pulse = (float)((Math.Sin(elapsed / 210.0) + 1.0) / 2.0);

            using (var glow = new Pen(Color.FromArgb(30 + (int)(35 * pulse), accentColor), 2F))
            using (var orbit = new Pen(Color.FromArgb(85, accentColor), 1F))
            using (var accent = new SolidBrush(accentColor))
            using (var shieldFill = new SolidBrush(Color.FromArgb(24, accentColor)))
            using (var shieldLine = new Pen(Lighten(accentColor, 0.18F), 2F))
            {
                float ring = 48F + pulse * 4F;
                graphics.DrawEllipse(glow, centerX - ring, centerY - ring, ring * 2, ring * 2);
                graphics.DrawEllipse(orbit, centerX - 60, centerY - 60, 120, 120);

                float angle = phase * 180F;
                float dotX = centerX + (float)Math.Cos(angle * Math.PI / 180.0) * 60F;
                float dotY = centerY + (float)Math.Sin(angle * Math.PI / 180.0) * 60F;
                graphics.FillEllipse(accent, dotX - 4, dotY - 4, 8, 8);

                PointF[] shield =
                {
                    new PointF(centerX, centerY - 35),
                    new PointF(centerX + 27, centerY - 25),
                    new PointF(centerX + 23, centerY + 10),
                    new PointF(centerX, centerY + 34),
                    new PointF(centerX - 23, centerY + 10),
                    new PointF(centerX - 27, centerY - 25)
                };
                graphics.FillPolygon(shieldFill, shield);
                graphics.DrawPolygon(shieldLine, shield);

                using (var monogram = new Pen(textColor, 4F))
                {
                    monogram.StartCap = LineCap.Round;
                    monogram.EndCap = LineCap.Round;
                    monogram.LineJoin = LineJoin.Round;
                    graphics.DrawLines(monogram, new[]
                    {
                        new PointF(centerX - 14, centerY + 9),
                        new PointF(centerX - 14, centerY - 11),
                        new PointF(centerX, centerY + 3),
                        new PointF(centerX + 14, centerY - 11),
                        new PointF(centerX + 14, centerY + 9)
                    });
                }
            }
        }

        private void DrawText(Graphics graphics)
        {
            using (var titleFont = new Font("Segoe UI Semibold", 21F, FontStyle.Bold))
            using (var subtitleFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (var statusFont = new Font("Segoe UI", 9F))
            using (var titleBrush = new SolidBrush(textColor))
            using (var mutedBrush = new SolidBrush(Color.FromArgb(185,
                textColor.R, textColor.G, textColor.B)))
            using (var statusBrush = new SolidBrush(accentColor))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                graphics.DrawString("МОГЛОКЕР", titleFont, titleBrush,
                    new RectangleF(20, 151, ClientSize.Width - 40, 34), format);
                graphics.DrawString("МОНИТОР ЗАЩИТЫ WINDOWS", subtitleFont, mutedBrush,
                    new RectangleF(20, 190, ClientSize.Width - 40, 20), format);

                string status = elapsed < 900
                    ? "Подготовка компонентов"
                    : elapsed < 1800
                        ? "Запуск модулей защиты"
                        : "Подготовка интерфейса";
                graphics.DrawString(status, statusFont, statusBrush,
                    new RectangleF(20, 237, ClientSize.Width - 40, 22), format);
            }
        }

        private void DrawProgress(Graphics graphics)
        {
            const int width = 390;
            const int height = 4;
            int left = (ClientSize.Width - width) / 2;
            int top = 274;
            float progress = Math.Min(1F, (float)elapsed / Duration);

            using (var track = new SolidBrush(Color.FromArgb(48,
                textColor.R, textColor.G, textColor.B)))
            using (var fill = new LinearGradientBrush(
                new Rectangle(left, top, width, height),
                accentColor, Lighten(accentColor, 0.18F), 0F))
            {
                graphics.FillRectangle(track, left, top, width, height);
                graphics.FillRectangle(fill, left, top, Math.Max(1, (int)(width * progress)), height);
            }
        }

        private static Color Lighten(Color color, float amount)
        {
            return Color.FromArgb(
                color.R + (int)((255 - color.R) * amount),
                color.G + (int)((255 - color.G) * amount),
                color.B + (int)((255 - color.B) * amount));
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (NativeMessagingHost.IsHostInvocation(args))
            {
                NativeMessagingHost.Run();
                return;
            }

            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, @"Local\MogLocker.Main", out createdNew))
            {
                if (!createdNew)
                {
                    if (RestoreExistingInstance())
                        return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppSettings settings = SettingsStore.Load();
                AnimatedButton.AnimationsEnabled = settings.Animations;
                if (settings.Animations)
                {
                    using (var splash = new StartupSplash(settings))
                        splash.ShowDialog();
                }
                using (var mainForm = new MainForm(settings))
                    Application.Run(mainForm);
            }
        }

        private static bool RestoreExistingInstance()
        {
            var processIds = new HashSet<int>();
            int currentSessionId = Process.GetCurrentProcess().SessionId;
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id != Process.GetCurrentProcess().Id &&
                        process.SessionId == currentSessionId &&
                        (String.Equals(process.ProcessName, "MogLocker", StringComparison.OrdinalIgnoreCase) ||
                         String.Equals(process.ProcessName, "MogLockerApp", StringComparison.OrdinalIgnoreCase)))
                        processIds.Add(process.Id);
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            bool restored = false;
            NativeMethods.EnumWindows(delegate(IntPtr window, IntPtr state)
            {
                uint processId;
                NativeMethods.GetWindowThreadProcessId(window, out processId);
                if (!processIds.Contains((int)processId) || !NativeMethods.IsWindowVisible(window))
                    return true;

                uint targetProcessId = processId;
                IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
                uint foregroundProcessId;
                uint foregroundThreadId = foregroundWindow == IntPtr.Zero
                    ? 0
                    : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
                uint targetThreadId = NativeMethods.GetWindowThreadProcessId(window, out targetProcessId);
                uint currentThreadId = NativeMethods.GetCurrentThreadId();
                bool attachedToForeground = foregroundThreadId != 0 &&
                    foregroundThreadId != currentThreadId &&
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                bool attachedToTarget = targetThreadId != 0 &&
                    targetThreadId != currentThreadId &&
                    targetThreadId != foregroundThreadId &&
                    NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);

                NativeMethods.AllowSetForegroundWindow(targetProcessId);
                NativeMethods.ShowWindowAsync(window, 9);
                NativeMethods.BringWindowToTop(window);
                NativeMethods.SetForegroundWindow(window);
                if (attachedToTarget)
                    NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
                if (attachedToForeground)
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                if (NativeMethods.GetForegroundWindow() == window)
                {
                    restored = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return restored;
        }
    }
}
