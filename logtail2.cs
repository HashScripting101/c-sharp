// LogTail.cs  --  v2
//
// Build from command line:
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//       /target:winexe
//       /out:LogTail.exe
//       /reference:System.Windows.Forms.dll
//       /reference:System.Drawing.dll
//       LogTail.cs
//
// In Visual Studio: add this file to a WinForms (.NET Framework) project.
//   If nullable warnings appear, set <Nullable>disable</Nullable> in the .csproj.
//
// New in v2:
// - Line numbers in a gutter
// - Bookmarks: click the gutter (or Ctrl+B) to toggle a bullet; F3 / Shift+F3
//   jump to next / previous bookmark; bookmarked lines get an orange outline
// - Markers now paint the FULL LINE BACKGROUND (much more blatant)
// - File > Open Remote Log (UNC)... prompts for credentials when access fails
// - File > Connect Server Event Logs... opens Application / System / Security
//   tabs for a local or remote server, with a credential prompt
// - Marker / filter changes recolor all open tabs instantly, bookmarks survive
//
// Notes:
// - Remote event logs require the Remote Registry service running on the
//   target server and an account with rights there. The Security log needs
//   elevated rights even locally.
// - Credentials are used to establish a Windows network session (same effect
//   as "net use \\server\IPC$ /user:...") and are never written to disk.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace LogTailApp
{
    // ============================================================
    // THEME
    // ============================================================

    public static class Theme
    {
        public static Color Back = Color.FromArgb(18, 18, 18);
        public static Color Gutter = Color.FromArgb(30, 30, 30);
        public static Color Fore = Color.Gainsboro;
        public static Color NumFore = Color.FromArgb(120, 120, 120);
        public static Color BookmarkBack = Color.FromArgb(45, 45, 82);
        public static Color Bullet = Color.Orange;

        public static Color TextOn(Color back)
        {
            double lum = 0.299 * back.R + 0.587 * back.G + 0.114 * back.B;
            return lum > 145.0 ? Color.Black : Color.White;
        }

        public static Color Lighten(Color c)
        {
            return Color.FromArgb(
                Math.Min(255, c.R + 40),
                Math.Min(255, c.G + 40),
                Math.Min(255, c.B + 40));
        }
    }


    // ============================================================
    // MARKER  (pattern -> full-line background color)
    // ============================================================

    public class Marker
    {
        public string Pattern = "";
        public Color Back = Color.Gray;
        public Regex Rx;

        public void Compile()
        {
            Rx = null;
            if (Pattern.Length == 0) return;

            try
            {
                // Try as a real regular expression first.
                Rx = new Regex(Pattern, RegexOptions.IgnoreCase);
            }
            catch (Exception)
            {
                // Fall back to plain text.
                Rx = new Regex(Regex.Escape(Pattern), RegexOptions.IgnoreCase);
            }
        }
    }


    // ============================================================
    // ONE DISPLAYED LOG LINE
    // ============================================================

    public class LogLine
    {
        public int Number;
        public string Text = "";
        public Color Back;
        public bool HasColor;
        public bool Bookmarked;

        public override string ToString() { return Text; }
    }


    // ============================================================
    // OWNER-DRAWN LOG VIEW  (line numbers + gutter bookmarks)
    // ============================================================

    public class LogView : ListBox
    {
        public const int GutterWidth = 64;

        public LogView()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 17;
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            ForeColor = Theme.Fore;
            Font = new Font("Consolas", 9.5f);
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            SelectionMode = SelectionMode.MultiExtended;
            HorizontalScrollbar = true;
            HorizontalExtent = 6000;

            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);
            UpdateStyles();
        }


        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;

            LogLine ln = Items[e.Index] as LogLine;
            if (ln == null) return;

            bool selected =
                (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            // ------------------------------------------------
            // Row background (full line, including the gutter)
            // ------------------------------------------------

            Color back = Theme.Back;

            if (ln.HasColor)
                back = ln.Back;
            else if (ln.Bookmarked)
                back = Theme.BookmarkBack;

            if (selected)
                back = Theme.Lighten(back);

            using (SolidBrush b = new SolidBrush(back))
                e.Graphics.FillRectangle(b, e.Bounds);

            // Gutter shading only when the line has no marker color,
            // so colored lines stay one solid blatant bar.
            if (!ln.HasColor && !ln.Bookmarked && !selected)
            {
                using (SolidBrush g = new SolidBrush(Theme.Gutter))
                {
                    e.Graphics.FillRectangle(
                        g,
                        e.Bounds.X, e.Bounds.Y,
                        GutterWidth, e.Bounds.Height);
                }
            }

            // ------------------------------------------------
            // Bookmark bullet + outline
            // ------------------------------------------------

            if (ln.Bookmarked)
            {
                int d = 8;
                int cy = e.Bounds.Y + (e.Bounds.Height - d) / 2;

                using (SolidBrush b = new SolidBrush(Theme.Bullet))
                    e.Graphics.FillEllipse(b, e.Bounds.X + 4, cy, d, d);

                using (Pen p = new Pen(Theme.Bullet))
                {
                    e.Graphics.DrawRectangle(
                        p,
                        e.Bounds.X,
                        e.Bounds.Y,
                        Math.Max(e.Bounds.Width, ClientSize.Width) - 1,
                        e.Bounds.Height - 1);
                }
            }

            // ------------------------------------------------
            // Line number (right aligned in the gutter)
            // ------------------------------------------------

            Color numColor =
                ln.HasColor ? Theme.TextOn(back) : Theme.NumFore;

            TextRenderer.DrawText(
                e.Graphics,
                ln.Number.ToString(),
                Font,
                new Rectangle(e.Bounds.X, e.Bounds.Y, GutterWidth - 6, e.Bounds.Height),
                numColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            // ------------------------------------------------
            // The text itself
            // ------------------------------------------------

            Color textColor =
                ln.HasColor ? Theme.TextOn(back) : Theme.Fore;

            TextRenderer.DrawText(
                e.Graphics,
                ln.Text,
                Font,
                new Point(e.Bounds.X + GutterWidth + 6, e.Bounds.Y + 1),
                textColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
        }


        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Click inside the gutter toggles a bookmark
            // without disturbing the current selection.
            if (e.Button == MouseButtons.Left && e.X <= GutterWidth)
            {
                int idx = IndexFromPoint(e.Location);

                if (idx >= 0 && idx < Items.Count)
                {
                    LogLine ln = Items[idx] as LogLine;

                    if (ln != null)
                    {
                        ln.Bookmarked = !ln.Bookmarked;
                        Invalidate(GetItemRectangle(idx));
                        return;
                    }
                }
            }

            base.OnMouseDown(e);
        }


        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Ctrl+C copies the selected line(s).
            if (e.Control && e.KeyCode == Keys.C)
            {
                StringBuilder sb = new StringBuilder();

                foreach (int i in SelectedIndices)
                {
                    LogLine ln = Items[i] as LogLine;
                    if (ln != null) sb.AppendLine(ln.Text);
                }

                if (sb.Length > 0)
                {
                    try { Clipboard.SetText(sb.ToString()); }
                    catch (Exception) { }
                }

                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }


        public void ScrollToBottom()
        {
            if (Items.Count > 0)
                TopIndex = Items.Count - 1;
        }
    }


    // ============================================================
    // FILE TAIL READER  (unchanged from v1)
    // ============================================================

    public class Tail
    {
        public string Path;

        private long pos;
        private string remainder = "";

        public Tail(string path, bool fromStart)
        {
            Path = path;

            try
            {
                long len = new FileInfo(path).Length;
                pos = fromStart ? 0 : Math.Max(0, len - 65536);
            }
            catch (Exception)
            {
                pos = 0;
            }
        }

        public List<string> ReadNew()
        {
            List<string> lines = new List<string>();

            try
            {
                using (FileStream fs = new FileStream(
                    Path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    // Truncation / rollover.
                    if (fs.Length < pos) { pos = 0; remainder = ""; }

                    if (fs.Length == pos) return lines;

                    fs.Seek(pos, SeekOrigin.Begin);

                    long want = fs.Length - pos;

                    if (want > 1048576)
                    {
                        fs.Seek(fs.Length - 1048576, SeekOrigin.Begin);
                        want = 1048576;
                        remainder = "";
                    }

                    byte[] buf = new byte[(int)want];
                    int n = fs.Read(buf, 0, buf.Length);
                    pos = fs.Position;

                    string s = remainder + Encoding.Default.GetString(buf, 0, n);
                    s = s.Replace("\r\n", "\n").Replace('\r', '\n');

                    string[] parts = s.Split('\n');

                    for (int i = 0; i < parts.Length - 1; i++)
                        lines.Add(parts[i]);

                    remainder = parts[parts.Length - 1];
                }
            }
            catch (Exception)
            {
                // Temporary access problems: retry next tick.
            }

            return lines;
        }
    }


    // ============================================================
    // BASE TAB PAGE  (shared by file tabs and event log tabs)
    // ============================================================

    public class LogPage : TabPage
    {
        public LogView View = new LogView();
        public List<LogLine> Lines = new List<LogLine>();
        public int Counter = 0;

        public LogPage()
        {
            Controls.Add(View);
        }
    }


    // ============================================================
    // FILE TAB
    // ============================================================

    public class TailPage : LogPage
    {
        public Tail Tail;

        public TailPage(string path)
        {
            Text = System.IO.Path.GetFileName(path);
            ToolTipText = path;
            Tail = new Tail(path, false);
        }
    }


    // ============================================================
    // EVENT LOG TAB  (Application / System / Security)
    // ============================================================

    public class EventLogPage : LogPage
    {
        public string Machine;   // "." = local
        public string LogName;

        private EventLog log;
        private int lastCount;
        private bool banner;
        private string initError;

        public EventLogPage(string machine, string logName)
        {
            Machine = machine;
            LogName = logName;

            string disp = (machine == "." || machine.Length == 0)
                ? "local" : machine;

            Text = disp + ":" + logName;
            ToolTipText = "Windows Event Log  \\\\" + disp + "  " + logName;

            try
            {
                log = new EventLog(logName, machine);
                lastCount = Math.Max(0, log.Entries.Count - 300);
            }
            catch (Exception ex)
            {
                log = null;
                initError = ex.Message;
            }
        }


        public List<string> PollNew()
        {
            List<string> outLines = new List<string>();

            if (log == null)
            {
                if (!banner)
                {
                    banner = true;
                    outLines.Add("[eventlog] ERROR opening " + LogName +
                        " on " + Machine + ": " + initError);
                    outLines.Add("[eventlog] Use File > Connect Server Event " +
                        "Logs to retry with credentials.");
                }
                return outLines;
            }

            try
            {
                int count = log.Entries.Count;

                // Log was cleared.
                if (count < lastCount) lastCount = 0;

                // Never pull more than 300 entries per poll.
                int start = Math.Max(lastCount, count - 300);

                for (int i = start; i < count; i++)
                    outLines.Add(Format(log.Entries[i]));

                lastCount = count;
                banner = false;
            }
            catch (Exception ex)
            {
                if (!banner)
                {
                    banner = true;
                    outLines.Add("[eventlog] ERROR reading " + LogName +
                        " on " + Machine + ": " + ex.Message);
                }
            }

            return outLines;
        }


        private static string Format(EventLogEntry en)
        {
            string type;

            switch (en.EntryType)
            {
                case EventLogEntryType.Error: type = "ERROR"; break;
                case EventLogEntryType.Warning: type = "WARN"; break;
                case EventLogEntryType.Information: type = "INFO"; break;
                case EventLogEntryType.SuccessAudit: type = "AUDIT-OK"; break;
                case EventLogEntryType.FailureAudit: type = "AUDIT-FAIL"; break;
                default: type = "INFO"; break;
            }

            string msg = en.Message ?? "";
            msg = msg.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (msg.Length > 400) msg = msg.Substring(0, 400) + "...";

            return string.Format(
                "{0:HH:mm:ss} {1,-10} {2}  [EventID {3}]  {4}",
                en.TimeGenerated, type, en.Source, en.InstanceId, msg);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing && log != null)
            {
                try { log.Close(); } catch (Exception) { }
                log = null;
            }
            base.Dispose(disposing);
        }
    }


    // ============================================================
    // NETWORK CREDENTIAL SESSION  (net use \\server\IPC$)
    // ============================================================

    public static class Net
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Auto)]
        private static extern int WNetAddConnection2(
            NETRESOURCE netResource,
            string password,
            string username,
            int flags);


        public static int Connect(string server, string user, string pass)
        {
            NETRESOURCE nr = new NETRESOURCE();
            nr.dwType = 0; // RESOURCETYPE_ANY
            nr.lpRemoteName = @"\\" + server + @"\IPC$";

            return WNetAddConnection2(nr, pass, user, 0);
        }


        public static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "";
                case 5: return "Access denied.";
                case 53: return "Network path not found.";
                case 86:
                case 1326: return "Bad username or password.";
                case 1219: return "Credential conflict: Windows already has a " +
                    "session to this server under different credentials. " +
                    "Run  net use * /delete  and try again.";
                default: return "Network error " + code + ".";
            }
        }
    }


    // ============================================================
    // CREDENTIAL PROMPT  (never persisted)
    // ============================================================

    public class CredDialog : Form
    {
        public TextBox UserBox = new TextBox();
        public TextBox PassBox = new TextBox();

        public CredDialog(string title, string hint)
        {
            Text = title;
            Width = 420;
            Height = 220;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            Label lh = new Label();
            lh.Text = hint;
            lh.SetBounds(12, 10, 380, 34);
            Controls.Add(lh);

            Label l1 = new Label();
            l1.Text = "Username (DOMAIN\\user):";
            l1.SetBounds(12, 50, 380, 18);
            Controls.Add(l1);

            UserBox.SetBounds(12, 70, 380, 24);
            Controls.Add(UserBox);

            Label l2 = new Label();
            l2.Text = "Password:";
            l2.SetBounds(12, 100, 380, 18);
            Controls.Add(l2);

            PassBox.UseSystemPasswordChar = true;
            PassBox.SetBounds(12, 120, 380, 24);
            Controls.Add(PassBox);

            Button ok = new Button();
            ok.Text = "Connect";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(216, 152, 85, 26);
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(307, 152, 85, 26);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }


    // ============================================================
    // SIMPLE TEXT PROMPT
    // ============================================================

    public class TextPrompt : Form
    {
        public TextBox Box = new TextBox();

        public TextPrompt(string title, string label, string initial)
        {
            Text = title;
            Width = 480;
            Height = 150;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            Label l = new Label();
            l.Text = label;
            l.SetBounds(12, 10, 440, 18);
            Controls.Add(l);

            Box.SetBounds(12, 32, 440, 24);
            Box.Text = initial;
            Controls.Add(Box);

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(276, 70, 85, 26);
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(367, 70, 85, 26);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }


    // ============================================================
    // MARKER / FILTER CONFIGURATION WINDOW
    // ============================================================

    public class ConfigForm : Form
    {
        public TextBox MarkerBox = new TextBox();
        public TextBox IncludeBox = new TextBox();
        public TextBox ExcludeBox = new TextBox();

        public ConfigForm(string markers, string include, string exclude)
        {
            Text = "Markers & Filters";
            Width = 700;
            Height = 600;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            Label l1 = new Label();
            l1.Text =
                "Markers - one per line, format  Color=pattern   (up to 20 used).\r\n" +
                "The color becomes the FULL LINE BACKGROUND.\r\n" +
                "Colors: Red Orange Yellow Lime Cyan Magenta Violet Gray Pink\r\n" +
                "        DarkRed Firebrick Goldenrod Teal or any .NET color name.\r\n" +
                "Pattern can be regex or plain text. First matching marker wins.";
            l1.SetBounds(10, 8, 660, 78);
            Controls.Add(l1);

            MarkerBox.Multiline = true;
            MarkerBox.ScrollBars = ScrollBars.Both;
            MarkerBox.WordWrap = false;
            MarkerBox.Font = new Font("Consolas", 10f);
            MarkerBox.SetBounds(10, 90, 660, 315);
            MarkerBox.Text = markers;
            Controls.Add(MarkerBox);

            Label l2 = new Label();
            l2.Text = "Include filter (regex, blank = everything):";
            l2.SetBounds(10, 415, 400, 18);
            Controls.Add(l2);

            IncludeBox.SetBounds(10, 437, 660, 24);
            IncludeBox.Text = include;
            Controls.Add(IncludeBox);

            Label l3 = new Label();
            l3.Text = "Exclude filter (regex, blank = nothing):";
            l3.SetBounds(10, 470, 400, 18);
            Controls.Add(l3);

            ExcludeBox.SetBounds(10, 492, 660, 24);
            ExcludeBox.Text = exclude;
            Controls.Add(ExcludeBox);

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(500, 525, 75, 26);
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(590, 525, 75, 26);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }


    // ============================================================
    // MAIN WINDOW
    // ============================================================

    public class MainForm : Form
    {
        private TabControl tabs = new TabControl();
        private ContextMenuStrip tabMenu = new ContextMenuStrip();
        private Timer timer = new Timer();
        private int evTicks = 0;

        private List<Marker> markers = new List<Marker>();

        private Regex includeRx;
        private Regex excludeRx;
        private string includeStr = "";
        private string excludeStr = "";

        private bool paused = false;
        private bool applyingState = false;

        private ToolStripMenuItem pauseItem;
        private ToolStripMenuItem profilesMenu;

        private string iniPath;

        private const int MaxLines = 60000;
        private const int TrimChunk = 20000;


        // ========================================================
        // ENTRY POINT
        // ========================================================

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            MainForm f = new MainForm();

            foreach (string a in args)
                if (File.Exists(a))
                    f.OpenLog(a);

            Application.Run(f);
        }


        // ========================================================
        // CONSTRUCTOR
        // ========================================================

        public MainForm()
        {
            Text = "LogTail";
            Width = 1100;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;

            iniPath = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath),
                "LogTail.ini");

            // ---------------- MENU ----------------

            MenuStrip menu = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add("&Open Log...\tCtrl+O", null, OnOpen);
            file.DropDownItems.Add("Open &Remote Log (UNC)...\tCtrl+U", null, OnOpenRemote);
            file.DropDownItems.Add("Connect Server &Event Logs...\tCtrl+E", null, OnConnectEventLogs);
            file.DropDownItems.Add("&Close Tab\tCtrl+W", null, OnCloseTab);
            file.DropDownItems.Add("C&lear View", null, OnClear);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("E&xit", null,
                delegate(object s, EventArgs e) { Close(); });
            menu.Items.Add(file);

            ToolStripMenuItem view = new ToolStripMenuItem("&Settings");
            view.DropDownItems.Add("&Markers && Filters...\tCtrl+M", null, OnConfig);

            pauseItem = new ToolStripMenuItem("&Pause Scroll\tCtrl+P", null, OnPause);
            view.DropDownItems.Add(pauseItem);

            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add("Toggle &Bookmark\tCtrl+B", null,
                delegate(object s, EventArgs e) { ToggleBookmark(); });
            view.DropDownItems.Add("&Next Bookmark\tF3", null,
                delegate(object s, EventArgs e) { JumpBookmark(1); });
            view.DropDownItems.Add("P&revious Bookmark\tShift+F3", null,
                delegate(object s, EventArgs e) { JumpBookmark(-1); });
            menu.Items.Add(view);

            profilesMenu = new ToolStripMenuItem("&Profiles");
            menu.Items.Add(profilesMenu);
            RebuildProfilesMenu();

            // ---------------- TABS ----------------

            Controls.Add(tabs);
            tabs.Dock = DockStyle.Fill;
            tabs.ShowToolTips = true;

            // ---------------- TAB RIGHT-CLICK MENU ----------------

            tabMenu.Items.Add("&Close Tab", null,
                delegate(object s2, EventArgs e2) { OnCloseTab(null, null); });

            tabMenu.Items.Add("&Reconnect Event Log", null,
                delegate(object s2, EventArgs e2) { ReconnectTab(); });

            tabMenu.Items.Add(new ToolStripSeparator());

            tabMenu.Items.Add("Close &All Tabs", null,
                delegate(object s2, EventArgs e2) { CloseAllTabs(); });

            tabMenu.Opening +=
                delegate(object s2, System.ComponentModel.CancelEventArgs e2)
            {
                // Reconnect only makes sense on an event log tab.
                tabMenu.Items[1].Enabled =
                    tabs.SelectedTab is EventLogPage;

                e2.Cancel = tabs.TabCount == 0;
            };

            tabs.MouseUp += delegate(object s2, MouseEventArgs me)
            {
                if (me.Button != MouseButtons.Right) return;

                // Select the tab whose header was right-clicked,
                // then show the menu.
                for (int i = 0; i < tabs.TabCount; i++)
                {
                    if (tabs.GetTabRect(i).Contains(me.Location))
                    {
                        tabs.SelectedIndex = i;
                        tabMenu.Show(tabs, me.Location);
                        return;
                    }
                }
            };

            Controls.Add(menu);
            MainMenuStrip = menu;

            // ---------------- DRAG AND DROP ----------------

            AllowDrop = true;

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };

            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string f in files)
                    if (File.Exists(f))
                        OpenLog(f);
            };

            // ---------------- SHORTCUTS ----------------

            KeyPreview = true;

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.O) OnOpen(null, null);
                else if (e.Control && e.KeyCode == Keys.U) OnOpenRemote(null, null);
                else if (e.Control && e.KeyCode == Keys.E) OnConnectEventLogs(null, null);
                else if (e.Control && e.KeyCode == Keys.W) OnCloseTab(null, null);
                else if (e.Control && e.KeyCode == Keys.M) OnConfig(null, null);
                else if (e.Control && e.KeyCode == Keys.P) OnPause(null, null);
                else if (e.Control && e.KeyCode == Keys.B) ToggleBookmark();
                else if (e.KeyCode == Keys.F3 && e.Shift) JumpBookmark(-1);
                else if (e.KeyCode == Keys.F3) JumpBookmark(1);
            };

            // ---------------- STATE ----------------

            DefaultMarkers();
            LoadLastSession();

            // ---------------- TIMER ----------------

            timer.Interval = 300;
            timer.Tick += OnTick;
            timer.Start();
        }


        // ========================================================
        // MARKERS
        // ========================================================

        private void DefaultMarkers()
        {
            markers.Clear();
            AddMarker("Firebrick", "ERROR|FATAL|SEVERE|CRITICAL|AUDIT-FAIL");
            AddMarker("DarkMagenta", "Exception|Traceback|stack trace");
            AddMarker("Goldenrod", "WARN");
            AddMarker("Teal", "timeout|timed out|retry");
            AddMarker("DarkGreen", "success|completed|started");
        }


        private void AddMarker(string colorName, string pattern)
        {
            Marker m = new Marker();
            m.Pattern = pattern;
            m.Back = Color.FromName(colorName);

            if (!m.Back.IsKnownColor)
                m.Back = Color.Gray;

            m.Compile();
            markers.Add(m);
        }


        private void ComputeColor(LogLine ln)
        {
            foreach (Marker m in markers)
            {
                if (m.Rx != null && m.Rx.IsMatch(ln.Text))
                {
                    ln.Back = m.Back;
                    ln.HasColor = true;
                    return;
                }
            }

            ln.HasColor = false;
        }


        private bool LinePassesFilters(string line)
        {
            if (includeRx != null && !includeRx.IsMatch(line)) return false;
            if (excludeRx != null && excludeRx.IsMatch(line)) return false;
            return true;
        }


        // ========================================================
        // APPENDING LINES
        // ========================================================

        public void AppendBatch(LogPage p, List<string> lines)
        {
            if (lines == null || lines.Count == 0) return;

            p.View.BeginUpdate();

            try
            {
                foreach (string text in lines)
                {
                    LogLine ln = new LogLine();
                    ln.Number = ++p.Counter;
                    ln.Text = text;
                    ComputeColor(ln);

                    p.Lines.Add(ln);

                    if (LinePassesFilters(text))
                        p.View.Items.Add(ln);
                }

                if (p.Lines.Count > MaxLines)
                {
                    p.Lines.RemoveRange(0, TrimChunk);
                    RebuildView(p);
                }
            }
            finally
            {
                p.View.EndUpdate();
            }

            if (!paused)
                p.View.ScrollToBottom();
        }


        private void RebuildView(LogPage p)
        {
            p.View.BeginUpdate();

            try
            {
                p.View.Items.Clear();

                foreach (LogLine ln in p.Lines)
                    if (LinePassesFilters(ln.Text))
                        p.View.Items.Add(ln);
            }
            finally
            {
                p.View.EndUpdate();
            }
        }


        // Marker / filter changes: recolor everything in place.
        // Bookmarks survive because the LogLine objects are reused.
        private void ApplySettingsToAllTabs()
        {
            foreach (TabPage p0 in tabs.TabPages)
            {
                LogPage p = p0 as LogPage;
                if (p == null) continue;

                foreach (LogLine ln in p.Lines)
                    ComputeColor(ln);

                RebuildView(p);

                if (!paused)
                    p.View.ScrollToBottom();
            }
        }


        // ========================================================
        // TIMER TICK
        // ========================================================

        private void OnTick(object s, EventArgs e)
        {
            // Files: every tick (300 ms).
            foreach (TabPage p0 in tabs.TabPages)
            {
                TailPage p = p0 as TailPage;
                if (p == null) continue;

                List<string> lines = p.Tail.ReadNew();
                if (lines.Count > 0)
                    AppendBatch(p, lines);
            }

            // Event logs: roughly every 4 seconds.
            evTicks++;

            if (evTicks >= 13)
            {
                evTicks = 0;

                foreach (TabPage p0 in tabs.TabPages)
                {
                    EventLogPage p = p0 as EventLogPage;
                    if (p == null) continue;

                    List<string> lines = p.PollNew();
                    if (lines.Count > 0)
                        AppendBatch(p, lines);
                }
            }
        }


        // ========================================================
        // BOOKMARKS
        // ========================================================

        private LogView CurrentView()
        {
            LogPage p = tabs.SelectedTab as LogPage;
            return p == null ? null : p.View;
        }


        private void ToggleBookmark()
        {
            LogView v = CurrentView();
            if (v == null) return;

            int idx = v.SelectedIndex;
            if (idx < 0 && v.Items.Count > 0) idx = v.Items.Count - 1;
            if (idx < 0) return;

            LogLine ln = v.Items[idx] as LogLine;
            if (ln == null) return;

            ln.Bookmarked = !ln.Bookmarked;
            v.Invalidate(v.GetItemRectangle(idx));
        }


        private void JumpBookmark(int dir)
        {
            LogView v = CurrentView();
            if (v == null || v.Items.Count == 0) return;

            int n = v.Items.Count;
            int start = v.SelectedIndex;
            if (start < 0) start = dir > 0 ? -1 : n;

            for (int step = 1; step <= n; step++)
            {
                int i = ((start + dir * step) % n + n) % n;

                LogLine ln = v.Items[i] as LogLine;

                if (ln != null && ln.Bookmarked)
                {
                    v.ClearSelected();
                    v.SetSelected(i, true);
                    v.TopIndex = Math.Max(0, i - 5);
                    return;
                }
            }
        }


        // ========================================================
        // OPENING LOG FILES
        // ========================================================

        public void OpenLog(string path)
        {
            if (!File.Exists(path)) return;

            foreach (TabPage p0 in tabs.TabPages)
            {
                TailPage existing = p0 as TailPage;

                if (existing != null &&
                    string.Equals(existing.Tail.Path, path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    tabs.SelectedTab = existing;
                    return;
                }
            }

            TailPage p = new TailPage(path);
            tabs.TabPages.Add(p);
            tabs.SelectedTab = p;

            AppendBatch(p, p.Tail.ReadNew());

            if (!applyingState)
                SaveLastSession();
        }


        private void OnOpen(object s, EventArgs e)
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = "Logs (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*";
            d.Multiselect = true;

            if (d.ShowDialog(this) == DialogResult.OK)
                foreach (string f in d.FileNames)
                    OpenLog(f);
        }


        // ========================================================
        // REMOTE UNC LOG  (credential prompt on access failure)
        // ========================================================

        private void OnOpenRemote(object s, EventArgs e)
        {
            TextPrompt tp = new TextPrompt(
                "Open Remote Log",
                @"UNC path to the log file (\\server\share\path\file.log):",
                @"\\");

            if (tp.ShowDialog(this) != DialogResult.OK) return;

            string path = tp.Box.Text.Trim();
            if (path.Length == 0) return;

            if (File.Exists(path))
            {
                OpenLog(path);
                return;
            }

            // Can't reach it: offer credentials.
            string server = ExtractServer(path);

            if (server == null)
            {
                MessageBox.Show(this,
                    "Could not open:\r\n" + path,
                    "Open Remote Log",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CredDialog cd = new CredDialog(
                "Connect to \\\\" + server,
                "Access failed with your current login. Enter credentials " +
                "for " + server + ":");

            if (cd.ShowDialog(this) != DialogResult.OK) return;

            int rc = Net.Connect(server, cd.UserBox.Text.Trim(), cd.PassBox.Text);

            if (rc != 0 && rc != 1219)
            {
                MessageBox.Show(this, Net.Describe(rc),
                    "Connect to " + server,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (File.Exists(path))
                OpenLog(path);
            else
                MessageBox.Show(this,
                    "Connected to " + server + " but still could not open:\r\n" +
                    path + "\r\n\r\nCheck the share and file name.",
                    "Open Remote Log",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }


        private static string ExtractServer(string uncPath)
        {
            if (!uncPath.StartsWith(@"\\")) return null;

            string rest = uncPath.Substring(2);
            int slash = rest.IndexOf('\\');

            string server = slash > 0 ? rest.Substring(0, slash) : rest;
            return server.Length > 0 ? server : null;
        }


        // ========================================================
        // EVENT LOG TABS
        // ========================================================

        // "localhost", "127.0.0.1", and this machine's own name would
        // otherwise be routed through Remote Registry (which usually is
        // not running) and fail with "network path was not found".
        // Normalize them all to "." so the direct local API is used.
        private static string NormalizeMachine(string s)
        {
            if (s == null) return ".";

            s = s.Trim();

            if (s.Length == 0 || s == "." ||
                s == "127.0.0.1" ||
                string.Equals(s, "localhost",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, Environment.MachineName,
                    StringComparison.OrdinalIgnoreCase))
                return ".";

            return s;
        }


        private void OnConnectEventLogs(object s, EventArgs e)
        {
            TextPrompt tp = new TextPrompt(
                "Connect Server Event Logs",
                "Server name (blank = this machine):",
                "");

            if (tp.ShowDialog(this) != DialogResult.OK) return;

            string server = NormalizeMachine(tp.Box.Text);

            if (server != ".")
            {
                CredDialog cd = new CredDialog(
                    "Connect to \\\\" + server,
                    "Credentials for " + server + " (leave username blank " +
                    "to use your current Windows login):");

                if (cd.ShowDialog(this) != DialogResult.OK) return;

                string user = cd.UserBox.Text.Trim();

                if (user.Length > 0)
                {
                    int rc = Net.Connect(server, user, cd.PassBox.Text);

                    if (rc != 0 && rc != 1219)
                    {
                        MessageBox.Show(this, Net.Describe(rc),
                            "Connect to " + server,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            AddEventLogTabs(server);

            if (!applyingState)
                SaveLastSession();
        }


        private void AddEventLogTabs(string machine)
        {
            machine = NormalizeMachine(machine);

            string[] logs = { "Application", "System", "Security" };

            foreach (string logName in logs)
            {
                bool already = false;

                foreach (TabPage p0 in tabs.TabPages)
                {
                    EventLogPage existing = p0 as EventLogPage;

                    if (existing != null &&
                        string.Equals(existing.Machine, machine,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.LogName, logName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        already = true;
                        break;
                    }
                }

                if (already) continue;

                EventLogPage p = new EventLogPage(machine, logName);
                tabs.TabPages.Add(p);

                AppendBatch(p, p.PollNew());
            }
        }


        // ========================================================
        // TAB / VIEW COMMANDS
        // ========================================================

        private void OnCloseTab(object s, EventArgs e)
        {
            if (tabs.SelectedTab == null) return;

            TabPage p = tabs.SelectedTab;
            tabs.TabPages.Remove(p);
            p.Dispose();

            if (!applyingState)
                SaveLastSession();
        }


        // Tear down and recreate the selected event log tab.
        // Useful after re-authenticating to a remote server.
        // Note: local Security log rights are checked per process,
        // so that one needs an elevated restart, not a reconnect.
        private void ReconnectTab()
        {
            EventLogPage p = tabs.SelectedTab as EventLogPage;
            if (p == null) return;

            int idx = tabs.SelectedIndex;
            string machine = p.Machine;
            string logName = p.LogName;

            tabs.TabPages.Remove(p);
            p.Dispose();

            EventLogPage np = new EventLogPage(machine, logName);
            tabs.TabPages.Insert(idx, np);
            tabs.SelectedIndex = idx;

            AppendBatch(np, np.PollNew());
        }


        private void CloseAllTabs()
        {
            while (tabs.TabPages.Count > 0)
            {
                TabPage p = tabs.TabPages[0];
                tabs.TabPages.RemoveAt(0);
                p.Dispose();
            }

            if (!applyingState)
                SaveLastSession();
        }


        private void OnClear(object s, EventArgs e)
        {
            LogPage p = tabs.SelectedTab as LogPage;
            if (p == null) return;

            p.Lines.Clear();
            p.View.Items.Clear();
        }


        private void OnPause(object s, EventArgs e)
        {
            paused = !paused;
            pauseItem.Checked = paused;
            Text = paused ? "LogTail  [PAUSED]" : "LogTail";
        }


        // ========================================================
        // MARKERS & FILTERS DIALOG
        // ========================================================

        private void OnConfig(object s, EventArgs e)
        {
            StringBuilder sb = new StringBuilder();

            foreach (Marker m in markers)
                sb.Append(m.Back.Name).Append("=")
                  .Append(m.Pattern).Append("\r\n");

            ConfigForm cf = new ConfigForm(sb.ToString(), includeStr, excludeStr);

            if (cf.ShowDialog(this) != DialogResult.OK) return;

            ParseMarkers(cf.MarkerBox.Text);
            SetFilters(cf.IncludeBox.Text.Trim(), cf.ExcludeBox.Text.Trim());

            // Recolor / refilter everything immediately.
            ApplySettingsToAllTabs();

            SaveLastSession();
        }


        private void ParseMarkers(string text)
        {
            markers.Clear();

            string[] markerLines =
                text.Replace("\r\n", "\n").Split('\n');

            foreach (string raw in markerLines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0 || eq == line.Length - 1) continue;

                AddMarker(
                    line.Substring(0, eq).Trim(),
                    line.Substring(eq + 1).Trim());

                if (markers.Count >= 20) break;
            }
        }


        private void SetFilters(string inc, string exc)
        {
            includeStr = inc;
            excludeStr = exc;
            includeRx = null;
            excludeRx = null;

            try
            {
                if (inc.Length > 0)
                    includeRx = new Regex(inc, RegexOptions.IgnoreCase);
            }
            catch (Exception) { }

            try
            {
                if (exc.Length > 0)
                    excludeRx = new Regex(exc, RegexOptions.IgnoreCase);
            }
            catch (Exception) { }
        }


        // ========================================================
        // INI  (same format as v1, plus eventserver= lines)
        // ========================================================

        private Dictionary<string, List<string>> ReadIni()
        {
            Dictionary<string, List<string>> ini =
                new Dictionary<string, List<string>>();

            if (!File.Exists(iniPath)) return ini;

            string section = "";

            try
            {
                foreach (string raw in File.ReadAllLines(iniPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2);

                        if (!ini.ContainsKey(section))
                            ini[section] = new List<string>();
                    }
                    else if (section.Length > 0)
                    {
                        ini[section].Add(line);
                    }
                }
            }
            catch (Exception) { }

            return ini;
        }


        private void WriteIni(Dictionary<string, List<string>> ini)
        {
            StringBuilder sb = new StringBuilder();

            foreach (KeyValuePair<string, List<string>> kv in ini)
            {
                sb.Append("[").Append(kv.Key).Append("]\r\n");

                foreach (string line in kv.Value)
                    sb.Append(line).Append("\r\n");

                sb.Append("\r\n");
            }

            try { File.WriteAllText(iniPath, sb.ToString()); }
            catch (Exception) { }
        }


        private List<string> CurrentStateLines()
        {
            List<string> lines = new List<string>();
            List<string> servers = new List<string>();

            foreach (TabPage p0 in tabs.TabPages)
            {
                TailPage tp = p0 as TailPage;

                if (tp != null)
                {
                    lines.Add("file=" + tp.Tail.Path);
                    continue;
                }

                EventLogPage ep = p0 as EventLogPage;

                if (ep != null && !servers.Contains(ep.Machine))
                    servers.Add(ep.Machine);
            }

            foreach (string sv in servers)
                lines.Add("eventserver=" + sv);

            foreach (Marker m in markers)
                lines.Add("marker=" + m.Back.Name + "=" + m.Pattern);

            lines.Add("include=" + includeStr);
            lines.Add("exclude=" + excludeStr);

            return lines;
        }


        private void ApplyStateLines(List<string> lines)
        {
            applyingState = true;
            timer.Stop();

            try
            {
                while (tabs.TabPages.Count > 0)
                {
                    TabPage p = tabs.TabPages[0];
                    tabs.TabPages.RemoveAt(0);
                    p.Dispose();
                }

                markers.Clear();
                includeStr = "";
                excludeStr = "";
                includeRx = null;
                excludeRx = null;

                List<string> files = new List<string>();
                List<string> servers = new List<string>();

                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq);
                    string val = line.Substring(eq + 1);

                    if (key == "file")
                    {
                        files.Add(val);
                    }
                    else if (key == "eventserver")
                    {
                        servers.Add(val);
                    }
                    else if (key == "marker")
                    {
                        int eq2 = val.IndexOf('=');

                        if (eq2 > 0 && markers.Count < 20)
                            AddMarker(
                                val.Substring(0, eq2),
                                val.Substring(eq2 + 1));
                    }
                    else if (key == "include")
                    {
                        includeStr = val;
                    }
                    else if (key == "exclude")
                    {
                        excludeStr = val;
                    }
                }

                if (markers.Count == 0)
                    DefaultMarkers();

                SetFilters(includeStr, excludeStr);

                foreach (string file in files)
                    if (File.Exists(file))
                        OpenLog(file);

                // Reconnect event log tabs with the current Windows login.
                // If that fails, the tab shows the error and File > Connect
                // Server Event Logs can be used to re-authenticate.
                foreach (string sv in servers)
                    AddEventLogTabs(sv);
            }
            finally
            {
                applyingState = false;
                timer.Start();
            }

            SaveLastSession();
        }


        private void SaveLastSession()
        {
            if (applyingState) return;

            Dictionary<string, List<string>> ini = ReadIni();
            ini["last"] = CurrentStateLines();
            WriteIni(ini);
        }


        private void LoadLastSession()
        {
            Dictionary<string, List<string>> ini = ReadIni();

            if (ini.ContainsKey("last"))
                ApplyStateLines(ini["last"]);
        }


        // ========================================================
        // PROFILES
        // ========================================================

        private void RebuildProfilesMenu()
        {
            profilesMenu.DropDownItems.Clear();

            profilesMenu.DropDownItems.Add(
                "&Save Profile As...", null, OnSaveProfile);

            profilesMenu.DropDownItems.Add(new ToolStripSeparator());

            Dictionary<string, List<string>> ini = ReadIni();

            foreach (string name in ini.Keys)
            {
                if (name == "last") continue;

                ToolStripMenuItem item = new ToolStripMenuItem(name);
                string captured = name;

                item.Click += delegate(object s, EventArgs e)
                {
                    Dictionary<string, List<string>> ini2 = ReadIni();

                    if (ini2.ContainsKey(captured))
                        ApplyStateLines(ini2[captured]);
                };

                profilesMenu.DropDownItems.Add(item);
            }
        }


        private void OnSaveProfile(object s, EventArgs e)
        {
            TextPrompt tp = new TextPrompt(
                "Save Profile", "Profile name:", "");

            if (tp.ShowDialog(this) != DialogResult.OK) return;

            string name = tp.Box.Text.Trim();
            if (name.Length == 0 || name == "last") return;

            Dictionary<string, List<string>> ini = ReadIni();
            ini[name] = CurrentStateLines();
            WriteIni(ini);

            RebuildProfilesMenu();
        }


        // ========================================================
        // CLOSE
        // ========================================================

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveLastSession();
            base.OnFormClosing(e);
        }
    }
}