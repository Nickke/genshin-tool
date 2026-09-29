using InventoryKamera.ui;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using NHotkey;
using NHotkey.WindowsForms;
using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using WindowsInput.Native;
using Application = System.Windows.Forms.Application;

namespace InventoryKamera
{
    public partial class MainForm : Form
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static Thread scannerThread;
        private static InventoryKamera data = new InventoryKamera();
        private static DatabaseManager databaseManager = new DatabaseManager();

        private int Delay;

        private bool running = false;

        public MainForm()
        {
            InitializeComponent();

            Language_ComboBox.SelectedItem = "ENG";

            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
#if DEBUG
            version = Assembly.GetExecutingAssembly().GetName().Version.ToString(4);
#endif
            Logger.Info("Inventory Kamera version {0}", version);

            Text = $"Inventory Kamera V{version}";

            UserInterface.Init(
                GearPictureBox,
                ArtifactOutput_TextBox,
                CharacterName_PictureBox,
                CharacterLevel_PictureBox,
                new[] { CharacterTalent1_PictureBox, CharacterTalent2_PictureBox, CharacterTalent3_PictureBox },
                CharacterOutput_TextBox,
                WeaponsScannedCount_Label,
                WeaponsMax_Labell,
                ArtifactsScanned_Label,
                ArtifactsMax_Label,
                CharactersScanned_Label,
                ProgramStatus_Label,
                ErrorLog_TextBox,
                Navigation_Image,
                MaterialsScanned_Label,
                CharDevScanned_Label);
        }

        private double ScannerDelayValue(int value)
        {
            switch (value)
            {
                case 0:
                    return 0.5;

                case 1:
                    return 1;

                case 2:
                    return 1.5;

                default:
                    return 1;
            }
        }

        private void Hotkey_Pressed(object sender, HotkeyEventArgs e)
        {
            Logger.Info("Hotkey pressed");
            e.Handled = true;
            // Check if scanner is running
            if (scannerThread.IsAlive)
            {
                // Stop navigating weapons/artifacts
                scannerThread.Abort();

                UserInterface.SetProgramStatus("Scan Stopped");

                Navigation.Reset();
            }
        }

        private void ResetUI()
        {
            Navigation.Reset();

            // Need to invoke method from the UI's handle, not the worker thread
            BeginInvoke((MethodInvoker)delegate { RemoveHotkey(); });
            Logger.Info("Hotkey removed");
        }

        private void RemoveHotkey()
        {
            HotkeyManager.Current.Remove("Stop");
        }

        public static void UnexpectedError(string error)
        {
            if (scannerThread.IsAlive)
            {
                UserInterface.AddError(error);
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.UpgradeNeeded)
            {
                try
                {
                    Properties.Settings.Default.Upgrade();
                    Logger.Info("Application settings loaded from previous version");
                }
                catch (Exception) { }
                Properties.Settings.Default.UpgradeNeeded = false;
                Properties.Settings.Default.Save();
            }

            UpdateKeyTextBoxes();

            Delay = ScannerDelay_TrackBar.Value;
            SpeedToggle_Button.Text = Properties.Settings.Default.ScannerDelay == 2 ? "Slow" : "Fast";

            ProgramStatus_Label.Text = "";
            if (string.IsNullOrWhiteSpace(OutputPath_TextBox.Text))
            {
                OutputPath_TextBox.Text = Directory.GetCurrentDirectory() + @"\GenshinData";
            }

            darkModeToolStripMenuItem.Checked = Properties.Settings.Default.DarkMode;
            ApplyTheme(Properties.Settings.Default.DarkMode);

            AutoCopy_CheckBox.Checked = Properties.Settings.Default.AutoCopyEnabled;
            bool autoCopyOn = AutoCopy_CheckBox.Checked;
            AutoCopyJsonLabel.Enabled = autoCopyOn;
            AutoCopyJsonSelect_Button.Enabled = autoCopyOn;
            AutoCopyJsonPath_TextBox.Enabled = autoCopyOn;
            AutoCopyLogLabel.Enabled = autoCopyOn;
            AutoCopyLogSelect_Button.Enabled = autoCopyOn;
            AutoCopyLogPath_TextBox.Enabled = autoCopyOn;

            // Check for game data updates in the background so the UI loads immediately.
            new Thread(StartupGameDataCheck) { IsBackground = true, Name = "StartupUpdateCheck" }.Start();
        }

