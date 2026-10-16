// System-tray helper. Args: <serverPid>. Hides the server's console window, shows a tray icon with a menu,
// prints menu events ("open", "pause", "resume", "quit") on stdout, and reads "tip <text>" / "balloon <text>" lines on stdin.
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class PPTray
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    static NotifyIcon icon;
    static StreamWriter outw;
    static ToolStripMenuItem pauseItem;
    static bool paused;

    [STAThread]
    static void Main(string[] args)
    {
        int pid; if (args.Length > 0 && int.TryParse(args[0], out pid)) HideConsole(pid);
        outw = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Application.EnableVisualStyles();
        icon = new NotifyIcon { Visible = true, Text = "Pong Plotter", Icon = MakeIcon() };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Pong Plotter", null, (s, e) => Send("open"));
        pauseItem = new ToolStripMenuItem("Pause all targets", null, (s, e) => { paused = !paused; pauseItem.Text = paused ? "Resume all targets" : "Pause all targets"; Send(paused ? "pause" : "resume"); });
        menu.Items.Add(pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (s, e) => { Send("quit"); Shutdown(); });
        icon.ContextMenuStrip = menu;
        icon.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) Send("open"); };
        var t = new Thread(ReadStdin) { IsBackground = true }; t.Start();
        Application.Run();
    }
    static void HideConsole(int pid)
    {
        try { FreeConsole(); if (AttachConsole(pid)) { var w = GetConsoleWindow(); if (w != IntPtr.Zero) ShowWindow(w, 0); FreeConsole(); } } catch { }
    }
    static void Send(string s) { try { outw.WriteLine(s); } catch { } }
    static void Shutdown() { icon.Visible = false; icon.Dispose(); Environment.Exit(0); }
    static void ReadStdin()
    {
        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            string l = line;
            if (l.StartsWith("tip ")) icon.Text = l.Substring(4).Length > 62 ? l.Substring(4, 62) : l.Substring(4);
            else if (l.StartsWith("balloon ")) icon.ShowBalloonTip(5000, "Pong Plotter", l.Substring(8), ToolTipIcon.Warning);
        }
        Shutdown(); // server went away
    }
    static Icon MakeIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(new SolidBrush(Color.FromArgb(31, 122, 224)), 1, 1, 30, 30);
            using (var p = new Pen(Color.White, 3f)) g.DrawLines(p, new[] { new Point(5, 20), new Point(11, 20), new Point(14, 9), new Point(19, 24), new Point(22, 16), new Point(27, 16) });
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
