﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BotCore;
using BotCore.Dto;
using TwitchBotUI.Properties;

namespace TwitchBotUI
{
    public partial class MainScreen : Form
    {
        public bool Start = false;

        private static string _productVersion = "1.5";

        private static string _proxyListDirectory = "";

        private static bool _headless = false;

        private bool _withLoggedIn = false;

        CancellationTokenSource _tokenSource = new CancellationTokenSource();

        readonly CancellationToken _token = new CancellationToken();

        readonly Configuration _configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        readonly Dictionary<string, string> _dataSourceQuality = new Dictionary<string, string>();

        private readonly ConcurrentQueue<LoginDto> _lstLoginInfo = new ConcurrentQueue<LoginDto>();

        public Core Core = new Core();

        public MainScreen()
        {
            InitializeComponent();

            Text += " v" + _productVersion;

            LogInfo("Application started.");
            LoadFromAppSettings();
            var isAvailable = IsNewerVersionAvailable();

            if (isAvailable)
                UpdateBot();

            #region StreamQuality

            FillQualityItems();

            void FillQualityItems()
            {
                _dataSourceQuality.Add("Source", string.Empty);
                _dataSourceQuality.Add("480p", "{\"default\":\"480p30\"}");
                _dataSourceQuality.Add("360p", "{\"default\":\"360p30\"}");
                _dataSourceQuality.Add("160p", "{\"default\":\"160p30\"}");

                lstQuality.ValueMember = "Value";
                lstQuality.DisplayMember = "Key";
                lstQuality.DataSource = new BindingSource(_dataSourceQuality, null);
                lstQuality.SelectedIndex = _dataSourceQuality.Count - 1;
            }
            #endregion

            MaximumSize = new Size(836, 374);
            MinimumSize = new Size(517, 374);
        }

        public sealed override Size MinimumSize
        {
            get => base.MinimumSize;
            set => base.MinimumSize = value;
        }

        public sealed override Size MaximumSize
        {
            get => base.MaximumSize;
            set => base.MaximumSize = value;
        }

        private bool IsNewerVersionAvailable()
        {
            try
            {
                var webRequest = WebRequest.Create(@"https://mytwitchbot.com/Download/latestVersion.txt");
                webRequest.Headers.Add("Accept: text/html, application/xhtml+xml, */*");
                webRequest.Headers.Add("User-Agent: Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; WOW64; Trident/5.0)");
                using var response = webRequest.GetResponse();
                using var content = response.GetResponseStream();
                using var reader = new StreamReader(content);
                var latestVersion = reader.ReadToEnd();

                return latestVersion != _productVersion;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UpdateBot()
        {
            DialogResult dialogResult = MessageBox.Show(GetFromResource("MainScreen_UpdateBot_Do_you_want_to_update_"), GetFromResource("MainScreen_UpdateBot_Newer_version_is_available_"), MessageBoxButtons.YesNo);

            if (dialogResult == DialogResult.Yes)
            {
                var args = "https://mytwitchbot.com/Download/win-x64.zip" + " " + Directory.GetCurrentDirectory() + " " + Path.Combine(Directory.GetCurrentDirectory(), "TwitchBotUI.exe");
                try
                {
                    Directory.Delete(Path.Combine(Directory.GetCurrentDirectory(), "AutoUpdaterOld"), true);
                }
                catch (Exception)
                {
                    //ignored
                }
                try
                {
                    var tempUpdaterPath = Path.Combine(Directory.GetCurrentDirectory(), "AutoUpdaterTemp");
                    var updaterPath = Path.Combine(Directory.GetCurrentDirectory(), "AutoUpdater");
                    Directory.CreateDirectory(tempUpdaterPath);
                    foreach (var file in Directory.GetFiles(updaterPath))
                    {
                        string destFile = Path.Combine(tempUpdaterPath, Path.GetFileName(file));
                        File.Move(file, destFile, true);
                    }
                    var filename = Path.Combine(tempUpdaterPath, "AutoUpdater.exe");
                    Process.Start(filename, args);
                    Environment.Exit(0);
                }
                catch (Exception)
                {
                    MessageBox.Show(GetFromResource("MainScreen_UpdateBot_Sorry__updater_failed_"));
                }
            }
        }

        private string GetFromResource(string key)
        {
            return Resources.ResourceManager.GetString(key);
        }

        private void LoadFromAppSettings()
        {
            LogInfo("Reading configuration.");
            _proxyListDirectory = txtProxyList.Text = _configuration.AppSettings.Settings["proxyListDirectory"].Value;
            txtStreamUrl.Text = _configuration.AppSettings.Settings["streamUrl"].Value;
            _headless = checkHeadless.Checked = Convert.ToBoolean(_configuration.AppSettings.Settings["headless"].Value);
            numRefreshMinutes.Value = Convert.ToInt32(_configuration.AppSettings.Settings["refreshInterval"].Value);
            _withLoggedIn = Convert.ToBoolean(_configuration.AppSettings.Settings["withLoggedIn"].Value);
            txtLoginInfos.Text = _configuration.AppSettings.Settings["loginInfos"].Value;

            ShowLoggedInPart(_withLoggedIn);
        }

        private void ShowLoggedInPart(bool visibility)
        {
            this.ClientSize = visibility ? new Size(820, 335) : new Size(517, 335);
        }

        private void startStopButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtProxyList.Text) || string.IsNullOrEmpty(txtStreamUrl.Text))
            {
                LogInfo("Please choose a proxy directory and enter your stream URL.");
                return;
            }