        private void StartupGameDataCheck()
        {
            var dbm = new DatabaseManager();
            bool updateAvailable;
            try
            {
                updateAvailable = dbm.UpdateAvailable();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not check for game data updates on startup");
                return;
            }

            if (!updateAvailable)
            {
                Logger.Info("Game data is up to date ({0})", dbm.LocalVersion.ToString(2));
                return;
            }

            // Ask the user on the UI thread so the dialog is properly parented.
            Invoke(new Action(() =>
            {
                var result = MessageBox.Show(
                    $"A new version of Genshin Impact data (v{dbm.RemoteVersion.ToString(2)}) is available.\n" +
                    "Would you like to update the lookup tables now? (Recommended)",
                    "Game Data Update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    var status = dbm.UpdateGameData();
                    switch (status)
                    {
                        case UpdateStatus.Fail:
                            MessageBox.Show("Update failed. Check the log for details.", "Update failed",
                                MessageBoxButtons.OK, MessageBoxIcon.Stop);
                            break;
                        case UpdateStatus.Success:
                            MessageBox.Show(
                                $"Game data updated to version {dbm.LocalVersion.ToString(2)}.",
                                "Update successful",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                            Logger.Info("Game data updated to {0}", dbm.LocalVersion.ToString(2));
                            break;
                    }
                }
                else
                {
                    Logger.Info("User declined startup game data update");
                }
            }));
        }

        private void UpdateKeyTextBoxes()
        { 
            Navigation.inventoryKey = (VirtualKeyCode)Properties.Settings.Default.InventoryKey;
            Navigation.characterKey = (VirtualKeyCode)Properties.Settings.Default.CharacterKey;
            Navigation.slotOneKey = (VirtualKeyCode)Properties.Settings.Default.Slot1Key;

            inventoryToolStripTextBox.Text = new KeysConverter().ConvertToString((Keys)Navigation.inventoryKey);
            characterToolStripTextBox.Text = new KeysConverter().ConvertToString((Keys)Navigation.characterKey);
            slot1StripTextBox.Text = new KeysConverter().ConvertToString((Keys)Navigation.slotOneKey);

            // Make sure text boxes show key glyph and not "OEM..."
            if (inventoryToolStripTextBox.Text.ToUpper().Contains("OEM"))
            {
                inventoryToolStripTextBox.Text = KeyCodeToUnicode((Keys)Navigation.inventoryKey);
            }
            if (characterToolStripTextBox.Text.ToUpper().Contains("OEM"))
            {
                characterToolStripTextBox.Text = KeyCodeToUnicode((Keys)Navigation.characterKey);
            }
            if (slot1StripTextBox.Text.ToUpper().Contains("OEM"))
            {
                slot1StripTextBox.Text = KeyCodeToUnicode((Keys)Navigation.slotOneKey);
            }
        }

        private void ValidateCustomName(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            var name = textbox.Text;

            if (!string.IsNullOrWhiteSpace(name))
            {
                if (GenshinProcesor.Characters.ContainsKey(name.ConvertToGood().ToLower()))
                {
                    textbox.BackColor = Color.Yellow;
                }
                else
                {
                    textbox.BackColor = Color.White;
                }
            }
        }

        private void DisplayCustomNameTooltip(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            
            if (textbox.BackColor == Color.Yellow)
            {
                var tooltip = new ToolTip();
                tooltip.Show($"{textbox.Text} already exists as a character's name.\n" +
                    $"This may affect equipping items to characters and is not fully supported yet.", textbox);
            }
        }

        private void StartButton_Clicked(object sender, EventArgs e)
        {
            GC.Collect();

            UserInterface.ResetAll();

            UserInterface.SetProgramStatus("Scanning");
            Logger.Info("Starting scan");

            if (Directory.Exists(OutputPath_TextBox.Text) || Directory.CreateDirectory(OutputPath_TextBox.Text).Exists)
            {
                if (running)
                {
                    Logger.Debug("Already running");
                    return;
                }
                running = true;

                HotkeyManager.Current.AddOrReplace("Stop", Keys.Enter, Hotkey_Pressed);
                Logger.Info("Hotkey registered");
                var settings = Properties.Settings.Default;
                var gameVersion = new DatabaseManager().LocalVersion.ToString(2);
                var options =
                    $"\n\tGame Version Data:\t\t\t {gameVersion}\n" +
                    $"\tWeapons:\t\t\t\t {settings.ScanWeapons}\n" +
                    $"\tArtifacts:\t\t\t\t {settings.ScanArtifacts}\n" +
                    $"\tCharacters:\t\t\t\t {settings.ScanCharacters}\n" +
                    $"\tDev Items:\t\t\t\t {settings.ScanCharDevItems}\n" +
                    $"\tMaterials:\t\t\t\t {settings.ScanMaterials}\n" +
                    $"\tMin Weapon Rarity:\t\t {settings.MinimumWeaponRarity}\n" +
                    $"\tMin Weapon Level:\t\t {settings.MinimumWeaponLevel}\n" +
                    $"\tEquip Weapons:\t\t\t {settings.EquipWeapons}\n" +
                    $"\tMin Artifact Rarity:\t {settings.MinimumArtifactRarity}\n" +
                    $"\tMin Artifact Level:\t\t {settings.MinimumArtifactLevel}\n" +
                    $"\tEquip Artifacts:\t\t {settings.EquipArtifacts}\n" +
                    $"\tDelay:\t\t\t\t\t {settings.ScannerDelay}";

                Logger.Info("Scan settings: {0}", options);

                scannerThread = new Thread(() =>
                {
                    try
                    {
                        // Get Screen Location and Size
                        Navigation.Initialize();

                        List<Size> sizes = new List<Size>
                        {
                            new Size(16,9),
                            new Size(8,5),
                        };

                        if (!sizes.Contains(Navigation.GetAspectRatio()))
                        {
                            throw new NotImplementedException($"{Navigation.GetSize().Width}x{Navigation.GetSize().Height} is an unsupported resolution.");
                        }

                        if (Navigation.GetSize() != Navigation.CaptureWindow().Size) throw new FormatException("Window size and screenshot size mismatch. Please make sure the game is not in a fullscreen mode.");

                        data = new InventoryKamera();

                        Logger.Info("Resolution: {0}x{1}", Navigation.GetSize().Width, Navigation.GetSize().Height);

                        // Add navigation delay
                        Navigation.SetDelay(ScannerDelayValue(Delay));


                        // The Data object of json object
                        data.GatherData();

                        // Covert to GOOD
                        GOOD good = new GOOD(data);
                        Logger.Info("Data converted to GOOD");

                        // Make Json File
                        good.WriteToJSON(OutputPath_TextBox.Text);
                        Logger.Info("Exported data");

                        // Auto-copy JSON and logging to their respective folders
                        if (AutoCopy_CheckBox.Checked)
                        {
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(AutoCopyJsonPath_TextBox.Text))
                                {
                                    string jsonDest = AutoCopyJsonPath_TextBox.Text;
                                    Directory.CreateDirectory(jsonDest);
                                    var jsonFile = Directory.GetFiles(OutputPath_TextBox.Text, "genshinData_GOOD_*.json")
                                                            .OrderByDescending(File.GetLastWriteTime)
                                                            .FirstOrDefault();
                                    if (jsonFile != null)
                                        File.Copy(jsonFile, Path.Combine(jsonDest, Path.GetFileName(jsonFile)), overwrite: true);
                                    Logger.Info("Auto-copied JSON to {0}", jsonDest);
                                }

                                if (!string.IsNullOrWhiteSpace(AutoCopyLogPath_TextBox.Text))
                                {
                                    string logDest = AutoCopyLogPath_TextBox.Text;
                                    if (Directory.Exists(logDest))
                                        Directory.Delete(logDest, recursive: true);
                                    string srcLog = Path.GetFullPath("./logging");
                                    if (Directory.Exists(srcLog))
                                        CopyDirectory(srcLog, logDest);
                                    Logger.Info("Auto-copied log to {0}", logDest);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "Auto-copy failed");
                                UserInterface.AddError($"Auto-copy failed: {ex.Message}");
                            }
                        }

                        UserInterface.SetProgramStatus("Finished");
                        OpenOptimizerDialog(good);
                    }
                    catch (ThreadAbortException)
                    {
                        // Workers can get stuck if the thread is aborted or an exception is raised
                        data?.StopImageProcessorWorkers();
                        UserInterface.SetProgramStatus("Scan stopped");
                    }
                    catch (NotImplementedException ex)
                    {
                        UserInterface.AddError(ex.ToString());
                    }
                    catch (Exception ex)
                    {
                        // Workers can get stuck if the thread is aborted or an exception is raised
                        data?.StopImageProcessorWorkers();
                        while (ex.InnerException != null) ex = ex.InnerException;
                        UserInterface.AddError(ex.ToString());
                        UserInterface.SetProgramStatus("Scan aborted", ok: false);
                    }
                    finally
                    {
                        ResetUI();
                        running = false;
                        ManualExportButton.Invoke((MethodInvoker)delegate
                        {
                            ManualExportButton.Enabled = data.HasData;
                        });
                        MainForm_Activate();
                    }
                })
                {
                    IsBackground = true
                };
                scannerThread.Start();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(OutputPath_TextBox.Text))
                    UserInterface.AddError("Please set an output directory");
                else
                    UserInterface.AddError($"{OutputPath_TextBox.Text} is not a valid directory");
            }
        }

        private void OpenOptimizerDialog(GOOD data, bool skip = false)
        {
            if (!skip)
            {
                var message = "Scan complete! Would you like to upload the database to Genshin Optimizer?";
                var result = MessageBox.Show(message, "Scan Complete", MessageBoxButtons.YesNo);
                if (result == DialogResult.No)
                    return;
            }
            var t = new Thread(() => Clipboard.SetText(data.ToString()));
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            MessageBox.Show("Content copied to your clipboard! Paste the content into the textbox when prompted.", "Data Copied", MessageBoxButtons.OK);
            Process.Start(new ProcessStartInfo("https://frzyc.github.io/genshin-optimizer/#/setting") { UseShellExecute = true });

        }

        private void SpeedToggle_Button_Click(object sender, EventArgs e)
        {
            int next = Properties.Settings.Default.ScannerDelay == 0 ? 2 : 0;
            Properties.Settings.Default.ScannerDelay = next;
            Properties.Settings.Default.Save();
            Delay = next;
            SpeedToggle_Button.Text = next == 0 ? "Fast" : "Slow";
        }

        private void Github_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/Andrewthe13th/Inventory_Kamera/");
        }

        private void Releases_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/Nickke/genshin-tool");
        }

        private void IssuesPage_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/Andrewthe13th/Inventory_Kamera/issues");
        }

        private void FileSelectButton_Click(object sender, EventArgs e)
        {
            // A nicer file browser
            CommonOpenFileDialog d = new CommonOpenFileDialog
            {
                InitialDirectory = !Directory.Exists(OutputPath_TextBox.Text) ? Directory.GetCurrentDirectory() : OutputPath_TextBox.Text,
                IsFolderPicker = true
            };

            if (d.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath_TextBox.Text = d.FileName;
            }
        }

        private void AutoCopyJsonSelect_Button_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog d = new CommonOpenFileDialog
            {
                InitialDirectory = !Directory.Exists(AutoCopyJsonPath_TextBox.Text) ? Directory.GetCurrentDirectory() : AutoCopyJsonPath_TextBox.Text,
                IsFolderPicker = true
            };
            if (d.ShowDialog() == CommonFileDialogResult.Ok)
                AutoCopyJsonPath_TextBox.Text = d.FileName;
        }

        private void AutoCopyLogSelect_Button_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog d = new CommonOpenFileDialog
            {
                InitialDirectory = !Directory.Exists(AutoCopyLogPath_TextBox.Text) ? Directory.GetCurrentDirectory() : AutoCopyLogPath_TextBox.Text,
                IsFolderPicker = true
            };
            if (d.ShowDialog() == CommonFileDialogResult.Ok)
                AutoCopyLogPath_TextBox.Text = d.FileName;
        }

        private void AutoCopy_CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            bool on = AutoCopy_CheckBox.Checked;
            AutoCopyJsonLabel.Enabled = on;
            AutoCopyJsonSelect_Button.Enabled = on;
            AutoCopyJsonPath_TextBox.Enabled = on;
            AutoCopyLogLabel.Enabled = on;
            AutoCopyLogSelect_Button.Enabled = on;
            AutoCopyLogPath_TextBox.Enabled = on;
            Properties.Settings.Default.AutoCopyEnabled = on;
            Properties.Settings.Default.Save();
        }

        private void darkModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool dark = darkModeToolStripMenuItem.Checked;
            Properties.Settings.Default.DarkMode = dark;
            Properties.Settings.Default.Save();
            ApplyTheme(dark);
        }

        // ─── Windows 11 palette ───────────────────────────────────────────────
        private static readonly Color Accent = Color.FromArgb(0, 120, 212); // Windows 11 blue

        private void ApplyTheme(bool dark)
        {
            // Windows 11 light / dark
            Color bg      = dark ? Color.FromArgb(32,  32,  32)  : Color.FromArgb(243, 243, 243);
            Color surface = dark ? Color.FromArgb(45,  45,  45)  : Color.FromArgb(255, 255, 255);
            Color btnBg   = dark ? Color.FromArgb(0,   120, 212) : Color.FromArgb(0,   120, 212);
            Color btnFg   = Color.White;
            Color text    = dark ? Color.FromArgb(255, 255, 255) : Color.FromArgb(30,  30,  30);
            Color inputBg = dark ? Color.FromArgb(61,  61,  61)  : Color.White;
            Color inputFg = dark ? Color.FromArgb(255, 255, 255) : Color.FromArgb(30,  30,  30);
            Color menuBg  = dark ? Color.FromArgb(44,  44,  44)  : Color.FromArgb(249, 249, 249);
            Color menuFg  = dark ? Color.FromArgb(255, 255, 255) : Color.FromArgb(30,  30,  30);
            Color border  = dark ? Color.FromArgb(69,  69,  69)  : Color.FromArgb(209, 209, 209);

            Font baseFont = new Font("Segoe UI", 9F);
            ApplyFont(baseFont);

            BackColor = bg;

            menuStrip1.BackColor = menuBg;
            menuStrip1.ForeColor = menuFg;
            menuStrip1.Font      = baseFont;
            menuStrip1.Renderer  = new ToolStripProfessionalRenderer(new FluentMenuColors(dark));
            ApplyMenuItemColors(menuStrip1.Items, menuFg, menuBg);

            ApplyToControls(Controls, bg, surface, btnBg, btnFg, text, inputBg, inputFg, border, dark);

            // Primary action button — Windows 11 blue, rounded
            StartScan_Button.FlatStyle = FlatStyle.Flat;
            StartScan_Button.BackColor = Accent;
            StartScan_Button.ForeColor = Color.White;
            StartScan_Button.FlatAppearance.BorderSize = 0;
            StartScan_Button.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            RoundButton(StartScan_Button, 6);
        }

        private void ApplyFont(Font f)
        {
            foreach (Control c in Controls)
                SetFont(c, f);
        }

        private static void SetFont(Control c, Font f)
        {
            if (c is MenuStrip || c is ToolStrip) return;
            if (!(c is PictureBox) && !(c is TrackBar))
                c.Font = f;
            foreach (Control child in c.Controls)
                SetFont(child, f);
        }

        private static void ApplyMenuItemColors(ToolStripItemCollection items, Color fg, Color bg)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = fg;
                item.BackColor = bg;
                if (item is ToolStripMenuItem mi)
                    ApplyMenuItemColors(mi.DropDownItems, fg, bg);
            }
        }

        private static void RoundButton(Button btn, int radius)
        {
            var gp = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            Rectangle r = btn.ClientRectangle;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            gp.CloseFigure();
            btn.Region = new Region(gp);
        }

        private void ApplyToControls(Control.ControlCollection controls,
            Color bg, Color surface, Color btnBg, Color btnFg,
            Color text, Color inputBg, Color inputFg, Color border, bool dark)
        {
            foreach (Control c in controls)
            {
                switch (c)
                {
                    case Button btn when btn.Name != "StartScan_Button":
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.BackColor = btnBg;
                        btn.ForeColor = btnFg;
                        btn.FlatAppearance.BorderSize  = 1;
                        btn.FlatAppearance.BorderColor = border;
                        RoundButton(btn, 5);
                        break;

                    case System.Windows.Forms.Label lbl when lbl.Name != "ProgramStatus_Label":
                        lbl.BackColor = Color.Transparent;
                        lbl.ForeColor = text;
                        break;

                    case System.Windows.Forms.LinkLabel ll:
                        ll.BackColor       = Color.Transparent;
                        ll.ForeColor       = Accent;
                        ll.LinkColor       = Accent;
                        ll.ActiveLinkColor = Color.FromArgb(0, 90, 180);
                        break;

                    case TextBox tb:
                        tb.BackColor = inputBg;
                        if (tb.Name != "ErrorLog_TextBox")
                            tb.ForeColor = inputFg;
                        break;

                    case CheckBox cb:
                        cb.BackColor = Color.Transparent;
                        cb.ForeColor = text;
                        break;

                    case Panel panel:
                        panel.BackColor = surface;
                        break;

                    case NumericUpDown nud:
                        nud.BackColor = inputBg;
                        nud.ForeColor = inputFg;
                        break;

                    case TrackBar tr:
                        tr.BackColor = bg;
                        break;

                    case PictureBox pb:
                        pb.BackColor = Color.Transparent;
                        break;
                }

                if (c.Controls.Count > 0)
                    ApplyToControls(c.Controls, bg, surface, btnBg, btnFg, text, inputBg, inputFg, border, dark);
            }
        }

        private class FluentMenuColors : ProfessionalColorTable
        {
            private readonly bool _dark;
            private static readonly Color Blue = Color.FromArgb(0, 120, 212);
            public FluentMenuColors(bool dark) { _dark = dark; }
            private Color Bg    => _dark ? Color.FromArgb(44,  44,  44) : Color.FromArgb(249, 249, 249);
            private Color Hover => _dark ? Color.FromArgb(61,  61,  61) : Color.FromArgb(227, 241, 255);
            public override Color MenuItemSelected              => Hover;
            public override Color MenuItemBorder                => Blue;
            public override Color MenuBorder                    => Color.FromArgb(209, 209, 209);
            public override Color ToolStripDropDownBackground   => Bg;
            public override Color ImageMarginGradientBegin      => Bg;
            public override Color ImageMarginGradientMiddle     => Bg;
            public override Color ImageMarginGradientEnd        => Bg;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd   => Hover;
            public override Color MenuItemPressedGradientBegin  => Hover;
            public override Color MenuItemPressedGradientEnd    => Hover;
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
            RemoveHotkey();
            NLog.LogManager.Shutdown();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        private void ScannerDelay_TrackBar_ValueChanged(object sender, EventArgs e)
        {
            Delay = ((TrackBar)sender).Value;
        }

        private void Exit_MenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void OptionsMenuItem_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            // Virtual keys for 0-9, A-Z
            bool vk = e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.Z;
            // Numpad keys and function keys (internally accepts up to F24)
            bool np = e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.F24;
            // OEM keys (Keys that vary depending on keyboard layout)
            bool oem = e.KeyCode >= Keys.Oem1 && e.KeyCode <= Keys.Oem7;
            // Arrow keys, spacebar, INS, DEL, HOME, END, PAGEUP, PAGEDOWN
            bool misc = e.KeyCode == Keys.Space || (e.KeyCode >= Keys.Left && e.KeyCode <= Keys.Down) || (e.KeyCode >= Keys.Prior && e.KeyCode <= Keys.Home) || e.KeyCode == Keys.Insert || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back;

            // Validate that key is an acceptable Genshin keybind.
            if (!vk && !np && !oem && !misc)
            {
                Logger.Debug("Invalid {key} key pressed", e.KeyCode);
                return;
            }
            ToolStripTextBox s = (ToolStripTextBox)sender;

            // Needed to differentiate between NUMPAD numbers and numbers at top of keyboard
            s.Text = np || e.KeyCode == Keys.Back ? new KeysConverter().ConvertToString(e.KeyCode) : KeyCodeToUnicode(e.KeyData);

            // Spacebar or upper navigation keys (INSERT-PAGEDOWN keys) make textbox empty
            if (string.IsNullOrWhiteSpace(s.Text) || string.IsNullOrEmpty(s.Text))
            {
                s.Text = new KeysConverter().ConvertToString(e.KeyCode);
            }


            switch (s.Tag)
            {
                case "InventoryKey":
                    Navigation.inventoryKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Inv key set to: {key}", Navigation.inventoryKey);
                    Properties.Settings.Default.InventoryKey = e.KeyValue;
                    break;

                case "CharacterKey":
                    Navigation.characterKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Char key set to: {key}", Navigation.characterKey);
                    Properties.Settings.Default.CharacterKey = e.KeyValue;
                    break;

                case "slot1Key":
                    Navigation.slotOneKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Slot 1 key set to: {key}", Navigation.slotOneKey);
                    Properties.Settings.Default.Slot1Key = e.KeyValue;
                    break;

                default:
                    break;
            }
        }

        private void DatabaseUpdateMenuItem_Click(object sender, EventArgs e)
        {
            var status = databaseManager.UpdateGameData();
            switch (status)
            {
                case UpdateStatus.Fail:
                    MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
                    break;
                case UpdateStatus.Success:
                    MessageBox.Show($"Update for game version {databaseManager.LocalVersion.ToString(2)} successful.", "Update status", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
                    Logger.Info("Updated game date to {0}", databaseManager.LocalVersion.ToString(2));
                    break;
                case UpdateStatus.Skipped:
                    if (MessageBox.Show($"No update necessary! You are already using the latest game data ({databaseManager.LocalVersion.ToString(2)})." +
                        $" Would you like to force an update?",
                        "Already Up to Date",
                        buttons: MessageBoxButtons.YesNo,
                        icon: MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        status = databaseManager.UpdateGameData(force: true);
                        switch (status)
                        {
                            case UpdateStatus.Fail:
                                MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
                                break;
                            default:
                                MessageBox.Show($"Update for game version {databaseManager.LocalVersion.ToString(2)} successful.", "Update success", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
                                Logger.Info("Successfully updated game data to {0}", new DatabaseManager().LocalVersion.ToString(2));
                                break;
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        #region Unicode Helper Functions

        // Needed to display OEM keys as glyphs from keyboard. Should work for other languages
        // and keyboard layouts but only tested with QWERTY layout.
        private string KeyCodeToUnicode(Keys key)
        {
            byte[] keyboardState = new byte[255];
            bool keyboardStateStatus = GetKeyboardState(keyboardState);

            if (!keyboardStateStatus)
            {
                return "";
            }
            uint virtualKeyCode = (uint)key;
            uint scanCode = MapVirtualKey(virtualKeyCode, 0);
            IntPtr inputLocaleIdentifier = GetKeyboardLayout(0);

            StringBuilder result = new StringBuilder();
            ToUnicodeEx(virtualKeyCode, scanCode, keyboardState, result, 5, 0, inputLocaleIdentifier);

            return result.ToString();
        }

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        #endregion Unicode Helper Functions

        private void ExportFolderMenuItem_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(OutputPath_TextBox.Text) || Directory.CreateDirectory(OutputPath_TextBox.Text).Exists)
            {
                Process.Start($@"{OutputPath_TextBox.Text}");
            }
            else
            {
                Process.Start("explorer.exe");
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            CheckForKameraUpdates();
            CheckForGenshinUpdates();
        }

        private async void CheckForKameraUpdates()
        {
            var client = new GitHubClient(new ProductHeaderValue("Inventory_Kamera"));
            try
            {
                var releases = await client.Repository.Release.GetAll("Andrewthe13th", "Inventory_Kamera");
                var latest = releases.First();


                Version latestVersion = new Version(Regex.Replace(latest.TagName, "[a-zA-Z]", string.Empty));
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion.CompareTo(latestVersion) < 0)
                {
                    var message = $"A new version of Inventory Kamera is available.\n\n" +
                        $"Current Version: {currentVersion}\nLatest Version: {latestVersion}\n\n" +
                        $"Would you like to download the update?";
                    var result = MessageBox.Show(message, "Inventory Kamera Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(latest.HtmlUrl) { UseShellExecute = true });
                    }
                }
            }
            catch (RateLimitExceededException) { Logger.Warn("Rate limit exceeded checking for Kamera Update!!!! This warning should be resolved in an hour."); }
            
        }

        private void CheckForGenshinUpdates()
        {
            var databaseManager = new DatabaseManager();
            try
            {
                var updatesAvailable = databaseManager.UpdateAvailable();
                if (updatesAvailable)
                {
                    var message = "A new version for Genshin Impact has been found. Would you like to update Kamera's lookup tables? (Recommended)";
                    var result = MessageBox.Show(message, "Game Version Update", MessageBoxButtons.YesNo);
                    if (result == DialogResult.Yes)
                    {
                        switch (databaseManager.UpdateGameData())
                        {
                            case UpdateStatus.Fail:
                                MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
                                break;
                            case UpdateStatus.Success:
                                MessageBox.Show($"Update for game version {databaseManager.LocalVersion.ToString(2) } successful.", "Update status", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
                                Logger.Info("Updated game data to {0}", databaseManager.LocalVersion.ToString(2));
                                break;
                            default:
                                break;
                        }
                    }
                    else if (result == DialogResult.No)
                    {
                        MessageBox.Show("Update skipped. Please know that skipping this update will likely result in incorrect scans.\n" +
                            "\nYou may check for updates again on restarting this application or by using the update manager found" +
                            " under 'options'", "Update declined", MessageBoxButtons.OK);
                    }
                }
                else
                    Logger.Info("Current game data is up to date with data for {0}", databaseManager.LocalVersion.ToString(2));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not check for list updates");
                MessageBox.Show("Could not check for updates. Consider trying again in an hour or so.", "Game Version Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Properties.Settings.Default.LastUpdateCheck = DateTime.Now;
        }

        private void Export_Button_Click(object sender, EventArgs e)
        {
            OpenOptimizerDialog(new GOOD(data), true);
        }

        private void MainForm_Activate()
        {
            BeginInvoke((MethodInvoker)delegate { Activate(); });
        }

        private void ErrorLog_Label_Click(object sender, EventArgs e)
        {
            Process.Start($@"logging");
        }

        private void updateExecutablesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new ExecutablesForm().Show();
        }
    }
}