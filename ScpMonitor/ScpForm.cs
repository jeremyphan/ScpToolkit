﻿using System;
using System.Configuration;
using System.Drawing;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using log4net;
using ReactiveSockets;
using ScpControl;
using ScpControl.Rx;
using ScpControl.Utilities;
using ScpMonitor.Properties;

namespace ScpMonitor
{
    public partial class ScpForm : Form
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        protected bool FormSaved, ConfSaved, ProfSaved, FormVisible;
        protected int FormX, FormY, ConfX, ConfY, ProfX, ProfY;
        private bool m_Connected;
        private readonly ProfilesForm _profiles = new ProfilesForm();
        private readonly ScpByteChannel _rootHubChannel;

        private readonly ReactiveClient _rxCommandClient = new ReactiveClient(Settings.Default.RootHubCommandRxHost,
            Settings.Default.RootHubCommandRxPort);

        private readonly SettingsForm _settings = new SettingsForm(null);
        private readonly byte[] m_Buffer = new byte[2];
        private readonly RegistrySettings m_Config = new RegistrySettings();
        private readonly char[] m_Delim = { '^' };

        public ScpForm()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Log.FatalFormat("Unhandled exception: {0}", args.ExceptionObject);
            };

            InitializeComponent();

            btnUp_1.Tag = (byte)1;
            btnUp_2.Tag = (byte)2;
            btnUp_3.Tag = (byte)3;

            m_Buffer[1] = 0x02;

            FormVisible = m_Config.Visible;

            FormSaved = m_Config.FormSaved;
            FormX = m_Config.FormX;
            FormY = m_Config.FormY;

            ConfSaved = m_Config.ConfSaved;
            ConfX = m_Config.ConfX;
            ConfY = m_Config.ConfY;

            ProfSaved = m_Config.ProfSaved;
            ProfX = m_Config.ProfX;
            ProfY = m_Config.ProfY;

            if (FormSaved)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(FormX, FormY);
            }

            if (!FormVisible)
            {
                WindowState = FormWindowState.Minimized;
                Visible = false;
            }

            if (ConfSaved)
            {
                _settings.StartPosition = FormStartPosition.Manual;
                _settings.Location = new Point(ConfX, ConfY);
            }

            if (ProfSaved)
            {
                _profiles.StartPosition = FormStartPosition.Manual;
                _profiles.Location = new Point(ProfX, ProfY);
            }

            lblHost.Text = "Host Address : 00:00:00:00:00:00\r\n\r\n0\r\n\r\n0\r\n\r\n0";
            lblPad_1.Text = "Pad 1 : DS3 00:00:00:00:00:00 - USB FFFFFFFF Charging";

            var SizeX = 50 + lblHost.Width + lblPad_1.Width;
            var SizeY = 20 + lblHost.Height;

            lblPad_1.Location = new Point(new Size(40 + lblHost.Width, 10 + lblHost.Height / 7 * 0));
            lblPad_2.Location = new Point(new Size(40 + lblHost.Width, 10 + lblHost.Height / 7 * 2));
            lblPad_3.Location = new Point(new Size(40 + lblHost.Width, 10 + lblHost.Height / 7 * 4));
            lblPad_4.Location = new Point(new Size(40 + lblHost.Width, 10 + lblHost.Height / 7 * 6));

            btnUp_1.Location = new Point(lblPad_2.Location.X - 26, lblPad_2.Location.Y - 6);
            btnUp_2.Location = new Point(lblPad_3.Location.X - 26, lblPad_3.Location.Y - 6);
            btnUp_3.Location = new Point(lblPad_4.Location.X - 26, lblPad_4.Location.Y - 6);

            ClientSize = new Size(SizeX, SizeY);

            try
            {
                _rootHubChannel = new ScpByteChannel(_rxCommandClient);

                _rxCommandClient.Disconnected += (sender, args) =>
                {
                    Log.Info("Server connection has been closed");

                    Clear();
                };

                _rxCommandClient.Disposed += (sender, args) =>
                {
                    Log.Info("Server connection has been disposed");

                    Clear();
                };

                _rootHubChannel.Receiver.SubscribeOn(TaskPoolScheduler.Default).Subscribe(packet =>
                {
                    var request = packet.Request;
                    var buffer = packet.Payload;

                    switch (request)
                    {
                        case ScpRequest.StatusData:
                            if (buffer.Length > 0)
                                Parse(buffer);
                            break;
                        case ScpRequest.ConfigRead:
                            _settings.Response(packet);
                            break;
                    }
                });

                _rxCommandClient.ConnectAsync().Wait();
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Couldn't connect to root hub: {0}", ex);
                return;
            }

            _settings = new SettingsForm(_rootHubChannel);
        }

        public void Reset()
        {
            CenterToScreen();
        }

        private void Parse(byte[] buffer)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() =>
                {
                    Parse(buffer);
                }));
                return;
            }

            if (!m_Connected)
            {
                m_Connected = true;
                tmConfig.Enabled = true;
                tmProfile.Enabled = true;

                niTray.BalloonTipText = "Server Connected";
                niTray.ShowBalloonTip(3000);
            }

            var data = buffer.ToUtf8();
            var split = data.Split(m_Delim, StringSplitOptions.RemoveEmptyEntries);

            lblHost.Text = split[0];

            lblPad_1.Text = split[1];
            lblPad_2.Text = split[2];
            btnUp_1.Enabled = !split[2].Contains("Disconnected");
            lblPad_3.Text = split[3];
            btnUp_2.Enabled = !split[3].Contains("Disconnected");
            lblPad_4.Text = split[4];
            btnUp_3.Enabled = !split[4].Contains("Disconnected");
        }

        private void Clear()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(Clear));
                return;
            }

            if (m_Connected)
            {
                m_Connected = false;
                tmConfig.Enabled = false;
                tmProfile.Enabled = false;

                niTray.BalloonTipText = "Server Disconnected";
                niTray.ShowBalloonTip(3000);
            }

            if (_settings.Visible) _settings.Hide();
            if (_profiles.Visible) _profiles.Hide();

            lblHost.Text = "Host Address : Disconnected";

            lblPad_1.Text = "Pad 1 : Disconnected";
            lblPad_2.Text = "Pad 2 : Disconnected";
            lblPad_3.Text = "Pad 3 : Disconnected";
            lblPad_4.Text = "Pad 4 : Disconnected";

            btnUp_1.Enabled = btnUp_2.Enabled = btnUp_3.Enabled = false;
        }

        private void tmrUpdate_Tick(object sender, EventArgs e)
        {
            lock (this)
            {
                tmrUpdate.Enabled = false;

                try
                {
                    if (Visible && Location.X != -32000 && Location.Y != -32000)
                    {
                        FormVisible = true;

                        FormX = Location.X;
                        FormY = Location.Y;
                        FormSaved = true;
                    }
                    else
                    {
                        FormVisible = false;
                    }

                    if (_settings.Visible && _settings.Location.X != -32000 && _settings.Location.Y != -32000)
                    {
                        ConfX = _settings.Location.X;
                        ConfY = _settings.Location.Y;
                        ConfSaved = true;
                    }

                    if (_profiles.Visible && _profiles.Location.X != -32000 && _profiles.Location.Y != -32000)
                    {
                        ProfX = _profiles.Location.X;
                        ProfY = _profiles.Location.Y;
                        ProfSaved = true;
                    }

                    if (_rxCommandClient.IsConnected)
                        _rootHubChannel.SendAsync(ScpRequest.StatusData, m_Buffer);
                }
                catch
                {
                    Clear();
                }

                tmrUpdate.Enabled = true;
            }
        }

        private async void Form_Load(object sender, EventArgs e)
        {
            Icon = niTray.Icon = Resources.Scp_All;

            await Task.Run(() =>
            {
                Thread.Sleep(1000);
                this.BeginInvoke(new Action(() => { tmrUpdate.Enabled = true; }));
            });
        }

        private void Form_Closing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && niTray.Visible)
            {
                e.Cancel = true;

                if (_settings.Visible) _settings.Hide();
                if (_profiles.Visible) _profiles.Hide();

                Visible = false;
                WindowState = FormWindowState.Minimized;
            }
            else
            {
                tmrUpdate.Enabled = false;

                m_Config.Visible = FormVisible;

                m_Config.FormSaved = FormSaved;
                m_Config.FormX = FormX;
                m_Config.FormY = FormY;

                m_Config.ConfSaved = ConfSaved;
                m_Config.ConfX = ConfX;
                m_Config.ConfY = ConfY;

                m_Config.ProfSaved = ProfSaved;
                m_Config.ProfX = ProfX;
                m_Config.ProfY = ProfY;

                m_Config.Save();
            }
        }

        private void btnUp_Click(object sender, EventArgs e)
        {
            byte[] buffer = { 0, 5, (byte)((Button)sender).Tag };

            if (_rxCommandClient.IsConnected)
                _rootHubChannel.SendAsync(ScpRequest.PadPromote, buffer);
        }

        private void niTray_Click(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
                Visible = true;

                Activate();
            }
            else
            {
                if (_settings.Visible) _settings.Hide();
                if (_profiles.Visible) _profiles.Hide();

                Visible = false;
                WindowState = FormWindowState.Minimized;
            }
        }

        private void tmConfig_Click(object sender, EventArgs e)
        {
            if (!_settings.Visible)
            {
                _settings.Request();
                _settings.Show(this);
            }

            _settings.Activate();
        }

        private void tmProfile_Click(object sender, EventArgs e)
        {
            if (!_profiles.Visible)
            {
                _profiles.Request();
                _profiles.Show(this);
            }

            _profiles.Activate();
        }

        private void tmReset_Click(object sender, EventArgs e)
        {
            lock (this)
            {
                tmrUpdate.Enabled = false;

                Reset();
                _settings.Reset();
                _profiles.Reset();

                tmrUpdate.Enabled = true;
            }
        }

        private void tmExit_Click(object sender, EventArgs e)
        {
            niTray.Visible = false;
            Close();
        }

        private void Button_Enter(object sender, EventArgs e)
        {
            ThemeUtil.UpdateFocus(((Button)sender).Handle);
        }
    }

    [SettingsProvider(typeof(RegistryProvider))]
    public class RegistrySettings : ApplicationSettingsBase
    {
        [UserScopedSetting, DefaultSettingValue("true")]
        public bool Visible
        {
            get { return (bool)this["Visible"]; }
            set { this["Visible"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("false")]
        public bool FormSaved
        {
            get { return (bool)this["FormSaved"]; }
            set { this["FormSaved"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("false")]
        public bool ConfSaved
        {
            get { return (bool)this["ConfSaved"]; }
            set { this["ConfSaved"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("false")]
        public bool ProfSaved
        {
            get { return (bool)this["ProfSaved"]; }
            set { this["ProfSaved"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("-32000")]
        public int FormX
        {
            get { return (int)this["FormX"]; }
            set { this["FormX"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("-32000")]
        public int FormY
        {
            get { return (int)this["FormY"]; }
            set { this["FormY"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("-32000")]
        public int ConfX
        {
            get { return (int)this["ConfX"]; }
            set { this["ConfX"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("-32000")]
        public int ConfY
        {
            get { return (int)this["ConfY"]; }
            set { this["ConfY"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("-32000")]
        public int ProfX
        {
            get { return (int)this["ProfX"]; }
            set { this["ProfX"] = value; }
        }

        [UserScopedSetting, DefaultSettingValue("-32000")]
        public int ProfY
        {
            get { return (int)this["ProfY"]; }
            set { this["ProfY"] = value; }
        }
    }
}