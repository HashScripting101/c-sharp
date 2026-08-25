// LogTail.cs
// GxTail-style log tailer in one C# file.
//
// Build:
// C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//     /target:winexe
//     /out:LogTail.exe
//     /reference:System.Windows.Forms.dll
//     /reference:System.Drawing.dll
//     LogTail.cs
//
// Features:
// - Multiple log tabs
// - Live tailing
// - Log rollover / truncation detection
// - Up to 20 color markers
// - Regex or plain-text matching
// - Include / exclude filters
// - Saved profiles
// - Last session restoration
// - Pause
// - Clear
// - Drag and drop
// - Word wrap (saved between sessions)
// - Copy and Find / Find Next / Find Previous
// - Add selected text to include or exclude filter
// - Enable / disable markers and filters without clearing them
// - Marker changes immediately reprocess displayed logs

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LogTailApp
{
    // ============================================================
    // MARKER
    // ============================================================

    public class Marker
    {
        public string Pattern = "";
        public Color Color = Color.Gainsboro;
        public Regex Rx;

        public void Compile()
        {
            Rx = null;

            if (Pattern.Length == 0)
                return;

            try
            {
                // First try the pattern as a real regular expression.
                Rx = new Regex(
                    Pattern,
                    RegexOptions.IgnoreCase
                );
            }
            catch (Exception)
            {
                // If regex parsing fails, treat it as plain text.
                Rx = new Regex(
                    Regex.Escape(Pattern),
                    RegexOptions.IgnoreCase
                );
            }
        }
    }


    // ============================================================
    // LOG FILE TAIL READER
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

                if (fromStart)
                {
                    pos = 0;
                }
                else
                {
                    // Preload the last 64 KB of the file.
                    pos = Math.Max(0, len - 65536);
                }
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
                using (
                    FileStream fs = new FileStream(
                        Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete
                    )
                )
                {
                    // File was truncated or rolled over.
                    if (fs.Length < pos)
                    {
                        pos = 0;
                        remainder = "";
                    }

                    // Nothing new.
                    if (fs.Length == pos)
                        return lines;

                    fs.Seek(pos, SeekOrigin.Begin);

                    long want = fs.Length - pos;

                    // Prevent a huge read from freezing the UI.
                    if (want > 1048576)
                    {
                        fs.Seek(
                            fs.Length - 1048576,
                            SeekOrigin.Begin
                        );

                        want = 1048576;
                        remainder = "";
                    }

                    byte[] buf = new byte[(int)want];

                    int n = fs.Read(
                        buf,
                        0,
                        buf.Length
                    );

                    pos = fs.Position;

                    string s =
                        remainder +
                        Encoding.Default.GetString(
                            buf,
                            0,
                            n
                        );

                    s = s
                        .Replace("\r\n", "\n")
                        .Replace('\r', '\n');

                    string[] parts = s.Split('\n');

                    for (
                        int i = 0;
                        i < parts.Length - 1;
                        i++
                    )
                    {
                        lines.Add(parts[i]);
                    }

                    remainder =
                        parts[parts.Length - 1];
                }
            }
            catch (Exception)
            {
                // Ignore temporary file access problems.
                // The next timer cycle will try again.
            }

            return lines;
        }
    }


    // ============================================================
    // TAB PAGE FOR EACH LOG FILE
    // ============================================================

    public class TailPage : TabPage
    {
        public RichTextBox Box;
        public Tail Tail;

        public TailPage(string path)
        {
            Text =
                System.IO.Path.GetFileName(path);

            ToolTipText = path;

            Tail = new Tail(
                path,
                false
            );

            Box = new RichTextBox();

            Box.Dock =
                DockStyle.Fill;

            Box.ReadOnly =
                true;

            Box.BackColor =
                Color.FromArgb(
                    18,
                    18,
                    18
                );

            Box.ForeColor =
                Color.Gainsboro;

            Box.Font =
                new Font(
                    "Consolas",
                    9.5f
                );

            Box.WordWrap =
                false;

            Box.HideSelection =
                false;

            Box.DetectUrls =
                false;

            Controls.Add(Box);
        }
    }


    // ============================================================
    // MARKER / FILTER CONFIGURATION WINDOW
    // ============================================================

    public class ConfigForm : Form
    {
        public TextBox MarkerBox =
            new TextBox();

        public TextBox IncludeBox =
            new TextBox();

        public TextBox ExcludeBox =
            new TextBox();


        public ConfigForm(
            string markers,
            string include,
            string exclude
        )
        {
            Text =
                "Markers & Filters";

            Width = 700;
            Height = 600;

            StartPosition =
                FormStartPosition.CenterParent;

            FormBorderStyle =
                FormBorderStyle.FixedDialog;

            MaximizeBox = false;
            MinimizeBox = false;


            // ----------------------------------------------------
            // Marker instructions
            // ----------------------------------------------------

            Label l1 = new Label();

            l1.Text =
                "Markers - one per line, format  Color=pattern   (up to 20 used).\r\n" +
                "Colors: Red Orange Yellow Lime Cyan Magenta Violet White Gray Pink\r\n" +
                "        LightGreen SkyBlue Gold Salmon Khaki or any .NET color name.\r\n" +
                "Pattern can be regex or plain text. First matching marker wins.";

            l1.SetBounds(
                10,
                8,
                660,
                65
            );

            Controls.Add(l1);


            // ----------------------------------------------------
            // Marker textbox
            // ----------------------------------------------------

            MarkerBox.Multiline = true;

            MarkerBox.ScrollBars =
                ScrollBars.Both;

            MarkerBox.WordWrap = false;

            MarkerBox.Font =
                new Font(
                    "Consolas",
                    10f
                );

            MarkerBox.SetBounds(
                10,
                75,
                660,
                330
            );

            MarkerBox.Text =
                markers;

            Controls.Add(MarkerBox);


            // ----------------------------------------------------
            // Include filter
            // ----------------------------------------------------

            Label l2 = new Label();

            l2.Text =
                "Include filter (regex, blank = everything):";

            l2.SetBounds(
                10,
                415,
                400,
                18
            );

            Controls.Add(l2);


            IncludeBox.SetBounds(
                10,
                437,
                660,
                24
            );

            IncludeBox.Text =
                include;

            Controls.Add(IncludeBox);


            // ----------------------------------------------------
            // Exclude filter
            // ----------------------------------------------------

            Label l3 = new Label();

            l3.Text =
                "Exclude filter (regex, blank = nothing):";

            l3.SetBounds(
                10,
                470,
                400,
                18
            );

            Controls.Add(l3);


            ExcludeBox.SetBounds(
                10,
                492,
                660,
                24
            );

            ExcludeBox.Text =
                exclude;

            Controls.Add(ExcludeBox);


            // ----------------------------------------------------
            // OK
            // ----------------------------------------------------

            Button ok = new Button();

            ok.Text =
                "OK";

            ok.DialogResult =
                DialogResult.OK;

            ok.SetBounds(
                500,
                525,
                75,
                26
            );

            Controls.Add(ok);


            // ----------------------------------------------------
            // Cancel
            // ----------------------------------------------------

            Button cancel =
                new Button();

            cancel.Text =
                "Cancel";

            cancel.DialogResult =
                DialogResult.Cancel;

            cancel.SetBounds(
                590,
                525,
                75,
                26
            );

            Controls.Add(cancel);


            AcceptButton = ok;
            CancelButton = cancel;
        }
    }


    // ============================================================
    // FIND WINDOW
    // ============================================================

    public class FindForm : Form
    {
        public TextBox FindBox =
            new TextBox();


        public FindForm(
            string initialText
        )
        {
            Text =
                "Find";

            Width = 430;
            Height = 145;

            StartPosition =
                FormStartPosition.CenterParent;

            FormBorderStyle =
                FormBorderStyle.FixedDialog;

            MaximizeBox = false;
            MinimizeBox = false;


            Label label =
                new Label();

            label.Text =
                "Find text:";

            label.SetBounds(
                10,
                12,
                390,
                18
            );

            Controls.Add(label);


            FindBox.SetBounds(
                10,
                34,
                395,
                24
            );

            FindBox.Text =
                initialText;

            Controls.Add(FindBox);


            Button find =
                new Button();

            find.Text =
                "Find Next";

            find.DialogResult =
                DialogResult.OK;

            find.SetBounds(
                240,
                70,
                80,
                26
            );

            Controls.Add(find);


            Button cancel =
                new Button();

            cancel.Text =
                "Cancel";

            cancel.DialogResult =
                DialogResult.Cancel;

            cancel.SetBounds(
                330,
                70,
                75,
                26
            );

            Controls.Add(cancel);


            AcceptButton = find;
            CancelButton = cancel;

            Shown +=
                delegate(
                    object s,
                    EventArgs e
                )
                {
                    FindBox.Focus();
                    FindBox.SelectAll();
                };
        }
    }


    // ============================================================
    // MAIN APPLICATION WINDOW
    // ============================================================

    public class MainForm : Form
    {
        private TabControl tabs =
            new TabControl();

        private Timer timer =
            new Timer();

        private List<Marker> markers =
            new List<Marker>();

        private Regex includeRx;
        private Regex excludeRx;

        private string includeStr = "";
        private string excludeStr = "";

        private bool paused = false;

        private bool wordWrap = false;
        private bool filtersEnabled = true;
        private bool markersEnabled = true;

        private string findText = "";

        // Prevent session saves while we are
        // in the middle of restoring a profile/session.
        private bool applyingState = false;

        private ToolStripMenuItem pauseItem;
        private ToolStripMenuItem profilesMenu;
        private ToolStripMenuItem wordWrapItem;
        private ToolStripMenuItem filtersEnabledItem;
        private ToolStripMenuItem markersEnabledItem;

        private string iniPath;


        // ========================================================
        // PROGRAM ENTRY POINT
        // ========================================================

        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();

            Application.SetCompatibleTextRenderingDefault(
                false
            );

            MainForm f =
                new MainForm();

            foreach (string a in args)
            {
                if (File.Exists(a))
                {
                    f.OpenLog(a);
                }
            }

            Application.Run(f);
        }


        // ========================================================
        // CONSTRUCTOR
        // ========================================================

        public MainForm()
        {
            Text =
                "LogTail";

            Width = 1000;
            Height = 650;

            StartPosition =
                FormStartPosition.CenterScreen;


            iniPath =
                Path.Combine(
                    Path.GetDirectoryName(
                        Application.ExecutablePath
                    ),
                    "LogTail.ini"
                );


            // ====================================================
            // MENU BAR
            // ====================================================

            MenuStrip menu =
                new MenuStrip();


            // ----------------------------------------------------
            // File menu
            // ----------------------------------------------------

            ToolStripMenuItem file =
                new ToolStripMenuItem(
                    "&File"
                );


            file.DropDownItems.Add(
                "&Open Log...\tCtrl+O",
                null,
                OnOpen
            );


            file.DropDownItems.Add(
                "&Close Tab\tCtrl+W",
                null,
                OnCloseTab
            );


            file.DropDownItems.Add(
                "C&lear View",
                null,
                OnClear
            );


            file.DropDownItems.Add(
                new ToolStripSeparator()
            );


            file.DropDownItems.Add(
                "E&xit",
                null,
                delegate(
                    object s,
                    EventArgs e
                )
                {
                    Close();
                }
            );


            menu.Items.Add(file);


            // ----------------------------------------------------
            // Edit menu
            // ----------------------------------------------------

            ToolStripMenuItem edit =
                new ToolStripMenuItem(
                    "&Edit"
                );


            edit.DropDownItems.Add(
                "&Copy\tCtrl+C",
                null,
                OnCopy
            );


            edit.DropDownItems.Add(
                new ToolStripSeparator()
            );


            edit.DropDownItems.Add(
                "&Find...\tCtrl+F",
                null,
                OnFind
            );


            edit.DropDownItems.Add(
                "Find &Next\tF3",
                null,
                OnFindNext
            );


            edit.DropDownItems.Add(
                "Find &Previous\tShift+F3",
                null,
                OnFindPrevious
            );


            edit.DropDownItems.Add(
                "Find &Selected Text",
                null,
                OnFindSelected
            );


            edit.DropDownItems.Add(
                new ToolStripSeparator()
            );


            edit.DropDownItems.Add(
                "&Markers && Filters...\tCtrl+M",
                null,
                OnConfig
            );


            markersEnabledItem =
                new ToolStripMenuItem(
                    "Enable &Markers",
                    null,
                    OnToggleMarkers
                );

            markersEnabledItem.CheckOnClick =
                true;

            markersEnabledItem.Checked =
                true;

            edit.DropDownItems.Add(
                markersEnabledItem
            );


            filtersEnabledItem =
                new ToolStripMenuItem(
                    "Enable Fi&lter\tCtrl+F5",
                    null,
                    OnToggleFilters
                );

            filtersEnabledItem.CheckOnClick =
                true;

            filtersEnabledItem.Checked =
                true;

            edit.DropDownItems.Add(
                filtersEnabledItem
            );


            edit.DropDownItems.Add(
                "Add Selected Text to &Include Filter\tAlt+F",
                null,
                OnAddSelectedToInclude
            );


            edit.DropDownItems.Add(
                "Add Selected Text to E&xclude Filter\tAlt+X",
                null,
                OnAddSelectedToExclude
            );


            menu.Items.Add(edit);


            // ----------------------------------------------------
            // Options menu
            // ----------------------------------------------------

            ToolStripMenuItem options =
                new ToolStripMenuItem(
                    "&Options"
                );


            wordWrapItem =
                new ToolStripMenuItem(
                    "&Word Wrap\tCtrl+Shift+W",
                    null,
                    OnWordWrap
                );

            wordWrapItem.CheckOnClick =
                true;

            options.DropDownItems.Add(
                wordWrapItem
            );


            menu.Items.Add(options);


            // ----------------------------------------------------
            // Settings menu
            // ----------------------------------------------------

            ToolStripMenuItem view =
                new ToolStripMenuItem(
                    "&Settings"
                );


            pauseItem =
                new ToolStripMenuItem(
                    "&Pause\tCtrl+P",
                    null,
                    OnPause
                );


            pauseItem.CheckOnClick =
                false;


            view.DropDownItems.Add(
                pauseItem
            );


            menu.Items.Add(view);


            // ----------------------------------------------------
            // Profiles menu
            // ----------------------------------------------------

            profilesMenu =
                new ToolStripMenuItem(
                    "&Profiles"
                );


            menu.Items.Add(
                profilesMenu
            );


            RebuildProfilesMenu();


            // ====================================================
            // TAB CONTROL
            // ====================================================

            Controls.Add(tabs);

            tabs.Dock =
                DockStyle.Fill;

            tabs.ShowToolTips =
                true;


            // ====================================================
            // MENU
            // ====================================================

            Controls.Add(menu);

            MainMenuStrip =
                menu;


            // ====================================================
            // DRAG AND DROP
            // ====================================================

            AllowDrop = true;


            DragEnter +=
                delegate(
                    object s,
                    DragEventArgs e
                )
                {
                    if (
                        e.Data.GetDataPresent(
                            DataFormats.FileDrop
                        )
                    )
                    {
                        e.Effect =
                            DragDropEffects.Copy;
                    }
                };


            DragDrop +=
                delegate(
                    object s,
                    DragEventArgs e
                )
                {
                    string[] files =
                        (string[])e.Data.GetData(
                            DataFormats.FileDrop
                        );

                    foreach (
                        string f in files
                    )
                    {
                        if (File.Exists(f))
                        {
                            OpenLog(f);
                        }
                    }
                };


            // ====================================================
            // KEYBOARD SHORTCUTS
            // ====================================================

            KeyPreview = true;


            KeyDown +=
                delegate(
                    object s,
                    KeyEventArgs e
                )
                {
                    bool handled =
                        true;


                    if (
                        e.Control &&
                        e.Shift &&
                        e.KeyCode == Keys.W
                    )
                    {
                        OnWordWrap(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Control &&
                        e.KeyCode == Keys.O
                    )
                    {
                        OnOpen(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Control &&
                        !e.Shift &&
                        e.KeyCode == Keys.W
                    )
                    {
                        OnCloseTab(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Control &&
                        e.KeyCode == Keys.F
                    )
                    {
                        OnFind(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Control &&
                        e.KeyCode == Keys.C
                    )
                    {
                        OnCopy(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Control &&
                        e.KeyCode == Keys.M
                    )
                    {
                        OnConfig(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Control &&
                        e.KeyCode == Keys.P
                    )
                    {
                        OnPause(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Control &&
                        e.KeyCode == Keys.F5
                    )
                    {
                        OnToggleFilters(
                            null,
                            null
                        );
                    }

                    else if (
                        e.KeyCode == Keys.F3 &&
                        e.Shift
                    )
                    {
                        OnFindPrevious(
                            null,
                            null
                        );
                    }

                    else if (
                        e.KeyCode == Keys.F3
                    )
                    {
                        OnFindNext(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Alt &&
                        e.KeyCode == Keys.F
                    )
                    {
                        OnAddSelectedToInclude(
                            null,
                            null
                        );
                    }

                    else if (
                        e.Alt &&
                        e.KeyCode == Keys.X
                    )
                    {
                        OnAddSelectedToExclude(
                            null,
                            null
                        );
                    }

                    else
                    {
                        handled = false;
                    }


                    if (handled)
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                };


            // ====================================================
            // INITIAL SETTINGS
            // ====================================================

            DefaultMarkers();

            LoadLastSession();


            // ====================================================
            // LIVE LOG TIMER
            // ====================================================

            timer.Interval =
                300;

            timer.Tick +=
                OnTick;

            timer.Start();
        }


        // ========================================================
        // DEFAULT MARKERS
        // ========================================================

        private void DefaultMarkers()
        {
            markers.Clear();


            AddMarker(
                "Red",
                "ERROR|FATAL|SEVERE"
            );


            AddMarker(
                "Magenta",
                "Exception|Traceback"
            );


            AddMarker(
                "Yellow",
                "WARN"
            );


            AddMarker(
                "Cyan",
                "timeout|timed out|retry"
            );


            AddMarker(
                "Lime",
                "success|completed|started"
            );
        }


        // ========================================================
        // ADD MARKER
        // ========================================================

        private void AddMarker(
            string colorName,
            string pattern
        )
        {
            Marker m =
                new Marker();

            m.Pattern =
                pattern;

            m.Color =
                Color.FromName(
                    colorName
                );


            if (!m.Color.IsKnownColor)
            {
                m.Color =
                    Color.Gainsboro;
            }


            m.Compile();

            markers.Add(m);
        }


        // ========================================================
        // GET COLOR FOR A LINE
        // ========================================================

        private Color GetLineColor(
            string line
        )
        {
            if (!markersEnabled)
            {
                return Color.Gainsboro;
            }


            foreach (
                Marker m in markers
            )
            {
                if (
                    m.Rx != null &&
                    m.Rx.IsMatch(line)
                )
                {
                    return m.Color;
                }
            }

            return Color.Gainsboro;
        }


        // ========================================================
        // CHECK FILTERS
        // ========================================================

        private bool LinePassesFilters(
            string line
        )
        {
            if (!filtersEnabled)
            {
                return true;
            }


            if (
                includeRx != null &&
                !includeRx.IsMatch(line)
            )
            {
                return false;
            }


            if (
                excludeRx != null &&
                excludeRx.IsMatch(line)
            )
            {
                return false;
            }


            return true;
        }


        // ========================================================
        // APPEND ONE COLORED LINE
        // ========================================================

        private void AppendColoredLine(
            RichTextBox box,
            string line
        )
        {
            if (!LinePassesFilters(line))
            {
                return;
            }


            Color c =
                GetLineColor(line);


            box.SelectionStart =
                box.TextLength;

            box.SelectionLength =
                0;

            box.SelectionColor =
                c;


            box.AppendText(
                line +
                Environment.NewLine
            );
        }


        // ========================================================
        // LIVE TAILING
        // ========================================================

        private void OnTick(
            object s,
            EventArgs e
        )
        {
            foreach (
                TabPage p0 in tabs.TabPages
            )
            {
                TailPage p =
                    p0 as TailPage;


                if (p == null)
                {
                    continue;
                }


                List<string> lines =
                    p.Tail.ReadNew();


                if (lines.Count == 0)
                {
                    continue;
                }


                RichTextBox box =
                    p.Box;


                box.SuspendLayout();


                foreach (
                    string line in lines
                )
                {
                    AppendColoredLine(
                        box,
                        line
                    );
                }


                // Keep memory usage under control.
                if (
                    box.TextLength >
                    1500000
                )
                {
                    box.ReadOnly =
                        false;

                    box.Select(
                        0,
                        400000
                    );

                    box.SelectedText =
                        "";

                    box.ReadOnly =
                        true;
                }


                if (!paused)
                {
                    box.SelectionStart =
                        box.TextLength;

                    box.SelectionLength =
                        0;

                    box.ScrollToCaret();
                }


                box.ResumeLayout();
            }
        }


        // ========================================================
        // RELOAD ALL OPEN LOGS
        //
        // THIS IS THE IMPORTANT FIX.
        //
        // Whenever markers or filters change, reset each Tail
        // reader and read the file again so existing lines receive
        // the new colors immediately.
        // ========================================================

        private void ReloadAllTabs()
        {
            timer.Stop();


            try
            {
                foreach (
                    TabPage p0 in tabs.TabPages
                )
                {
                    TailPage p =
                        p0 as TailPage;


                    if (p == null)
                    {
                        continue;
                    }


                    string path =
                        p.Tail.Path;


                    // Clear existing formatted text.
                    p.Box.Clear();


                    // Reset the reader.
                    //
                    // false means load the last 64 KB,
                    // which matches normal log opening behavior.
                    p.Tail =
                        new Tail(
                            path,
                            false
                        );
                }


                // Immediately process the files again
                // using the new marker/filter settings.
                OnTick(
                    null,
                    EventArgs.Empty
                );
            }
            finally
            {
                timer.Start();
            }
        }


        // ========================================================
        // OPEN LOG
        // ========================================================

        public void OpenLog(
            string path
        )
        {
            if (!File.Exists(path))
            {
                return;
            }


            // If already open, select the existing tab.
            foreach (
                TabPage p0 in tabs.TabPages
            )
            {
                TailPage existing =
                    p0 as TailPage;


                if (
                    existing != null &&
                    string.Equals(
                        existing.Tail.Path,
                        path,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    tabs.SelectedTab =
                        existing;

                    return;
                }
            }


            TailPage p =
                new TailPage(path);


            ApplyDisplayOptions(
                p.Box
            );


            tabs.TabPages.Add(p);

            tabs.SelectedTab =
                p;


            // Read its initial contents immediately.
            OnTick(
                null,
                EventArgs.Empty
            );


            if (!applyingState)
            {
                SaveLastSession();
            }
        }


        // ========================================================
        // OPEN FILE DIALOG
        // ========================================================

        private void OnOpen(
            object s,
            EventArgs e
        )
        {
            OpenFileDialog d =
                new OpenFileDialog();


            d.Filter =
                "Logs (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*";


            d.Multiselect =
                true;


            if (
                d.ShowDialog(this) ==
                DialogResult.OK
            )
            {
                foreach (
                    string f in d.FileNames
                )
                {
                    OpenLog(f);
                }
            }
        }


        // ========================================================
        // CLOSE TAB
        // ========================================================

        private void OnCloseTab(
            object s,
            EventArgs e
        )
        {
            if (
                tabs.SelectedTab != null
            )
            {
                tabs.TabPages.Remove(
                    tabs.SelectedTab
                );


                if (!applyingState)
                {
                    SaveLastSession();
                }
            }
        }


        // ========================================================
        // CLEAR VIEW
        // ========================================================

        private void OnClear(
            object s,
            EventArgs e
        )
        {
            TailPage p =
                tabs.SelectedTab
                as TailPage;


            if (p != null)
            {
                p.Box.Clear();
            }
        }


        // ========================================================
        // PAUSE AUTO-SCROLL
        // ========================================================

        private void OnPause(
            object s,
            EventArgs e
        )
        {
            paused =
                !paused;


            pauseItem.Checked =
                paused;


            Text =
                paused
                ? "LogTail  [PAUSED]"
                : "LogTail";
        }


        // ========================================================
        // ACTIVE LOG VIEW
        // ========================================================

        private RichTextBox ActiveBox()
        {
            TailPage p =
                tabs.SelectedTab
                as TailPage;


            if (p == null)
            {
                return null;
            }


            return p.Box;
        }


        // ========================================================
        // COPY
        // ========================================================

        private void OnCopy(
            object s,
            EventArgs e
        )
        {
            RichTextBox box =
                ActiveBox();


            if (
                box != null &&
                box.SelectionLength > 0
            )
            {
                box.Copy();
            }
        }


        // ========================================================
        // FIND
        // ========================================================

        private void OnFind(
            object s,
            EventArgs e
        )
        {
            RichTextBox box =
                ActiveBox();


            if (box == null)
            {
                return;
            }


            string initial =
                findText;


            if (box.SelectionLength > 0)
            {
                initial =
                    box.SelectedText;
            }


            using (
                FindForm ff =
                    new FindForm(initial)
            )
            {
                if (
                    ff.ShowDialog(this) !=
                    DialogResult.OK
                )
                {
                    return;
                }


                findText =
                    ff.FindBox.Text;
            }


            FindInActiveView(
                true
            );
        }


        private void OnFindNext(
            object s,
            EventArgs e
        )
        {
            if (findText.Length == 0)
            {
                OnFind(
                    s,
                    e
                );

                return;
            }


            FindInActiveView(
                true
            );
        }


        private void OnFindPrevious(
            object s,
            EventArgs e
        )
        {
            if (findText.Length == 0)
            {
                OnFind(
                    s,
                    e
                );

                return;
            }


            FindInActiveView(
                false
            );
        }


        private void OnFindSelected(
            object s,
            EventArgs e
        )
        {
            RichTextBox box =
                ActiveBox();


            if (
                box == null ||
                box.SelectionLength == 0
            )
            {
                System.Media.SystemSounds.Beep.Play();

                return;
            }


            findText =
                box.SelectedText;


            FindInActiveView(
                true
            );
        }


        private void FindInActiveView(
            bool forward
        )
        {
            RichTextBox box =
                ActiveBox();


            if (
                box == null ||
                findText.Length == 0 ||
                box.TextLength == 0
            )
            {
                return;
            }


            string text =
                box.Text;

            int found = -1;


            if (forward)
            {
                int start =
                    box.SelectionStart +
                    box.SelectionLength;


                if (start < text.Length)
                {
                    found =
                        text.IndexOf(
                            findText,
                            start,
                            StringComparison.OrdinalIgnoreCase
                        );
                }


                // Wrap to the beginning.
                if (
                    found < 0 &&
                    start > 0
                )
                {
                    found =
                        text.IndexOf(
                            findText,
                            0,
                            StringComparison.OrdinalIgnoreCase
                        );
                }
            }
            else
            {
                int start =
                    box.SelectionStart - 1;


                if (start >= 0)
                {
                    found =
                        text.LastIndexOf(
                            findText,
                            start,
                            StringComparison.OrdinalIgnoreCase
                        );
                }


                // Wrap to the end.
                if (
                    found < 0 &&
                    text.Length > 0
                )
                {
                    found =
                        text.LastIndexOf(
                            findText,
                            text.Length - 1,
                            StringComparison.OrdinalIgnoreCase
                        );
                }
            }


            if (found < 0)
            {
                System.Media.SystemSounds.Beep.Play();

                return;
            }


            box.Select(
                found,
                findText.Length
            );

            box.ScrollToCaret();
            box.Focus();
        }


        // ========================================================
        // QUICK FILTER FROM SELECTED TEXT
        // ========================================================

        private string SelectedFilterText()
        {
            RichTextBox box =
                ActiveBox();


            if (
                box == null ||
                box.SelectionLength == 0
            )
            {
                return "";
            }


            string selected =
                box.SelectedText
                    .Replace(
                        "\r\n",
                        "\n"
                    );


            int nl =
                selected.IndexOf('\n');


            if (nl >= 0)
            {
                selected =
                    selected.Substring(
                        0,
                        nl
                    );
            }


            return selected.Trim();
        }


        private string AddLiteralToPattern(
            string existing,
            string literal
        )
        {
            string escaped =
                Regex.Escape(literal);


            if (existing.Length == 0)
            {
                return escaped;
            }


            return
                "(?:" +
                existing +
                ")|(?:" +
                escaped +
                ")";
        }


        private void OnAddSelectedToInclude(
            object s,
            EventArgs e
        )
        {
            string selected =
                SelectedFilterText();


            if (selected.Length == 0)
            {
                System.Media.SystemSounds.Beep.Play();

                return;
            }


            includeStr =
                AddLiteralToPattern(
                    includeStr,
                    selected
                );


            EnableFilters();

            SetFilters(
                includeStr,
                excludeStr
            );

            ReloadAllTabs();
            SaveLastSession();
        }


        private void OnAddSelectedToExclude(
            object s,
            EventArgs e
        )
        {
            string selected =
                SelectedFilterText();


            if (selected.Length == 0)
            {
                System.Media.SystemSounds.Beep.Play();

                return;
            }


            excludeStr =
                AddLiteralToPattern(
                    excludeStr,
                    selected
                );


            EnableFilters();

            SetFilters(
                includeStr,
                excludeStr
            );

            ReloadAllTabs();
            SaveLastSession();
        }


        private void EnableFilters()
        {
            filtersEnabled = true;


            if (filtersEnabledItem != null)
            {
                filtersEnabledItem.Checked =
                    true;
            }
        }


        // ========================================================
        // ENABLE / DISABLE MARKERS AND FILTERS
        // ========================================================

        private void OnToggleMarkers(
            object s,
            EventArgs e
        )
        {
            if (s == null)
            {
                markersEnabledItem.Checked =
                    !markersEnabledItem.Checked;
            }


            markersEnabled =
                markersEnabledItem.Checked;


            ReloadAllTabs();
            SaveLastSession();
        }


        private void OnToggleFilters(
            object s,
            EventArgs e
        )
        {
            if (s == null)
            {
                filtersEnabledItem.Checked =
                    !filtersEnabledItem.Checked;
            }


            filtersEnabled =
                filtersEnabledItem.Checked;


            ReloadAllTabs();
            SaveLastSession();
        }


        // ========================================================
        // WORD WRAP
        // ========================================================

        private void OnWordWrap(
            object s,
            EventArgs e
        )
        {
            if (s == null)
            {
                wordWrapItem.Checked =
                    !wordWrapItem.Checked;
            }


            wordWrap =
                wordWrapItem.Checked;


            foreach (
                TabPage p0 in tabs.TabPages
            )
            {
                TailPage p =
                    p0 as TailPage;


                if (p != null)
                {
                    ApplyDisplayOptions(
                        p.Box
                    );
                }
            }


            SaveLastSession();
        }


        private void ApplyDisplayOptions(
            RichTextBox box
        )
        {
            box.WordWrap =
                wordWrap;

            box.ScrollBars =
                wordWrap
                    ? RichTextBoxScrollBars.Vertical
                    : RichTextBoxScrollBars.Both;
        }


        // ========================================================
        // MARKERS & FILTERS
        // ========================================================

        private void OnConfig(
            object s,
            EventArgs e
        )
        {
            StringBuilder sb =
                new StringBuilder();


            foreach (
                Marker m in markers
            )
            {
                sb
                    .Append(
                        m.Color.Name
                    )
                    .Append("=")
                    .Append(
                        m.Pattern
                    )
                    .Append("\r\n");
            }


            ConfigForm cf =
                new ConfigForm(
                    sb.ToString(),
                    includeStr,
                    excludeStr
                );


            if (
                cf.ShowDialog(this) !=
                DialogResult.OK
            )
            {
                return;
            }


            ParseMarkers(
                cf.MarkerBox.Text
            );


            SetFilters(
                cf.IncludeBox.Text.Trim(),
                cf.ExcludeBox.Text.Trim()
            );


            // IMPORTANT:
            // Re-read all currently open files so the
            // marker changes apply immediately.
            ReloadAllTabs();


            // Persist marker/filter changes.
            SaveLastSession();
        }


        // ========================================================
        // PARSE MARKERS
        // ========================================================

        private void ParseMarkers(
            string text
        )
        {
            markers.Clear();


            string[] markerLines =
                text.Replace(
                    "\r\n",
                    "\n"
                ).Split('\n');


            foreach (
                string raw in markerLines
            )
            {
                string line =
                    raw.Trim();


                if (
                    line.Length == 0
                )
                {
                    continue;
                }


                int eq =
                    line.IndexOf('=');


                if (
                    eq <= 0 ||
                    eq == line.Length - 1
                )
                {
                    continue;
                }


                string colorName =
                    line
                        .Substring(
                            0,
                            eq
                        )
                        .Trim();


                string pattern =
                    line
                        .Substring(
                            eq + 1
                        )
                        .Trim();


                AddMarker(
                    colorName,
                    pattern
                );


                if (
                    markers.Count >= 20
                )
                {
                    break;
                }
            }
        }


        // ========================================================
        // SET FILTERS
        // ========================================================

        private void SetFilters(
            string inc,
            string exc
        )
        {
            includeStr =
                inc;

            excludeStr =
                exc;


            includeRx = null;
            excludeRx = null;


            try
            {
                if (
                    inc.Length > 0
                )
                {
                    includeRx =
                        new Regex(
                            inc,
                            RegexOptions.IgnoreCase
                        );
                }
            }
            catch (Exception)
            {
                // Invalid include regex = ignore it.
            }


            try
            {
                if (
                    exc.Length > 0
                )
                {
                    excludeRx =
                        new Regex(
                            exc,
                            RegexOptions.IgnoreCase
                        );
                }
            }
            catch (Exception)
            {
                // Invalid exclude regex = ignore it.
            }
        }


        // ========================================================
        // READ INI
        //
        // File format:
        //
        // [name]
        // file=...
        // marker=Red=ERROR
        // include=...
        // exclude=...
        // wordwrap=0|1
        // filtersenabled=0|1
        // markersenabled=0|1
        // ========================================================

        private Dictionary<string, List<string>>
            ReadIni()
        {
            Dictionary<string, List<string>> ini =
                new Dictionary<string, List<string>>();


            if (!File.Exists(iniPath))
            {
                return ini;
            }


            string section = "";


            try
            {
                foreach (
                    string raw in
                    File.ReadAllLines(iniPath)
                )
                {
                    string line =
                        raw.Trim();


                    if (
                        line.Length == 0
                    )
                    {
                        continue;
                    }


                    if (
                        line.StartsWith("[") &&
                        line.EndsWith("]")
                    )
                    {
                        section =
                            line.Substring(
                                1,
                                line.Length - 2
                            );


                        if (
                            !ini.ContainsKey(
                                section
                            )
                        )
                        {
                            ini[section] =
                                new List<string>();
                        }
                    }
                    else if (
                        section.Length > 0
                    )
                    {
                        ini[section].Add(
                            line
                        );
                    }
                }
            }
            catch (Exception)
            {
            }


            return ini;
        }


        // ========================================================
        // WRITE INI
        // ========================================================

        private void WriteIni(
            Dictionary<string, List<string>> ini
        )
        {
            StringBuilder sb =
                new StringBuilder();


            foreach (
                KeyValuePair<
                    string,
                    List<string>
                > kv in ini
            )
            {
                sb
                    .Append("[")
                    .Append(kv.Key)
                    .Append("]\r\n");


                foreach (
                    string line in kv.Value
                )
                {
                    sb
                        .Append(line)
                        .Append("\r\n");
                }


                sb.Append("\r\n");
            }


            try
            {
                File.WriteAllText(
                    iniPath,
                    sb.ToString()
                );
            }
            catch (Exception)
            {
            }
        }


        // ========================================================
        // CURRENT STATE
        // ========================================================

        private List<string>
            CurrentStateLines()
        {
            List<string> lines =
                new List<string>();


            foreach (
                TabPage p0 in tabs.TabPages
            )
            {
                TailPage p =
                    p0 as TailPage;


                if (p != null)
                {
                    lines.Add(
                        "file=" +
                        p.Tail.Path
                    );
                }
            }


            foreach (
                Marker m in markers
            )
            {
                lines.Add(
                    "marker=" +
                    m.Color.Name +
                    "=" +
                    m.Pattern
                );
            }


            lines.Add(
                "include=" +
                includeStr
            );


            lines.Add(
                "exclude=" +
                excludeStr
            );


            lines.Add(
                "wordwrap=" +
                (wordWrap ? "1" : "0")
            );


            lines.Add(
                "filtersenabled=" +
                (filtersEnabled ? "1" : "0")
            );


            lines.Add(
                "markersenabled=" +
                (markersEnabled ? "1" : "0")
            );


            return lines;
        }


        // ========================================================
        // APPLY SAVED STATE
        // ========================================================

        private void ApplyStateLines(
            List<string> lines
        )
        {
            applyingState =
                true;


            timer.Stop();


            try
            {
                while (
                    tabs.TabPages.Count > 0
                )
                {
                    tabs.TabPages.RemoveAt(
                        0
                    );
                }


                markers.Clear();


                includeStr = "";
                excludeStr = "";

                includeRx = null;
                excludeRx = null;

                wordWrap = false;
                filtersEnabled = true;
                markersEnabled = true;


                // First process marker/filter settings,
                // then open files.
                //
                // This guarantees that files are initially
                // displayed with the correct colors.

                List<string> files =
                    new List<string>();


                foreach (
                    string line in lines
                )
                {
                    int eq =
                        line.IndexOf('=');


                    if (
                        eq <= 0
                    )
                    {
                        continue;
                    }


                    string key =
                        line.Substring(
                            0,
                            eq
                        );


                    string val =
                        line.Substring(
                            eq + 1
                        );


                    if (
                        key == "file"
                    )
                    {
                        files.Add(val);
                    }

                    else if (
                        key == "marker"
                    )
                    {
                        int eq2 =
                            val.IndexOf('=');


                        if (
                            eq2 > 0 &&
                            markers.Count < 20
                        )
                        {
                            AddMarker(
                                val.Substring(
                                    0,
                                    eq2
                                ),
                                val.Substring(
                                    eq2 + 1
                                )
                            );
                        }
                    }

                    else if (
                        key == "include"
                    )
                    {
                        includeStr =
                            val;
                    }

                    else if (
                        key == "exclude"
                    )
                    {
                        excludeStr =
                            val;
                    }

                    else if (
                        key == "wordwrap"
                    )
                    {
                        wordWrap =
                            val == "1";
                    }

                    else if (
                        key == "filtersenabled"
                    )
                    {
                        filtersEnabled =
                            val != "0";
                    }

                    else if (
                        key == "markersenabled"
                    )
                    {
                        markersEnabled =
                            val != "0";
                    }
                }


                if (
                    markers.Count == 0
                )
                {
                    DefaultMarkers();
                }


                SetFilters(
                    includeStr,
                    excludeStr
                );


                wordWrapItem.Checked =
                    wordWrap;

                filtersEnabledItem.Checked =
                    filtersEnabled;

                markersEnabledItem.Checked =
                    markersEnabled;


                foreach (
                    string file in files
                )
                {
                    if (
                        File.Exists(file)
                    )
                    {
                        OpenLog(file);
                    }
                }
            }
            finally
            {
                applyingState =
                    false;

                timer.Start();
            }


            SaveLastSession();
        }


        // ========================================================
        // SAVE LAST SESSION
        // ========================================================

        private void SaveLastSession()
        {
            if (applyingState)
            {
                return;
            }


            Dictionary<string, List<string>> ini =
                ReadIni();


            ini["last"] =
                CurrentStateLines();


            WriteIni(ini);
        }


        // ========================================================
        // LOAD LAST SESSION
        // ========================================================

        private void LoadLastSession()
        {
            Dictionary<string, List<string>> ini =
                ReadIni();


            if (
                ini.ContainsKey("last")
            )
            {
                ApplyStateLines(
                    ini["last"]
                );
            }
        }


        // ========================================================
        // BUILD PROFILES MENU
        // ========================================================

        private void RebuildProfilesMenu()
        {
            profilesMenu
                .DropDownItems
                .Clear();


            profilesMenu.DropDownItems.Add(
                "&Save Profile As...",
                null,
                OnSaveProfile
            );


            profilesMenu.DropDownItems.Add(
                new ToolStripSeparator()
            );


            Dictionary<string, List<string>> ini =
                ReadIni();


            foreach (
                string name in ini.Keys
            )
            {
                if (
                    name == "last"
                )
                {
                    continue;
                }


                ToolStripMenuItem item =
                    new ToolStripMenuItem(
                        name
                    );


                string captured =
                    name;


                item.Click +=
                    delegate(
                        object s,
                        EventArgs e
                    )
                    {
                        Dictionary<
                            string,
                            List<string>
                        > ini2 =
                            ReadIni();


                        if (
                            ini2.ContainsKey(
                                captured
                            )
                        )
                        {
                            ApplyStateLines(
                                ini2[captured]
                            );
                        }
                    };


                profilesMenu
                    .DropDownItems
                    .Add(item);
            }
        }


        // ========================================================
        // SAVE PROFILE
        // ========================================================

        private void OnSaveProfile(
            object s,
            EventArgs e
        )
        {
            Form dlg =
                new Form();


            dlg.Text =
                "Save Profile";

            dlg.Width =
                340;

            dlg.Height =
                150;

            dlg.FormBorderStyle =
                FormBorderStyle.FixedDialog;

            dlg.StartPosition =
                FormStartPosition.CenterParent;

            dlg.MaximizeBox =
                false;

            dlg.MinimizeBox =
                false;


            Label l =
                new Label();


            l.Text =
                "Profile name:";


            l.SetBounds(
                10,
                10,
                300,
                18
            );


            dlg.Controls.Add(l);


            TextBox tb =
                new TextBox();


            tb.SetBounds(
                10,
                32,
                300,
                24
            );


            dlg.Controls.Add(tb);


            Button ok =
                new Button();


            ok.Text =
                "OK";


            ok.DialogResult =
                DialogResult.OK;


            ok.SetBounds(
                235,
                70,
                75,
                26
            );


            dlg.Controls.Add(ok);


            dlg.AcceptButton =
                ok;


            if (
                dlg.ShowDialog(this) ==
                    DialogResult.OK &&
                tb.Text.Trim().Length > 0
            )
            {
                Dictionary<
                    string,
                    List<string>
                > ini =
                    ReadIni();


                ini[
                    tb.Text.Trim()
                ] =
                    CurrentStateLines();


                WriteIni(ini);


                RebuildProfilesMenu();
            }
        }


        // ========================================================
        // APPLICATION CLOSING
        // ========================================================

        protected override void OnFormClosing(
            FormClosingEventArgs e
        )
        {
            SaveLastSession();

            base.OnFormClosing(e);
        }
    }
}
