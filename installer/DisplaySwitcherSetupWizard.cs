using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Monitor Display Switcher 1.2.0 Setup (Mehedi Hasan Shihab)")]
[assembly: AssemblyDescription("Official Windows Setup Installer & Uninstaller for Monitor Display Switcher 1.2.0 (Mehedi Hasan Shihab)")]
[assembly: AssemblyCompany("Mehedi Hasan Shihab")]
[assembly: AssemblyProduct("Monitor Display Switcher Setup")]
[assembly: AssemblyCopyright("Copyright © 2026 Mehedi Hasan Shihab (sshihabb007)")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: ComVisible(false)]

namespace DisplayInputSwitcherInstaller
{
    public sealed class SetupInstallOptions
    {
        public string InstallDirectory { get; set; }
        public bool CreateDesktopShortcut { get; set; }
        public bool CreateStartMenuShortcuts { get; set; }
        public bool LaunchOnStartup { get; set; }
        public bool SkipRegistryAndShortcuts { get; set; }

        public SetupInstallOptions()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            InstallDirectory = Path.Combine(Path.Combine(localAppData, "Programs"), "Monitor Display Switcher");
            CreateDesktopShortcut = true;
            CreateStartMenuShortcuts = true;
            LaunchOnStartup = true;
            SkipRegistryAndShortcuts = false;
        }
    }

    public static class SetupTheme
    {
        public const string ProductTitle = "Monitor Display Switcher 1.2.0";
        public const string FullHeaderTitle = "Monitor Display Switcher 1.2.0 (Mehedi Hasan Shihab)";
        public const string VersionText = "1.2.0";
        public const string ExeFileName = "DisplayInputSwitcher.exe";
        public const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorDisplaySwitcher";
        public const string RunRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "DisplayInputSwitcher";

        [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static Icon _cachedIcon;

        public static Color WindowBg { get { return Color.FromArgb(11, 17, 32); } }
        public static Color CardBg { get { return Color.FromArgb(17, 26, 46); } }
        public static Color InputBg { get { return Color.FromArgb(15, 23, 42); } }
        public static Color BorderCol { get { return Color.FromArgb(38, 53, 77); } }
        public static Color PrimaryText { get { return Color.FromArgb(248, 250, 252); } }
        public static Color MutedText { get { return Color.FromArgb(148, 163, 184); } }
        public static Color AccentCyan { get { return Color.FromArgb(14, 165, 233); } }
        public static Color AccentViolet { get { return Color.FromArgb(124, 92, 255); } }
        public static Color AccentGreen { get { return Color.FromArgb(16, 185, 129); } }
        public static Color AccentRed { get { return Color.FromArgb(220, 38, 38); } }

        public static void ApplyDarkTitleBar(Form form)
        {
            if (form == null) return;
            EventHandler handler = delegate
            {
                try
                {
                    if (!form.IsHandleCreated) return;
                    int dark = 1;
                    DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
                    DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
                    int captionColor = (32 << 16) | (17 << 8) | 11;
                    DwmSetWindowAttribute(form.Handle, 35, ref captionColor, sizeof(int));
                    int textColor = (252 << 16) | (250 << 8) | 248;
                    DwmSetWindowAttribute(form.Handle, 36, ref textColor, sizeof(int));
                }
                catch
                {
                }
            };
            if (form.IsHandleCreated) handler(form, EventArgs.Empty);
            form.HandleCreated += handler;
            form.Shown += handler;
        }

        public static Icon LoadEmbeddedIcon()
        {
            if (_cachedIcon != null) return _cachedIcon;
            try
            {
                Stream s = typeof(SetupTheme).Assembly.GetManifestResourceStream("DisplayInputSwitcherInstaller.app.ico");
                if (s != null)
                {
                    using (s)
                    {
                        _cachedIcon = new Icon(s);
                        return _cachedIcon;
                    }
                }
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        public static Bitmap RenderBrandLogo(int size)
        {
            int dim = Math.Max(24, size);
            Bitmap bmp = new Bitmap(dim, dim);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                try
                {
                    Icon ico = new Icon(LoadEmbeddedIcon(), new Size(dim, dim));
                    g.DrawIcon(ico, new Rectangle(0, 0, dim, dim));
                    ico.Dispose();
                }
                catch
                {
                    g.DrawIcon(LoadEmbeddedIcon(), new Rectangle(0, 0, dim, dim));
                }
            }
            return bmp;
        }

        public static Button CreateStyledButton(string text, Color bg, Color fg, int width)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Size = new Size(width, 34);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                Math.Min(255, bg.R + 18),
                Math.Min(255, bg.G + 18),
                Math.Min(255, bg.B + 22));
            btn.BackColor = bg;
            btn.ForeColor = fg;
            btn.Font = new Font("Segoe UI Semibold", 9.2f);
            btn.Cursor = Cursors.Hand;
            return btn;
        }
    }

    public sealed class GradientProgressBarPanel : Panel
    {
        public int Percent { get; set; }
        public string Caption { get; set; }

        public GradientProgressBarPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Caption = "0%";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = ClientRectangle;
            using (SolidBrush bg = new SolidBrush(SetupTheme.InputBg))
            {
                g.FillRectangle(bg, rect);
            }

            int clamped = Math.Max(0, Math.Min(100, Percent));
            int fillW = (rect.Width * clamped) / 100;
            if (fillW > 1)
            {
                using (LinearGradientBrush grad = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(4, rect.Width), Math.Max(4, rect.Height)),
                    Color.FromArgb(34, 211, 238),
                    Color.FromArgb(124, 92, 255),
                    0f))
                {
                    g.FillRectangle(grad, 0, 0, fillW, rect.Height);
                }
            }

            using (Pen border = new Pen(SetupTheme.BorderCol, 1f))
            {
                g.DrawRectangle(border, 0, 0, rect.Width - 1, rect.Height - 1);
            }

            TextRenderer.DrawText(
                g,
                Caption ?? (clamped + "%"),
                new Font("Segoe UI Semibold", 8.8f),
                rect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    public static class SetupInstallerEngine
    {
        public static bool IsProcessElevated()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool CanWriteTargetDirectory(string targetDir)
        {
            try
            {
                string full = Path.GetFullPath(targetDir);
                Directory.CreateDirectory(full);
                string probe = Path.Combine(full, ".dis_setup_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void StopOwnedAppInDirectory(string installDir, Action<string> log)
        {
            string targetExe = Path.GetFullPath(Path.Combine(installDir, SetupTheme.ExeFileName));
            Process[] procs = Process.GetProcessesByName("DisplayInputSwitcher");
            for (int i = 0; i < procs.Length; i++)
            {
                Process p = procs[i];
                try
                {
                    string procPath = p.MainModule != null ? Path.GetFullPath(p.MainModule.FileName) : "";
                    if (string.Equals(procPath, targetExe, StringComparison.OrdinalIgnoreCase))
                    {
                        if (log != null) log("Stopping running DisplayInputSwitcher.exe instance (PID " + p.Id + ")...");
                        p.Kill();
                        p.WaitForExit(4000);
                    }
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        public static void ExecuteInstallation(SetupInstallOptions opts, Action<int, string> progressCallback)
        {
            string installRoot = Path.GetFullPath(opts.InstallDirectory.Trim());
            string rootWithSep = installRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? installRoot
                : (installRoot + Path.DirectorySeparatorChar);

            Action<int, string> report = delegate(int pct, string msg)
            {
                if (progressCallback != null) progressCallback(pct, msg);
            };

            report(5, "Preparing installation directory: " + installRoot);
            Directory.CreateDirectory(installRoot);

            StopOwnedAppInDirectory(installRoot, delegate(string msg) { report(12, msg); });

            Stream payloadStream = typeof(SetupInstallerEngine).Assembly.GetManifestResourceStream("DisplayInputSwitcherInstaller.payload.zip");
            if (payloadStream == null)
            {
                throw new InvalidOperationException("Embedded installer payload (DisplayInputSwitcherInstaller.payload.zip) is missing.");
            }

            long totalExtractedBytes = 0L;
            using (payloadStream)
            using (ZipArchive zip = new ZipArchive(payloadStream, ZipArchiveMode.Read))
            {
                int totalEntries = Math.Max(1, zip.Entries.Count);
                for (int idx = 0; idx < zip.Entries.Count; idx++)
                {
                    ZipArchiveEntry entry = zip.Entries[idx];
                    string relPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(relPath)) continue;

                    string destPath = Path.GetFullPath(Path.Combine(installRoot, relPath));
                    if (!destPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Blocked unsafe archive entry path: " + entry.FullName);
                    }

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destPath);
                        continue;
                    }

                    string parentDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    int pct = 18 + ((idx + 1) * 52) / totalEntries;
                    report(pct, "Extracting: " + relPath);

                    using (Stream inStream = entry.Open())
                    using (FileStream outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        inStream.CopyTo(outStream);
                        totalExtractedBytes += outStream.Length;
                    }
                }
            }

            string selfExe = Process.GetCurrentProcess().MainModule.FileName;
            string uninstallerPath = Path.Combine(installRoot, "Uninstall.exe");
            report(76, "Creating uninstaller: Uninstall.exe");
            if (!string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(selfExe, uninstallerPath, true);
            }

            string installedExe = Path.Combine(installRoot, SetupTheme.ExeFileName);

            if (!opts.SkipRegistryAndShortcuts)
            {
                BackupExistingRegistryState();

                if (opts.CreateDesktopShortcut)
                {
                    report(84, "Creating Desktop shortcut...");
                    string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShellShortcut(
                        Path.Combine(desktopDir, "Monitor Display Switcher.lnk"),
                        installedExe,
                        "",
                        installRoot,
                        SetupTheme.FullHeaderTitle,
                        installedExe + ",0");
                }

                if (opts.CreateStartMenuShortcuts)
                {
                    report(90, "Creating Start Menu shortcuts...");
                    string startPrograms = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                        "Monitor Display Switcher");
                    Directory.CreateDirectory(startPrograms);

                    CreateShellShortcut(
                        Path.Combine(startPrograms, "Monitor Display Switcher.lnk"),
                        installedExe,
                        "",
                        installRoot,
                        SetupTheme.FullHeaderTitle,
                        installedExe + ",0");

                    CreateShellShortcut(
                        Path.Combine(startPrograms, "Uninstall Monitor Display Switcher.lnk"),
                        uninstallerPath,
                        "--uninstall",
                        installRoot,
                        "Uninstall Monitor Display Switcher",
                        uninstallerPath + ",0");
                }

                if (opts.LaunchOnStartup)
                {
                    report(95, "Configuring Windows System Tray startup entry...");
                    using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(SetupTheme.RunRegKey))
                    {
                        if (runKey != null)
                        {
                            runKey.SetValue(SetupTheme.RunValueName, "\"" + installedExe + "\" --minimized", RegistryValueKind.String);
                        }
                    }
                }

                report(98, "Registering Windows Installed Apps entry...");
                RegisterWindowsUninstaller(installRoot, installedExe, uninstallerPath, totalExtractedBytes);
            }

            report(100, "Installation complete! Installed to: " + installRoot);
        }

        private static void BackupExistingRegistryState()
        {
            try
            {
                string backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MonitorDisplaySwitcher");
                Directory.CreateDirectory(backupDir);
                string backupFile = Path.Combine(backupDir, "installer_registry_backup.txt");
                if (File.Exists(backupFile)) return;

                List<string> lines = new List<string>();
                lines.Add("# Monitor Display Switcher Additive Registry Backup (" + DateTime.UtcNow.ToString("o") + ")");
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(SetupTheme.RunRegKey, false))
                {
                    if (runKey != null)
                    {
                        object existing = runKey.GetValue(SetupTheme.RunValueName);
                        lines.Add("HKCU_RUN=" + (existing != null ? existing.ToString() : "<none>"));
                    }
                }
                File.WriteAllLines(backupFile, lines.ToArray(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void RegisterWindowsUninstaller(
            string installDir,
            string exePath,
            string uninstallExePath,
            long sizeBytes)
        {
            try
            {
                using (RegistryKey uninst = Registry.CurrentUser.CreateSubKey(SetupTheme.UninstallRegKey))
                {
                    if (uninst == null) return;
                    uninst.SetValue("DisplayName", SetupTheme.FullHeaderTitle, RegistryValueKind.String);
                    uninst.SetValue("DisplayVersion", SetupTheme.VersionText, RegistryValueKind.String);
                    uninst.SetValue("Publisher", "Mehedi Hasan Shihab", RegistryValueKind.String);
                    uninst.SetValue("DisplayIcon", exePath + ",0", RegistryValueKind.String);
                    uninst.SetValue("InstallLocation", installDir, RegistryValueKind.String);
                    uninst.SetValue("UninstallString", "\"" + uninstallExePath + "\" --uninstall", RegistryValueKind.String);
                    uninst.SetValue("QuietUninstallString", "\"" + uninstallExePath + "\" --uninstall --silent", RegistryValueKind.String);
                    uninst.SetValue("URLInfoAbout", "https://github.com/sshihabb007/monitor-display-switcher", RegistryValueKind.String);
                    uninst.SetValue("HelpLink", "https://sshihabb007.github.io/", RegistryValueKind.String);
                    uninst.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninst.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    int sizeKb = (int)Math.Max(256L, sizeBytes / 1024L);
                    uninst.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
                }
            }
            catch
            {
            }
        }

        public static void CreateShellShortcut(
            string lnkPath,
            string targetPath,
            string arguments,
            string workingDir,
            string description,
            string iconLocation)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { lnkPath });
                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                if (!string.IsNullOrEmpty(arguments))
                {
                    scType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                }
                if (!string.IsNullOrEmpty(workingDir))
                {
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                }
                if (!string.IsNullOrEmpty(description))
                {
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                }
                if (!string.IsNullOrEmpty(iconLocation))
                {
                    scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconLocation });
                }
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch
            {
            }
        }

        public static string ResolveExistingInstallDirForUninstall(string explicitDir)
        {
            if (!string.IsNullOrWhiteSpace(explicitDir))
            {
                return Path.GetFullPath(explicitDir);
            }
            string selfDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (File.Exists(Path.Combine(selfDir, SetupTheme.ExeFileName)))
            {
                return selfDir;
            }
            try
            {
                using (RegistryKey uninst = Registry.CurrentUser.OpenSubKey(SetupTheme.UninstallRegKey, false))
                {
                    if (uninst != null)
                    {
                        object loc = uninst.GetValue("InstallLocation");
                        if (loc != null && !string.IsNullOrWhiteSpace(loc.ToString()))
                        {
                            return Path.GetFullPath(loc.ToString());
                        }
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        public static void ExecuteUninstall(string installDir, bool skipRegistryAndShortcuts)
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            {
                return;
            }
            string fullDir = Path.GetFullPath(installDir);
            StopOwnedAppInDirectory(fullDir, null);

            string[] knownFiles = new string[]
            {
                SetupTheme.ExeFileName,
                "DisplayInputSwitcher.config.json",
                "README.md",
                "LICENSE"
            };
            for (int i = 0; i < knownFiles.Length; i++)
            {
                string f = Path.Combine(fullDir, knownFiles[i]);
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }

            if (!skipRegistryAndShortcuts)
            {
                try
                {
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Monitor Display Switcher.lnk");
                    if (File.Exists(desktopLnk)) File.Delete(desktopLnk);
                }
                catch { }

                try
                {
                    string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Monitor Display Switcher");
                    if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);
                }
                catch { }

                try
                {
                    using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(SetupTheme.RunRegKey, true))
                    {
                        if (runKey != null)
                        {
                            string curVal = runKey.GetValue(SetupTheme.RunValueName) as string;
                            if (!string.IsNullOrEmpty(curVal) && curVal.IndexOf(fullDir, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                runKey.DeleteValue(SetupTheme.RunValueName, false);
                            }
                        }
                    }
                }
                catch { }

                try { Registry.CurrentUser.DeleteSubKeyTree(SetupTheme.UninstallRegKey, false); } catch { }
            }

            string selfExe = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string uninstExe = Path.Combine(fullDir, "Uninstall.exe");
            if (string.Equals(selfExe, uninstExe, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(
                        "cmd.exe",
                        "/c ping 127.0.0.1 -n 2 > nul & del /f /q \"" + uninstExe + "\" & rmdir \"" + fullDir + "\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
                catch { }
            }
            else
            {
                try { if (File.Exists(uninstExe)) File.Delete(uninstExe); } catch { }
                try
                {
                    if (Directory.GetFiles(fullDir).Length == 0 && Directory.GetDirectories(fullDir).Length == 0)
                    {
                        Directory.Delete(fullDir, false);
                    }
                }
                catch { }
            }
        }
    }

    public sealed class SetupWizardForm : Form
    {
        private readonly SetupInstallOptions _options;
        private int _currentStep = 0;
        private bool _installingBusy = false;

        private Panel _stepWelcome;
        private Panel _stepLocation;
        private Panel _stepProgress;
        private Panel _stepFinish;

        private TextBox _installDirBox;
        private CheckBox _desktopShortcutCheck;
        private CheckBox _startMenuCheck;
        private CheckBox _startupCheck;

        private GradientProgressBarPanel _progressBar;
        private TextBox _installLogBox;

        private TextBox _finalPathBox;
        private CheckBox _launchAppCheck;
        private CheckBox _openFolderCheck;

        private Button _backBtn;
        private Button _nextBtn;
        private Button _cancelBtn;

        public SetupWizardForm(SetupInstallOptions initialOpts)
        {
            _options = initialOpts ?? new SetupInstallOptions();

            Text = SetupTheme.FullHeaderTitle + " — Setup";
            Size = new Size(640, 465);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SetupTheme.WindowBg;
            ForeColor = SetupTheme.PrimaryText;
            Font = new Font("Segoe UI", 9.2f);
            Icon = SetupTheme.LoadEmbeddedIcon();
            SetupTheme.ApplyDarkTitleBar(this);

            BuildHeaderBanner();
            BuildFooterBar();
            BuildStepPanels();
            ShowWizardStep(0);
        }

        private void BuildHeaderBanner()
        {
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = SetupTheme.CardBg
            };
            PictureBox logo = new PictureBox
            {
                Image = SetupTheme.RenderBrandLogo(42),
                Location = new Point(18, 13),
                Size = new Size(42, 42),
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            Label title = new Label
            {
                Text = SetupTheme.FullHeaderTitle,
                Location = new Point(72, 13),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 11.4f),
                ForeColor = SetupTheme.PrimaryText
            };
            Label sub = new Label
            {
                Text = "Universal DDC/CI Monitor HDMI, DisplayPort & USB-C Switcher (AnyCPU)",
                Location = new Point(73, 38),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = SetupTheme.MutedText
            };
            Panel accentLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SetupTheme.BorderCol
            };
            header.Controls.AddRange(new Control[] { logo, title, sub, accentLine });
            Controls.Add(header);
        }

        private void BuildFooterBar()
        {
            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = SetupTheme.CardBg
            };
            Panel topBorder = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = SetupTheme.BorderCol
            };

            _backBtn = SetupTheme.CreateStyledButton("< Back", SetupTheme.InputBg, ForeColor, 96);
            _backBtn.Location = new Point(300, 11);
            _backBtn.Click += delegate { if (_currentStep == 1) ShowWizardStep(0); };

            _nextBtn = SetupTheme.CreateStyledButton("Next >", SetupTheme.AccentViolet, Color.White, 108);
            _nextBtn.Location = new Point(404, 11);
            _nextBtn.Click += delegate { HandlePrimaryButtonClick(); };

            _cancelBtn = SetupTheme.CreateStyledButton("Cancel", SetupTheme.InputBg, ForeColor, 96);
            _cancelBtn.Location = new Point(520, 11);
            _cancelBtn.Click += delegate
            {
                if (!_installingBusy) Close();
            };

            footer.Controls.AddRange(new Control[] { topBorder, _backBtn, _nextBtn, _cancelBtn });
            Controls.Add(footer);
        }

        private void BuildStepPanels()
        {
            Rectangle bodyBounds = new Rectangle(0, 68, 624, 304);

            // Step 0: Welcome
            _stepWelcome = CreateStepPanel(bodyBounds);
            Label wTitle = new Label
            {
                Text = "Welcome to Monitor Display Switcher Setup",
                Location = new Point(24, 20),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = SetupTheme.PrimaryText
            };
            Label wBody = new Label
            {
                Text =
                    "This wizard will install Monitor Display Switcher 1.2.0 on your Windows computer.\r\n\r\n" +
                    "Key Features Included:\r\n" +
                    "  • Universal DDC/CI auto-detection across LG, Dell, ASUS, MSI, BenQ, Samsung & HP\r\n" +
                    "  • 1-Click HDMI, DisplayPort & USB-C switching + Quick Toggle (Ctrl+Alt+S)\r\n" +
                    "  • Live hardware Brightness (VCP 10), Speaker Volume (VCP 62) & Audio Mute sliders\r\n" +
                    "  • Native AnyCPU support (x64, ARM64 & x86) with System Tray & Uninstaller\r\n\r\n" +
                    "Developed by Mehedi Hasan Shihab (Web & Software Developer) • @sshihabb007\r\n" +
                    "Click Next to review the installation directory and planned shortcuts.",
                Location = new Point(24, 58),
                Size = new Size(576, 220),
                Font = new Font("Segoe UI", 9.3f),
                ForeColor = SetupTheme.MutedText
            };
            _stepWelcome.Controls.AddRange(new Control[] { wTitle, wBody });

            // Step 1: Planned Changes & Destination
            _stepLocation = CreateStepPanel(bodyBounds);
            Label lTitle = new Label
            {
                Text = "Select Destination & Planned Setup Actions",
                Location = new Point(24, 16),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 11.8f),
                ForeColor = SetupTheme.PrimaryText
            };
            Label dirCaption = new Label
            {
                Text = "Installation Folder:",
                Location = new Point(24, 48),
                AutoSize = true,
                ForeColor = SetupTheme.MutedText
            };
            _installDirBox = new TextBox
            {
                Text = _options.InstallDirectory,
                Location = new Point(24, 70),
                Width = 468,
                BackColor = SetupTheme.InputBg,
                ForeColor = SetupTheme.PrimaryText,
                BorderStyle = BorderStyle.FixedSingle
            };
            Button browseBtn = SetupTheme.CreateStyledButton("Browse...", SetupTheme.InputBg, ForeColor, 96);
            browseBtn.Size = new Size(96, 27);
            browseBtn.Location = new Point(500, 68);
            browseBtn.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Choose installation folder for Monitor Display Switcher";
                    fbd.SelectedPath = _installDirBox.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    {
                        _installDirBox.Text = fbd.SelectedPath;
                    }
                }
            };

            _desktopShortcutCheck = new CheckBox
            {
                Text = "Create a Desktop shortcut (Monitor Display Switcher.lnk)",
                Checked = _options.CreateDesktopShortcut,
                Location = new Point(24, 118),
                AutoSize = true,
                ForeColor = SetupTheme.PrimaryText
            };
            _startMenuCheck = new CheckBox
            {
                Text = "Create Start Menu shortcuts (Application + Uninstaller)",
                Checked = _options.CreateStartMenuShortcuts,
                Location = new Point(24, 148),
                AutoSize = true,
                ForeColor = SetupTheme.PrimaryText
            };
            _startupCheck = new CheckBox
            {
                Text = "Launch minimized in System Tray when Windows starts (HKCU Run)",
                Checked = _options.LaunchOnStartup,
                Location = new Point(24, 178),
                AutoSize = true,
                ForeColor = SetupTheme.PrimaryText
            };
            Label planNote = new Label
            {
                Text = "Note: Setup writes files and shortcuts only after you click Install below, and registers a clean Uninstaller in Windows Installed Apps.",
                Location = new Point(24, 218),
                Size = new Size(572, 42),
                Font = new Font("Segoe UI", 8.6f),
                ForeColor = SetupTheme.MutedText
            };
            _stepLocation.Controls.AddRange(new Control[]
            {
                lTitle, dirCaption, _installDirBox, browseBtn,
                _desktopShortcutCheck, _startMenuCheck, _startupCheck, planNote
            });

            // Step 2: Progress
            _stepProgress = CreateStepPanel(bodyBounds);
            Label pTitle = new Label
            {
                Text = "Installing Monitor Display Switcher...",
                Location = new Point(24, 18),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 11.8f),
                ForeColor = SetupTheme.PrimaryText
            };
            _progressBar = new GradientProgressBarPanel
            {
                Location = new Point(24, 54),
                Size = new Size(572, 26)
            };
            _installLogBox = new TextBox
            {
                Location = new Point(24, 92),
                Size = new Size(572, 185),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SetupTheme.InputBg,
                ForeColor = SetupTheme.MutedText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.6f)
            };
            _stepProgress.Controls.AddRange(new Control[] { pTitle, _progressBar, _installLogBox });

            // Step 3: Finish
            _stepFinish = CreateStepPanel(bodyBounds);
            Label fTitle = new Label
            {
                Text = "Monitor Display Switcher 1.2.0 Installed Successfully!",
                Location = new Point(24, 18),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 12.2f),
                ForeColor = Color.FromArgb(110, 231, 183)
            };
            Label fSub = new Label
            {
                Text = "Installed Application Directory:",
                Location = new Point(24, 54),
                AutoSize = true,
                ForeColor = SetupTheme.MutedText
            };
            _finalPathBox = new TextBox
            {
                ReadOnly = true,
                Location = new Point(24, 76),
                Width = 572,
                BackColor = SetupTheme.InputBg,
                ForeColor = SetupTheme.PrimaryText,
                BorderStyle = BorderStyle.FixedSingle
            };
            _launchAppCheck = new CheckBox
            {
                Text = "Launch Monitor Display Switcher now",
                Checked = true,
                Location = new Point(24, 118),
                AutoSize = true,
                ForeColor = SetupTheme.PrimaryText
            };
            _openFolderCheck = new CheckBox
            {
                Text = "Open installation directory in File Explorer",
                Checked = false,
                Location = new Point(24, 148),
                AutoSize = true,
                ForeColor = SetupTheme.PrimaryText
            };
            LinkLabel devLink = new LinkLabel
            {
                Text = "Developed by Mehedi Hasan Shihab — https://sshihabb007.github.io/",
                Location = new Point(24, 196),
                AutoSize = true,
                LinkColor = Color.FromArgb(129, 140, 248),
                ActiveLinkColor = Color.FromArgb(56, 189, 248)
            };
            devLink.LinkClicked += delegate
            {
                try { Process.Start(new ProcessStartInfo("https://sshihabb007.github.io/") { UseShellExecute = true }); } catch { }
            };
            _stepFinish.Controls.AddRange(new Control[] { fTitle, fSub, _finalPathBox, _launchAppCheck, _openFolderCheck, devLink });

            Controls.AddRange(new Control[] { _stepWelcome, _stepLocation, _stepProgress, _stepFinish });
        }

        private static Panel CreateStepPanel(Rectangle bounds)
        {
            return new Panel
            {
                Bounds = bounds,
                BackColor = SetupTheme.WindowBg,
                Visible = false
            };
        }

        private void ShowWizardStep(int step)
        {
            _currentStep = step;
            _stepWelcome.Visible = (step == 0);
            _stepLocation.Visible = (step == 1);
            _stepProgress.Visible = (step == 2);
            _stepFinish.Visible = (step == 3);

            _backBtn.Visible = (step == 1);
            _cancelBtn.Visible = (step < 3);
            _cancelBtn.Enabled = !_installingBusy;

            if (step == 0) _nextBtn.Text = "Next >";
            else if (step == 1) _nextBtn.Text = "Install";
            else if (step == 2) _nextBtn.Text = "Installing...";
            else _nextBtn.Text = "Finish";

            _nextBtn.Enabled = !_installingBusy;
        }

        private async void HandlePrimaryButtonClick()
        {
            if (_currentStep == 0)
            {
                ShowWizardStep(1);
                return;
            }

            if (_currentStep == 1)
            {
                string chosenDir = (_installDirBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(chosenDir))
                {
                    MessageBox.Show(this, "Please select a valid installation folder.", "Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _options.InstallDirectory = Path.GetFullPath(chosenDir);
                _options.CreateDesktopShortcut = _desktopShortcutCheck.Checked;
                _options.CreateStartMenuShortcuts = _startMenuCheck.Checked;
                _options.LaunchOnStartup = _startupCheck.Checked;

                if (!SetupInstallerEngine.CanWriteTargetDirectory(_options.InstallDirectory) && !SetupInstallerEngine.IsProcessElevated())
                {
                    DialogResult askAdmin = MessageBox.Show(
                        this,
                        "The selected folder requires Administrator permission:\r\n" + _options.InstallDirectory + "\r\n\r\nRestart Setup as Administrator?",
                        "Administrator Permission Required",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (askAdmin == DialogResult.Yes)
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName);
                            psi.Verb = "runas";
                            psi.Arguments = "--install-dir \"" + _options.InstallDirectory + "\"";
                            psi.UseShellExecute = true;
                            Process.Start(psi);
                            Close();
                            return;
                        }
                        catch
                        {
                            return;
                        }
                    }
                    return;
                }

                _installingBusy = true;
                ShowWizardStep(2);

                try
                {
                    await Task.Run(delegate
                    {
                        SetupInstallerEngine.ExecuteInstallation(_options, delegate(int pct, string msg)
                        {
                            BeginInvoke(new Action(delegate
                            {
                                _progressBar.Percent = pct;
                                _progressBar.Caption = pct + "%";
                                _progressBar.Invalidate();
                                _installLogBox.AppendText(msg + Environment.NewLine);
                            }));
                        });
                    });

                    _installingBusy = false;
                    _finalPathBox.Text = _options.InstallDirectory;
                    ShowWizardStep(3);
                }
                catch (Exception ex)
                {
                    _installingBusy = false;
                    ShowWizardStep(1);
                    MessageBox.Show(this, "Installation failed:\r\n" + ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            if (_currentStep == 3)
            {
                string installedExe = Path.Combine(_options.InstallDirectory, SetupTheme.ExeFileName);
                if (_openFolderCheck.Checked && Directory.Exists(_options.InstallDirectory))
                {
                    try { Process.Start("explorer.exe", "\"" + _options.InstallDirectory + "\""); } catch { }
                }
                if (_launchAppCheck.Checked && File.Exists(installedExe))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(installedExe);
                        psi.WorkingDirectory = _options.InstallDirectory;
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch { }
                }
                Close();
            }
        }
    }

    public static class SetupProgram
    {
        [STAThread]
        public static int Main(string[] args)
        {
            bool silent = HasFlag(args, "--silent") || HasFlag(args, "/S") || HasFlag(args, "/s");
            bool uninstall = HasFlag(args, "--uninstall") || HasFlag(args, "/uninstall");
            bool noRegistry = HasFlag(args, "--no-registry");
            string customDir = GetArgValue(args, "--install-dir");

            SetupInstallOptions opts = new SetupInstallOptions();
            opts.SkipRegistryAndShortcuts = noRegistry;
            if (!string.IsNullOrWhiteSpace(customDir))
            {
                opts.InstallDirectory = Path.GetFullPath(customDir);
            }

            if (uninstall)
            {
                string targetDir = SetupInstallerEngine.ResolveExistingInstallDirForUninstall(customDir);
                if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
                {
                    return 0;
                }

                if (!silent)
                {
                    Application.EnableVisualStyles();
                    DialogResult confirm = MessageBox.Show(
                        "Uninstall Monitor Display Switcher from:\r\n" + targetDir + " ?",
                        "Uninstall Monitor Display Switcher",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes) return 0;
                }

                SetupInstallerEngine.ExecuteUninstall(targetDir, noRegistry);
                if (!silent)
                {
                    MessageBox.Show(
                        "Monitor Display Switcher has been uninstalled.",
                        "Uninstall Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return 0;
            }

            if (silent)
            {
                try
                {
                    SetupInstallerEngine.ExecuteInstallation(opts, null);
                    return 0;
                }
                catch (Exception ex)
                {
                    try { Console.Error.WriteLine("Setup failed: " + ex.Message); } catch { }
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupWizardForm(opts));
            return 0;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetArgValue(string[] args, string key)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