            _lstLoginInfo.Clear();

            if (_withLoggedIn)
            {
                foreach (var line in txtLoginInfos.Text.Split("\r\n"))
                {
                    var parts = line.Split(' ');

                    if (parts.Length != 2)
                    {
                        LogInfo("Please correct the format of your login credentials");

                        return;
                    }

                    _lstLoginInfo.Enqueue(new LoginDto() { Username = parts[0], Password = parts[1] });
                }
            }

            Start = !Start;

            if (Start)
            {
                startStopButton.BackgroundImage = Image.FromFile(Directory.GetCurrentDirectory() + "\\Images\\button_stop.png");
                LogInfo("Initializing bot.");
                Core.CanRun = true;
                TaskFactory factory = new TaskFactory(_token);
                _tokenSource = new CancellationTokenSource();

                Task.Run(() =>
                {
                    RunIt(null);
                }, _tokenSource.Token);

                ConfigurationManager.RefreshSection("appSettings");
            }
            else
            {
                startStopButton.BackgroundImage = Image.FromFile(Directory.GetCurrentDirectory() + "\\Images\\button_stopping.png");
                startStopButton.Enabled = false;
                LogInfo("Terminating bot, please wait.");

                _tokenSource.Cancel();
                Core.CanRun = false;

                try
                {
                    Core.Stop();
                }
                catch (Exception)
                {
                    LogInfo("Termination error. (Ignored)");
                }

                startStopButton.BackgroundImage = Image.FromFile(Directory.GetCurrentDirectory() + "\\Images\\button_start.png");
                startStopButton.Enabled = true;
            }
        }

        private void RunIt(object obj)
        {
            LogInfo("Saving the configuration.");

            _configuration.AppSettings.Settings["streamUrl"].Value = txtStreamUrl.Text;
            _configuration.AppSettings.Settings["headless"].Value = checkHeadless.Checked.ToString();
            _configuration.AppSettings.Settings["proxyListDirectory"].Value = txtProxyList.Text;
            _configuration.AppSettings.Settings["refreshInterval"].Value = numRefreshMinutes.Value.ToString();
            _configuration.AppSettings.Settings["withLoggedIn"].Value = _withLoggedIn.ToString();
            _configuration.AppSettings.Settings["loginInfos"].Value = txtLoginInfos.Text;
            _configuration.Save(ConfigurationSaveMode.Modified);

            LogInfo("Configuration saved.");
            LogInfo("Bot is starting.");

            Int32.TryParse(txtBrowserLimit.Text, out var browserLimit);

            _headless = checkHeadless.Checked;

            Core.AllBrowsersTerminated += AllBrowsersTerminated;

            Core.InitializationError += ErrorOccured;

            Core.LogMessage += LogMessage;

            Core.DidItsJob += DidItsJob;

            Core.IncreaseViewer += IncreaseViewer;

            Core.DecreaseViewer += DecreaseViewer;

            Core.LiveViewer += LiveViewer;

            var quality = string.Empty;

            if (lstQuality.InvokeRequired)
            {
                lstQuality.BeginInvoke(new Action(() =>
                {
                    quality = lstQuality.SelectedValue.ToString();
                }));
            }
            else
            {
                quality = lstQuality.SelectedValue.ToString();
            }

            Core.Start(_proxyListDirectory, txtStreamUrl.Text, _headless, browserLimit, Convert.ToInt32(numRefreshMinutes.Value), quality, _lstLoginInfo);
        }

        private void LiveViewer(string count)
        {
            if (lblLiveViewer.InvokeRequired)
            {
                lblLiveViewer.BeginInvoke(new Action(() =>
                {
                    lblLiveViewer.Text = count;
                }));
            }
            else
            {
                lblLiveViewer.Text = count;
            }
        }

        private void DecreaseViewer()
        {
            if (lblViewer.InvokeRequired)
            {
                lblViewer.BeginInvoke(new Action(() =>
                {
                    lblViewer.Text = (Convert.ToInt32(lblViewer.Text) - 1).ToString();
                }));
            }
            else
            {
                lblViewer.Text = (Convert.ToInt32(lblViewer.Text) - 1).ToString();
            }
        }

        private void IncreaseViewer()
        {
            if (lblViewer.InvokeRequired)
            {
                lblViewer.BeginInvoke(new Action(() =>
                {
                    lblViewer.Text = (Convert.ToInt32(lblViewer.Text) + 1).ToString();
                }));
            }
            else
            {
                lblViewer.Text = (Convert.ToInt32(lblViewer.Text) + 1).ToString();
            }
        }

        private void ErrorOccured(string message)
        {
            Core.Stop();

            LogError(message);

            Core.AllBrowsersTerminated -= AllBrowsersTerminated;

            Core.InitializationError -= ErrorOccured;

            Core.LogMessage -= LogMessage;

            Core.DidItsJob -= DidItsJob;

            Core.IncreaseViewer -= IncreaseViewer;

            Core.DecreaseViewer -= DecreaseViewer;
        }

        private void LogMessage(string message)
        {
            LogInfo(message);
        }

        private void AllBrowsersTerminated()
        {
            startStopButton.BackgroundImage = Image.FromFile(Directory.GetCurrentDirectory() + "\\Images\\button_stop.png");

            LogInfo("Bot terminated.");
        }

        private void DidItsJob()
        {
            Core.InitializationError -= ErrorOccured;

            Core.LogMessage -= LogMessage;

            Core.DidItsJob -= DidItsJob;

            Core.IncreaseViewer -= IncreaseViewer;

            Core.DecreaseViewer -= DecreaseViewer;

            LogInfo("Bot did it's job.");
        }

        private void LogInfo(string str)
        {
            if (logScreen.InvokeRequired)
            {
                logScreen.BeginInvoke(new Action(() =>
                {
                    logScreen.Text += DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss | ") + str + "\r\n";
                    logScreen.SelectionStart = logScreen.TextLength;
                    logScreen.ScrollToCaret();
                }));
            }
            else
            {
                logScreen.Text += DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss | ") + str + "\r\n";
                logScreen.SelectionStart = logScreen.TextLength;
                logScreen.ScrollToCaret();
            }
        }

        private void LogError(string str)
        {
            if (logScreen.InvokeRequired)
            {
                logScreen.BeginInvoke(new Action(() =>
                {
                    logScreen.Text += DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss | ") + str + "\r\n";
                    logScreen.SelectionStart = logScreen.TextLength;
                    logScreen.ScrollToCaret();
                }));
            }
            else
            {
                logScreen.Text += DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss | ") + str + "\r\n";
                logScreen.SelectionStart = logScreen.TextLength;
                logScreen.ScrollToCaret();
            }
        }

        private void browseProxyList_Click(object sender, EventArgs e)
        {
            OpenFileDialog fileDialog = new OpenFileDialog();

            fileDialog.Filter = "txt files (*.txt)|*.txt";
            fileDialog.FilterIndex = 1;
            fileDialog.Multiselect = false;

            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                _proxyListDirectory = txtProxyList.Text = fileDialog.FileName;
            }
        }

        private void MainScreen_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {
            try
            {
                string strCmdLine = "/C explorer \"https://www.vultr.com/?ref=8827163\"";
                var browserProcess = Process.Start("CMD.exe", strCmdLine);
                browserProcess?.Close();
            }
            catch (Exception)
            {
                //ignored
            }
        }

        private void picLimitInfo_MouseHover(object sender, EventArgs e)
        {
            toolTip.SetToolTip(tipLimitInfo, "Rotates proxies with limited quantity of browser. Old ones dies, new ones born. 0 means, no limit.");
        }

        private void lblProxyList_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                string strCmdLine = "/C explorer \"https://github.com/gorkemhacioglu/TwitchViewerBot/wiki/Configuration";
                var browserProcess = Process.Start("CMD.exe", strCmdLine);
                browserProcess?.Close();
            }
            catch (Exception)
            {
                //ignored
            }
        }

        private void refreshInterval_MouseHover(object sender, EventArgs e)
        {
            toolTip.SetToolTip(tipRefreshBrowser, "Refreshes browser, just in case.");
        }

        private void txtBrowserLimit_TextChanged(object sender, EventArgs e)
        {
            var value = txtBrowserLimit.Text;

            if (value == "0" || value == string.Empty)
            {
                lblRefreshMin.Enabled = lblRefreshMin2.Enabled = lblRefreshMin3.Enabled = numRefreshMinutes.Enabled = tipRefreshBrowser.Enabled = true;
            }
            else
            {
                lblRefreshMin.Enabled = lblRefreshMin2.Enabled = lblRefreshMin3.Enabled = numRefreshMinutes.Enabled = tipRefreshBrowser.Enabled = false;
                numRefreshMinutes.Value = 0;
                _withLoggedIn = false;
                ShowLoggedInPart(false);
            }
        }

        private void lstQuality_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstQuality.InvokeRequired)
            {
                lstQuality.BeginInvoke(new Action(() =>
                {
                    Core.PreferredQuality = lstQuality.SelectedValue.ToString();
                }));
            }
            else
            {
                Core.PreferredQuality = lstQuality.SelectedValue.ToString();
            }
        }

        private void streamQuality_MouseHover(object sender, EventArgs e)
        {
            toolTip.SetToolTip(tipQuality, "Stream quality.");
        }

        private void btnWithLoggedIn_Click(object sender, EventArgs e)
        {
            _withLoggedIn = !_withLoggedIn;

            ShowLoggedInPart(_withLoggedIn);
        }

        private void tipLiveViewer_MouseHover(object sender, EventArgs e)
        {
            toolTip.SetToolTip(tipLiveViewer, "Your bots will be here soon.");
        }
    }
}
