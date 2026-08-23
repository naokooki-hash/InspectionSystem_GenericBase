using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace InspectionSystem_GenericBase
{
    public class Form1 : Form
    {
        private const int STATE_WAITING = 0;
        private const int STATE_STABILIZING = 1;
        private const int STATE_COOLING = 2;

        private TeliCamera _camera;
        private MeasurementCore _measurement;
        private InspectionEngine _inspectionEngine;
        private PlcCommunicator _plc;
        private AppSettings _appSettings;
        private ProductionAnalyzer _analyzer = new ProductionAnalyzer();

        private PictureBox _pictureBoxMain = null!;
        private PictureBox _pictureBoxDebug = null!;
        private TabControl _tabControl = null!;
        private TextBox _txtLog = null!;

        private Label _lblStatus = null!;
        private Label _lblBrightness = null!;
        private Label _lblFps = null!;
        private Label _lblBigResult = null!, _lblAlignmentStatus = null!;
        private Label _lblTotal = null!, _lblOk = null!, _lblNg = null!, _lblCurrentHoleDistPx = null!;

        private CheckBox _chkShowOverlay = null!, _chkEnableJigCheck = null!;
        private CheckBox _chkEnableOuterTiltCheck = null!, _chkEnableHoleCheck = null!;

        private ComboBox _cmbTriggerMode = null!, _cmbSaveMode = null!;

        private Button _btnRunToggle = null!;
        private bool _isRunning = false;
        private bool _requestErrorTest = false;
        private bool _requestOkTest = false; // ★テスト用: 強制OKフラグ

        private bool _isAdminMode = false;
        private Button _btnAdminLogin = null!;

        private NumericUpDown _nudTriggerThreshold = null!, _nudStabilityDuration = null!, _nudResetThreshold = null!;
        private NumericUpDown _nudExposureTime = null!;
        private NumericUpDown _nudRoiX = null!, _nudRoiY = null!, _nudRoiW = null!, _nudRoiH = null!;

        // Test mode variables
        private GroupBox _gbTestMode = null!;
        private CheckBox _chkTestModeEnable = null!;
        private Button _btnSelectTestFolder = null!;
        private Button _btnPrevImage = null!;
        private Button _btnNextImage = null!;
        private CheckBox _chkAutoPlay = null!;
        private Label _lblTestImageInfo = null!;

        private bool _isTestModeEnabled = false;
        private string _testImagesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestImages");
        private List<string> _testImageFiles = new List<string>();
        private int _currentTestImageIndex = 0;
        private System.Windows.Forms.Timer _autoPlayTimer = null!;
        private NumericUpDown _nudSaveRoiX = null!, _nudSaveRoiY = null!, _nudSaveRoiW = null!, _nudSaveRoiH = null!;

        private NumericUpDown _nudLogKeepDays = null!;
        private int _logKeepDays = 30;

        private NumericUpDown _nudAutoStartCount = null!;
        private int _autoStartCount = 3;
        private int _missedTriggerCount = 0;
        private bool _wasTriggeredLastFrame = false;

        private NumericUpDown _nudBtmRoiX = null!, _nudBtmRoiY = null!, _nudBtmRoiW = null!, _nudBtmRoiH = null!;

        private NumericUpDown _nudBtmInnerLX = null!, _nudBtmInnerLY = null!, _nudBtmInnerLW = null!, _nudBtmInnerLH = null!;
        private NumericUpDown _nudBtmInnerRX = null!, _nudBtmInnerRY = null!, _nudBtmInnerRW = null!, _nudBtmInnerRH = null!;

        private NumericUpDown _nudHolesX = null!, _nudHolesY = null!, _nudHolesW = null!, _nudHolesH = null!;
        private NumericUpDown _nudMinHoleArea = null!, _nudMaxHoleArea = null!, _nudMinCircularity = null!;

        private NumericUpDown _nudTiltLX = null!, _nudTiltLY = null!, _nudTiltLW = null!, _nudTiltLH = null!;
        private NumericUpDown _nudTiltRX = null!, _nudTiltRY = null!, _nudTiltRW = null!, _nudTiltRH = null!;

        private NumericUpDown _nudThreshOuterL = null!, _nudThreshOuterR = null!;
        private NumericUpDown _nudThreshBtmInnerL = null!, _nudThreshBtmInnerR = null!;

        private NumericUpDown _nudSplitX = null!, _nudSplitY = null!;
        private NumericUpDown _nudThreshTL = null!, _nudThreshTR = null!, _nudThreshBL = null!, _nudThreshBR = null!;

        private NumericUpDown _nudJigLX = null!, _nudJigLY = null!, _nudJigLW = null!, _nudJigLH = null!;
        private NumericUpDown _nudJigRX = null!, _nudJigRY = null!, _nudJigRW = null!, _nudJigRH = null!;
        private NumericUpDown _nudJigTarget = null!, _nudJigTolerance = null!, _nudPixelToMm = null!;

        private NumericUpDown _nudOuterTargetX = null!, _nudOuterOffsetX = null!, _nudOuterTargetA = null!, _nudOuterOffsetA = null!;
        private NumericUpDown _nudTargetXOffset = null!, _nudOffsetTolerance = null!, _nudTargetAngle = null!, _nudAngleTolerance = null!;
        private NumericUpDown _nudActualWidthMm = null!;

        private NumericUpDown _nudPlcDelayMs = null!;
        private int _plcDelayMs = 100;

        private NumericUpDown _nudRetryCount = null!;
        private NumericUpDown _nudRetryDelayMs = null!;
        private int _maxRetryCount = 3;
        private int _retryDelayMs = 100;
        private int _currentRetry = 0;

        private Button _btnCalcRatio = null!;

        private int _currentState = STATE_WAITING;
        private DateTime _stabilityStartTime = DateTime.MinValue;
        private DateTime _cooldownStartTime = DateTime.MinValue;
        private int _cooldownDurationMs = 500;

        private CvRect _roi = new CvRect(300, 200, 100, 100);
        private CvRect _saveRoi = new CvRect(100, 50, 440, 380);

        private bool _requestManualTest = false;
        private bool _isLoadingConfig = false, _isUiLoaded = false, _isUpdatingUI = false;
        private bool _isProcessing = false;
        private bool _isMonitoring = false;
        private bool _plcTriggerReceived = false;

        // 各カメラの本来の検査設定（フォールバック復元用）
        private bool _configEnableOuterTilt = true;
        private bool _configEnableHole = true;

        private int _totalCount = 0, _okCount = 0, _ngCount = 0, _saveMode = 0, _stabilityDurationMs = 300;
        private int _pendingSaveResult = -1;
        private InspectionResult? _lastInspectionResult;
        private bool _triggerOnBright = true;
        private double _triggerThreshold = 100.0, _resetThreshold = 50.0;
        private string _logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        private DateTime _lastUiUpdateTime = DateTime.MinValue;
        private DateTime _lastFrameProcessTime = DateTime.MinValue;
        private bool _isDebugTabActive = false;

        private int _camFrameCount;
        private int _procFrameCount;
        private int _uiFrameCount;
        private DateTime _lastFpsTime = DateTime.Now;
        private string _currentFpsText = "Cam FPS: --";

        public Form1()
        {
            _appSettings = AppSettings.Load();
            _camera = new TeliCamera();
            _measurement = new MeasurementCore();
            _inspectionEngine = _measurement;
            _plc = new PlcCommunicator(_appSettings);

            InitializeCustomUI();
            _camera.OnFrameCaptured += (s, e) => Camera_OnFrameCaptured(s, e);

            _plc.OnLog += (msg, isErr) => AppendLog(msg, isErr);

            LoadConfig();
            UpdateUiForAdminMode();

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;

            AppendLog("アプリケーションを起動しました（シングルカメラ構成）。");
        }

        private void InitializeCustomUI()
        {
            this.Text = "Punching Metal Auto Inspection System (Single Engine Base)";
            this.Size = new Size(1920, 1000);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 1台カメラ用メイン表示領域の最大化（1280x780）
            _pictureBoxMain = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(1280, 780),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(_pictureBoxMain);

            _lblStatus = new Label { Text = "Status: STOPPED", Location = new Point(10, 800), AutoSize = true, Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold), ForeColor = Color.Red };
            _lblBrightness = new Label { Text = "Brightness: 0.0", Location = new Point(10, 830), AutoSize = true, Font = new Font(this.Font.FontFamily, 12) };
            _lblFps = new Label { Text = "Cam FPS: --", Location = new Point(200, 830), AutoSize = true, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold), ForeColor = Color.Blue };
            this.Controls.Add(_lblStatus); this.Controls.Add(_lblBrightness); this.Controls.Add(_lblFps);

            _txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(10, 500),
                Size = new Size(640, 290),
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9)
            };

            int tabX = 1310;
            _tabControl = new TabControl { Location = new Point(tabX, 10), Size = new Size(580, 930), Font = new Font(this.Font.FontFamily, 10) };
            _tabControl.SelectedIndexChanged += (s, e) => { _isDebugTabActive = (_tabControl.SelectedIndex == 3); };
            this.Controls.Add(_tabControl);

            TabPage t1 = new TabPage("運用 (Main)"); InitializeMainTab(t1); _tabControl.TabPages.Add(t1);
            TabPage t2 = new TabPage("設定 (Settings)") { AutoScroll = true }; InitializeSettingsTab(t2); _tabControl.TabPages.Add(t2);
            TabPage t3 = new TabPage("検査設定 (Inspection)") { AutoScroll = true }; InitializeInspectionTab(t3); _tabControl.TabPages.Add(t3);
            TabPage t4 = new TabPage("画像確認 (Debug)") { AutoScroll = true }; InitializeDebugTab(t4); _tabControl.TabPages.Add(t4);
            TabPage t5 = new TabPage("PLC設定 (PLC Comms)") { AutoScroll = true }; InitializePlcCommsTab(t5); _tabControl.TabPages.Add(t5);

            _btnAdminLogin = new Button { Text = "管理者ログイン", Location = new Point(tabX + 430, 10), Size = new Size(140, 25) };
            _btnAdminLogin.Click += BtnAdminLogin_Click;
            this.Controls.Add(_btnAdminLogin);
            _btnAdminLogin.BringToFront();
        }

        private void BtnAdminLogin_Click(object? sender, EventArgs e)
        {
            if (_isAdminMode)
            {
                _isAdminMode = false;
                _btnAdminLogin.Text = "管理者ログイン";
                UpdateUiForAdminMode();
                return;
            }

            using (var prompt = new Form())
            {
                prompt.Width = 300;
                prompt.Height = 150;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = "管理者ログイン";
                prompt.StartPosition = FormStartPosition.CenterScreen;

                Label textLabel = new Label() { Left = 50, Top=20, Text="パスワード:" };
                TextBox textBox = new TextBox() { Left = 50, Top=50, Width=180, PasswordChar='*' };
                Button confirmation = new Button() { Text = "OK", Left=130, Width=100, Top=80, DialogResult = DialogResult.OK };

                prompt.Controls.Add(textBox);
                prompt.Controls.Add(confirmation);
                prompt.Controls.Add(textLabel);
                prompt.AcceptButton = confirmation;

                if (prompt.ShowDialog() == DialogResult.OK)
                {
                    if (textBox.Text == _appSettings.AdminPassword)
                    {
                        _isAdminMode = true;
                        _btnAdminLogin.Text = "管理者ログアウト";
                        UpdateUiForAdminMode();
                    }
                    else
                    {
                        MessageBox.Show("パスワードが違います。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void BtnChangePassword_Click(object? sender, EventArgs e)
        {
            using (var prompt = new Form())
            {
                prompt.Width = 350;
                prompt.Height = 220;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = "管理者パスワードの変更";
                prompt.StartPosition = FormStartPosition.CenterScreen;

                Label lblCurrent = new Label { Left = 20, Top = 20, Width = 130, Text = "現在のパスワード:" };
                TextBox txtCurrent = new TextBox { Left = 160, Top = 20, Width = 150, PasswordChar = '*' };

                Label lblNew = new Label { Left = 20, Top = 55, Width = 130, Text = "新しいパスワード:" };
                TextBox txtNew = new TextBox { Left = 160, Top = 55, Width = 150, PasswordChar = '*' };

                Label lblConfirm = new Label { Left = 20, Top = 90, Width = 130, Text = "新しいパスワード(確認):" };
                TextBox txtConfirm = new TextBox { Left = 160, Top = 90, Width = 150, PasswordChar = '*' };

                Button btnOk = new Button { Text = "変更", Left = 110, Top = 135, Width = 90, DialogResult = DialogResult.OK };
                Button btnCancel = new Button { Text = "キャンセル", Left = 210, Top = 135, Width = 90, DialogResult = DialogResult.Cancel };

                prompt.Controls.Add(lblCurrent); prompt.Controls.Add(txtCurrent);
                prompt.Controls.Add(lblNew); prompt.Controls.Add(txtNew);
                prompt.Controls.Add(lblConfirm); prompt.Controls.Add(txtConfirm);
                prompt.Controls.Add(btnOk); prompt.Controls.Add(btnCancel);
                prompt.AcceptButton = btnOk;
                prompt.CancelButton = btnCancel;

                if (prompt.ShowDialog() == DialogResult.OK)
                {
                    if (txtCurrent.Text != _appSettings.AdminPassword)
                    {
                        MessageBox.Show("現在のパスワードが一致しません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    if (string.IsNullOrEmpty(txtNew.Text))
                    {
                        MessageBox.Show("新しいパスワードを入力してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    if (txtNew.Text != txtConfirm.Text)
                    {
                        MessageBox.Show("新しいパスワードと確認用パスワードが一致しません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _appSettings.AdminPassword = txtNew.Text;
                    _appSettings.Save();
                    MessageBox.Show("管理者パスワードを更新しました。", "通知", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void InitializePlcCommsTab(TabPage tab)
        {
            int y = 10, lw = 150, cw = 120, lh = 28;
            void AddL(string txt) { tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) }); }

            tab.Controls.Add(new Label { Text = "--- PLC通信設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;

            AddL("IPアドレス:");
            TextBox txtIp = new TextBox { Location = new Point(10 + lw, y), Size = new Size(cw, 25), Text = _appSettings.PlcIpAddress };
            txtIp.TextChanged += (s, e) => { _appSettings.PlcIpAddress = txtIp.Text; }; tab.Controls.Add(txtIp); y += lh;

            AddL("ポート:");
            NumericUpDown nudPort = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 1, Maximum = 65535, Value = _appSettings.PlcPort };
            nudPort.ValueChanged += (s, e) => { _appSettings.PlcPort = (int)nudPort.Value; }; tab.Controls.Add(nudPort); y += lh;

            AddL("ベンダー:");
            ComboBox cmbVendor = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbVendor.Items.AddRange(new object[] { "Mitsubishi", "Keyence" });
            cmbVendor.SelectedItem = _appSettings.PlcVendor;
            cmbVendor.SelectedIndexChanged += (s, e) => { _appSettings.PlcVendor = cmbVendor.SelectedItem?.ToString() ?? "Mitsubishi"; }; tab.Controls.Add(cmbVendor); y += lh;

            AddL("データ型:");
            ComboBox cmbDataType = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbDataType.Items.AddRange(new object[] { "Bit", "Word" });
            cmbDataType.SelectedItem = _appSettings.PlcDataType;
            cmbDataType.SelectedIndexChanged += (s, e) => { _appSettings.PlcDataType = cmbDataType.SelectedItem?.ToString() ?? "Bit"; }; tab.Controls.Add(cmbDataType); y += lh;
            y += 10;

            tab.Controls.Add(new Label { Text = "--- デバイスアドレス設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;

            AddL("Heartbeat (常時監視):");
            NumericUpDown nudHeartbeat = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999, Value = _appSettings.HeartbeatAddress };
            nudHeartbeat.ValueChanged += (s, e) => { _appSettings.HeartbeatAddress = (int)nudHeartbeat.Value; }; tab.Controls.Add(nudHeartbeat); y += lh;
            y += 10;

            tab.Controls.Add(new Label { Text = "【カメラ設定】", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkOrange }); y += 22;
            AddL("読取 (Trigger):");
            NumericUpDown nudCRead = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999, Value = _appSettings.Cam.ReadDeviceAddress };
            nudCRead.ValueChanged += (s, e) => { _appSettings.Cam.ReadDeviceAddress = (int)nudCRead.Value; }; tab.Controls.Add(nudCRead); y += lh;
            AddL("書込 (OK):");
            NumericUpDown nudCOk = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999, Value = _appSettings.Cam.OkDeviceAddress };
            nudCOk.ValueChanged += (s, e) => { _appSettings.Cam.OkDeviceAddress = (int)nudCOk.Value; _appSettings.Cam.WriteDeviceAddress = (int)nudCOk.Value; }; tab.Controls.Add(nudCOk); y += lh;
            AddL("書込 (NG):");
            NumericUpDown nudCNg = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999, Value = _appSettings.Cam.NgDeviceAddress };
            nudCNg.ValueChanged += (s, e) => { _appSettings.Cam.NgDeviceAddress = (int)nudCNg.Value; }; tab.Controls.Add(nudCNg); y += lh;
            y += 20;

            Button btnSaveAndReconnect = new Button { Text = "保存して再接続", Location = new Point(10, y), Size = new Size(300, 40), BackColor = Color.LightGreen };
            btnSaveAndReconnect.Click += (s, e) => {
                _appSettings.Save();
                _plc.Disconnect();
                _plc.Connect();
                MessageBox.Show("設定を保存し、再接続を試みました。");
            };
            tab.Controls.Add(btnSaveAndReconnect);
        }

        private void InitializeMainTab(TabPage tab)
        {
            int y = 10;
            _btnRunToggle = new Button { Text = "▶ 運転開始 (START)", Location = new Point(10, y), Size = new Size(540, 60), BackColor = Color.LightGreen, Font = new Font(this.Font.FontFamily, 16, FontStyle.Bold) };
            _btnRunToggle.Click += (s, e) => {
                _isRunning = !_isRunning; _missedTriggerCount = 0;
                if (_isRunning) {
                    _btnRunToggle.Text = "■ 運転停止 (STOP)"; _btnRunToggle.BackColor = Color.Salmon;
                    _currentState = STATE_WAITING;
                    SafeInvoke(() => lblStateUpdate("READY", Color.LightGray));
                }
                else {
                    _btnRunToggle.Text = "▶ 運転開始 (START)"; _btnRunToggle.BackColor = Color.LightGreen;
                    _currentState = STATE_WAITING;
                    SafeInvoke(() => lblStateUpdate("STOPPED", Color.DarkGray));
                }
            };
            tab.Controls.Add(_btnRunToggle); y += 75;

            _chkShowOverlay = new CheckBox { Text = "計測パラメータを表示する", Location = new Point(10, y), AutoSize = true, Checked = true };
            tab.Controls.Add(_chkShowOverlay); y += 30;

            _lblAlignmentStatus = new Label { Text = "ALIGNMENT: STOPPED", Location = new Point(10, y), Size = new Size(540, 50), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 18, FontStyle.Bold), BackColor = Color.DarkGray, ForeColor = Color.White };
            tab.Controls.Add(_lblAlignmentStatus); y += 60;

            _lblBigResult = new Label { Text = "TOTAL: STOPPED", Location = new Point(10, y), Size = new Size(540, 80), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 24, FontStyle.Bold), BackColor = Color.DarkGray, ForeColor = Color.White };
            tab.Controls.Add(_lblBigResult); y += 95;

            GroupBox gp = new GroupBox { Text = "生産カウンター", Location = new Point(10, y), Size = new Size(540, 110) };
            _lblTotal = new Label { Text = "総検査数 : 0", Location = new Point(20, 25), AutoSize = true };
            _lblOk = new Label { Text = "良品 (OK): 0", Location = new Point(20, 50), AutoSize = true, ForeColor = Color.Green, Font = new Font(this.Font, FontStyle.Bold) };
            _lblNg = new Label { Text = "不良 (NG): 0", Location = new Point(20, 75), AutoSize = true, ForeColor = Color.Red, Font = new Font(this.Font, FontStyle.Bold) };
            gp.Controls.Add(_lblTotal); gp.Controls.Add(_lblOk); gp.Controls.Add(_lblNg);
            tab.Controls.Add(gp); y += 120;

            Button btnReset = new Button { Text = "カウンターリセット", Location = new Point(10, y), Size = new Size(540, 30) };
            btnReset.Click += (s, e) => { _totalCount = _okCount = _ngCount = 0; UpdateCounterDisplay(); };
            tab.Controls.Add(btnReset); y += 45;

            Button btnDashboard = new Button
            {
                Text = "📊 稼働・品質分析ダッシュボードを開く",
                Location = new Point(10, y),
                Size = new Size(540, 45),
                BackColor = Color.LightSlateGray,
                ForeColor = Color.White,
                Font = new Font(this.Font.FontFamily, 11, FontStyle.Bold)
            };

            btnDashboard.Click += (s, e) => {
                FormDashboard dash = new FormDashboard(_analyzer);
                dash.ShowDialog(this);
            };
            tab.Controls.Add(btnDashboard); y += 55;

            Button btnTest = new Button { Text = "手動検査テスト", Location = new Point(10, y), Size = new Size(540, 35), BackColor = Color.LightSkyBlue, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            btnTest.Click += (s, e) => {
                _requestManualTest = true;
                _requestOkTest = false;
                _requestErrorTest = false;
            };
            tab.Controls.Add(btnTest); y += 40;

            _gbTestMode = new GroupBox { Text = "テストモード操作パネル (Test Mode)", Location = new Point(10, y), Size = new Size(540, 120), Enabled = false };

            _chkTestModeEnable = new CheckBox { Text = "テストモード有効化", Location = new Point(10, 25), AutoSize = true };
            _chkTestModeEnable.CheckedChanged += ChkTestModeEnable_CheckedChanged;

            _btnSelectTestFolder = new Button { Text = "フォルダ選択", Location = new Point(160, 20), Size = new Size(100, 30) };
            _btnSelectTestFolder.Click += BtnSelectTestFolder_Click;

            _lblTestImageInfo = new Label { Text = "画像: 0 / 0", Location = new Point(270, 25), AutoSize = true };

            _btnPrevImage = new Button { Text = "◀ 前の画像", Location = new Point(10, 60), Size = new Size(100, 40) };
            _btnPrevImage.Click += (s, e) => { LoadAndInspectTestImage(-1); };

            _btnNextImage = new Button { Text = "次の画像 ▶", Location = new Point(120, 60), Size = new Size(100, 40) };
            _btnNextImage.Click += (s, e) => { LoadAndInspectTestImage(1); };

            _chkAutoPlay = new CheckBox { Text = "自動送り", Location = new Point(240, 70), AutoSize = true };
            _chkAutoPlay.CheckedChanged += ChkAutoPlay_CheckedChanged;

            _gbTestMode.Controls.Add(_chkTestModeEnable);
            _gbTestMode.Controls.Add(_btnSelectTestFolder);
            _gbTestMode.Controls.Add(_lblTestImageInfo);
            _gbTestMode.Controls.Add(_btnPrevImage);
            _gbTestMode.Controls.Add(_btnNextImage);
            _gbTestMode.Controls.Add(_chkAutoPlay);

            tab.Controls.Add(_gbTestMode); y += 130;

            _autoPlayTimer = new System.Windows.Forms.Timer { Interval = 1500 }; // 1.5 seconds default
            _autoPlayTimer.Tick += (s, e) => { LoadAndInspectTestImage(1); };
        }

        private void UpdateUiForAdminMode()
        {
            if (_tabControl != null && _tabControl.TabPages.Count >= 5)
            {
                // Disable/Enable specific tabs or controls based on admin mode
                foreach (Control ctrl in _tabControl.TabPages[1].Controls) ctrl.Enabled = _isAdminMode;
                foreach (Control ctrl in _tabControl.TabPages[2].Controls) ctrl.Enabled = _isAdminMode;
                foreach (Control ctrl in _tabControl.TabPages[4].Controls) ctrl.Enabled = _isAdminMode;
            }
            if (_gbTestMode != null)
            {
                _gbTestMode.Enabled = _isAdminMode;
                // Automatically turn off test mode if leaving admin mode to be safe
                if (!_isAdminMode && _chkTestModeEnable.Checked)
                {
                    _chkTestModeEnable.Checked = false;
                }
            }
        }

        private void ChkTestModeEnable_CheckedChanged(object? sender, EventArgs e)
        {
            _isTestModeEnabled = _chkTestModeEnable.Checked;
            if (_isTestModeEnabled)
            {
                if (!Directory.Exists(_testImagesPath))
                {
                    Directory.CreateDirectory(_testImagesPath);
                }
                LoadTestImageFiles();
            }
            else
            {
                _chkAutoPlay.Checked = false; // Stop auto-play
            }
        }

        private void BtnSelectTestFolder_Click(object? sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = _testImagesPath;
                fbd.Description = "テスト用画像の入ったフォルダを選択してください";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _testImagesPath = fbd.SelectedPath;
                    LoadTestImageFiles();
                }
            }
        }

        private void LoadTestImageFiles()
        {
            if (Directory.Exists(_testImagesPath))
            {
                var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp" };
                _testImageFiles = Directory.GetFiles(_testImagesPath)
                    .Where(file => extensions.Contains(Path.GetExtension(file).ToLower()))
                    .OrderBy(f => f)
                    .ToList();

                _currentTestImageIndex = 0;
                UpdateTestImageInfoLabel();
            }
            else
            {
                _testImageFiles.Clear();
                _currentTestImageIndex = 0;
                UpdateTestImageInfoLabel();
            }
        }

        private void UpdateTestImageInfoLabel()
        {
            if (_testImageFiles.Count == 0)
            {
                _lblTestImageInfo.Text = "画像: 0 / 0";
            }
            else
            {
                string filename = Path.GetFileName(_testImageFiles[_currentTestImageIndex]);
                _lblTestImageInfo.Text = $"画像: {_currentTestImageIndex + 1} / {_testImageFiles.Count} ({filename})";
            }
        }

        private void ChkAutoPlay_CheckedChanged(object? sender, EventArgs e)
        {
            if (_chkAutoPlay.Checked && _isTestModeEnabled && _testImageFiles.Count > 0)
            {
                _autoPlayTimer.Start();
            }
            else
            {
                _autoPlayTimer.Stop();
            }
        }

        private void LoadAndInspectTestImage(int step)
        {
            if (!_isTestModeEnabled || _testImageFiles.Count == 0) return;

            _currentTestImageIndex += step;
            if (_currentTestImageIndex < 0) _currentTestImageIndex = _testImageFiles.Count - 1;
            if (_currentTestImageIndex >= _testImageFiles.Count) _currentTestImageIndex = 0;

            UpdateTestImageInfoLabel();

            string filePath = _testImageFiles[_currentTestImageIndex];
            try
            {
                using (var mat = Cv2.ImRead(filePath, ImreadModes.Color))
                {
                    if (!mat.Empty())
                    {
                        // Wrap the inspection call similarly to the camera capture event
                        Task.Run(() => InspectTestImageAsync(mat.Clone()));
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[TestMode] Error reading image {filePath}: {ex.Message}", true);
            }
        }

        private void InspectTestImageAsync(Mat frame)
        {
            try
            {
                DateTime startTime = DateTime.Now;

                // Actually run inspection
                var res = _inspectionEngine.Inspect(frame);

                _lastInspectionResult?.Dispose();
                _lastInspectionResult = res;

                // Save image (0: Do not save, 1: Save NG only, 2: Save all)
                if (_saveMode == 2 || (_saveMode == 1 && !res.IsOk))
                {
                    SaveInspectionImage(res);
                }

                // Logging
                double lastAngle = 0;
                if (res.Measurements.ContainsKey("OuterAngleDeg")) lastAngle = res.Measurements["OuterAngleDeg"];
                else if (res.Measurements.ContainsKey("HoleAngleDeg")) lastAngle = res.Measurements["HoleAngleDeg"];

                _analyzer.AddRecord(lastAngle, 0, res.IsOk, _logDirPath); // Emulate logging and analysis

                // UI update
                double elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
                SafeInvoke(() => {
                    // UpdateResultDisplay handles counter increment, UI text update, and Daily CSV Log automatically
                    UpdateResultDisplay(res, false, 0);

                    AppendLog($"[TestMode] Inspect {elapsedMs:F1}ms - OK={res.IsOk}", !res.IsOk);

                    if (res.OutputImage != null && !res.OutputImage.Empty())
                    {
                        UpdateUIDisplay(res.OutputImage, 0, false);
                    }
                    else
                    {
                        UpdateUIDisplay(frame, 0, false);
                    }
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[TestMode] Inspection Error: {ex.Message}", true);
            }
            finally
            {
                frame.Dispose();
            }
        }

                private void InitializeSettingsTab(TabPage tab)
        {
            int y = 20, lw = 150, cw = 100, lh = 28;
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0, decimal step = 1M, Control? parent = null, int lx = 10)
            { if (parent == null) parent = tab; parent.Controls.Add(new Label { Text = txt, Location = new Point(lx, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(lx + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = step };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI(); parent.Controls.Add(n); y += lh;
            }

            GroupBox grpCameraSettings = new GroupBox { Text = "カメラ・システム基本設定", Location = new Point(10, 10), Size = new Size(540, 150) };
            tab.Controls.Add(grpCameraSettings);

            y = 20;
            ComboBox cmbAppMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw + 50, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbAppMode.Items.AddRange(new object[] { "Visual (カメラ輝度自動)", "Plc (ネットワーク指令)" });
            cmbAppMode.SelectedIndex = _appSettings.TriggerMode == "Visual" ? 0 : 1;
            cmbAppMode.SelectedIndexChanged += (s, e) => { _appSettings.TriggerMode = cmbAppMode.SelectedIndex == 0 ? "Visual" : "Plc"; _appSettings.Save(); };
            grpCameraSettings.Controls.Add(new Label { Text = "検査トリガー元:", Location = new Point(10, y + 2), Size = new Size(lw, 20) });
            grpCameraSettings.Controls.Add(cmbAppMode);
            y += lh;

            _cmbSaveMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw + 50, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSaveMode.Items.AddRange(new object[] { "0: 保存しない", "1: NGのみ保存", "2: 全て保存" });
            _cmbSaveMode.SelectedIndex = _saveMode;
            _cmbSaveMode.SelectedIndexChanged += (s, e) => { if (!_isLoadingConfig) _saveMode = _cmbSaveMode.SelectedIndex; };
            AddN("カメラ露光時間:", ref _nudExposureTime, 100, 100000, (decimal)_appSettings.Cam.ExposureTime, 0, 100M, grpCameraSettings);
            grpCameraSettings.Controls.Add(new Label { Text = "画像保存モード:", Location = new Point(10, y + 2), Size = new Size(lw, 20) });
            grpCameraSettings.Controls.Add(_cmbSaveMode);
            y += lh;

            AddN("ログ保存期間(日) ※0で無期限:", ref _nudLogKeepDays, 0, 3650, _logKeepDays, 0, 1M, grpCameraSettings);

            grpCameraSettings.Controls.Add(new Label { Text = "セキュリティ:", Location = new Point(10, y + 2), Size = new Size(lw, 20) });
            Button btnChangePassword = new Button { Text = "管理者パスワードの変更...", Location = new Point(10 + lw, y), Size = new Size(cw + 50, 25), BackColor = Color.LightGray };
            btnChangePassword.Click += BtnChangePassword_Click;
            grpCameraSettings.Controls.Add(btnChangePassword);
            y += lh;

            GroupBox grpTriggerSettings = new GroupBox { Text = "トリガー・リトライ・ポカヨケ設定", Location = new Point(10, 170), Size = new Size(540, 280) };
            tab.Controls.Add(grpTriggerSettings);

            y = 20;
            _cmbTriggerMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTriggerMode.Items.AddRange(new object[] { "明転 (>)", "暗転 (<)" });
            _cmbTriggerMode.SelectedIndex = _triggerOnBright ? 0 : 1;
            _cmbTriggerMode.SelectedIndexChanged += (s, e) => { if (!_isLoadingConfig) _triggerOnBright = _cmbTriggerMode.SelectedIndex == 0; };
            grpTriggerSettings.Controls.Add(new Label { Text = "Visual Trigger:", Location = new Point(10, y + 2), Size = new Size(lw, 20) });
            grpTriggerSettings.Controls.Add(_cmbTriggerMode);
            y += lh;

            AddN("Trigger Thresh:", ref _nudTriggerThreshold, 0, 255, (decimal)_triggerThreshold, 1, 0.5M, grpTriggerSettings);
            AddN("Reset Thresh:", ref _nudResetThreshold, 0, 255, (decimal)_resetThreshold, 1, 0.5M, grpTriggerSettings);
            AddN("Stability (ms):", ref _nudStabilityDuration, 0, 5000, _stabilityDurationMs, 0, 1M, grpTriggerSettings);
            AddN("PLC Delay(待機) ms:", ref _nudPlcDelayMs, 0, 5000, _plcDelayMs, 0, 1M, grpTriggerSettings);

            AddN("最大リトライ回数:", ref _nudRetryCount, 0, 10, _maxRetryCount, 0, 1M, grpTriggerSettings);
            AddN("リトライ間隔(ms):", ref _nudRetryDelayMs, 0, 5000, _retryDelayMs, 0, 1M, grpTriggerSettings);
            AddN("自動起動トリガー回数:", ref _nudAutoStartCount, 0, 100, _autoStartCount, 0, 1M, grpTriggerSettings);
            grpTriggerSettings.Controls.Add(new Label { Text = "※0で無効。指定回数トリガーが来たら自動で運転開始します", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Gray }); y += 20;

            GroupBox grpAdvanced = new GroupBox { Text = "高度な設定 (ROI)", Location = new Point(10, 460), Size = new Size(540, 160) };
            tab.Controls.Add(grpAdvanced);
            y = 20;

            AddN("輝度 ROI X:", ref _nudRoiX, 0, 3000, _roi.X, 0, 1M, grpAdvanced);
            AddN("輝度 ROI Y:", ref _nudRoiY, 0, 3000, _roi.Y, 0, 1M, grpAdvanced);
            AddN("輝度 ROI W:", ref _nudRoiW, 1, 3000, _roi.Width, 0, 1M, grpAdvanced);
            AddN("輝度 ROI H:", ref _nudRoiH, 1, 3000, _roi.Height, 0, 1M, grpAdvanced);

            y = 20; int lx2 = 280;
            AddN("Save ROI X:", ref _nudSaveRoiX, 0, 3000, _saveRoi.X, 0, 1M, grpAdvanced, lx2);
            AddN("Save ROI Y:", ref _nudSaveRoiY, 0, 3000, _saveRoi.Y, 0, 1M, grpAdvanced, lx2);
            AddN("Save ROI W:", ref _nudSaveRoiW, 1, 3000, _saveRoi.Width, 0, 1M, grpAdvanced, lx2);
            AddN("Save ROI H:", ref _nudSaveRoiH, 1, 3000, _saveRoi.Height, 0, 1M, grpAdvanced, lx2);

            GroupBox grpMaintenance = new GroupBox { Text = "デバッグ / メンテナンス", Location = new Point(10, 630), Size = new Size(540, 120) };
            tab.Controls.Add(grpMaintenance);
            y = 20;

            Button btnTestOk = new Button { Text = "強制OKテスト", Location = new Point(10, y), Size = new Size(250, 35), BackColor = Color.LightGreen, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            btnTestOk.Click += (s, e) => {
                _requestOkTest = true;
                _requestErrorTest = false;
            };
            grpMaintenance.Controls.Add(btnTestOk);

            Button btnTestNg = new Button { Text = "強制NGテスト", Location = new Point(270, y), Size = new Size(250, 35), BackColor = Color.Orange, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            btnTestNg.Click += (s, e) => {
                _requestErrorTest = true;
                _requestOkTest = false;
            };
            grpMaintenance.Controls.Add(btnTestNg);
            y += 45;

            Button btnSave = new Button { Text = "設定を保存する (Save)", Location = new Point(10, y), Size = new Size(510, 40), BackColor = Color.LightGreen };
            btnSave.Click += (s, e) => { SaveConfig(); MessageBox.Show("保存しました。"); };
            grpMaintenance.Controls.Add(btnSave);

            y = 760;

            if (_txtLog != null) {
                _txtLog.Location = new Point(10, y);
                _txtLog.Size = new Size(540, 250);
                tab.Controls.Add(_txtLog);
                y += 260;
            }
        }

                private void InitializeInspectionTab(TabPage tab)
        {
            int y = 20;
            var m = _measurement;
            int lw = 160, cw = 100, lh = 28;
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0, decimal step = 1M, Control? parent = null)
            { if (parent == null) parent = tab; parent.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = step };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI(); parent.Controls.Add(n); y += lh;
            }
            void AddRect(string txt, ref NumericUpDown nx, ref NumericUpDown ny, ref NumericUpDown nw, ref NumericUpDown nh, CvRect r, Control? parent = null)
            { if (parent == null) parent = tab; parent.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(110, 20) });
                int sx = 120, step = 65, boxW = 60;
                nx = new NumericUpDown { Location = new Point(sx, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.X };
                ny = new NumericUpDown { Location = new Point(sx + step, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.Y };
                nw = new NumericUpDown { Location = new Point(sx + step * 2, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Width };
                nh = new NumericUpDown { Location = new Point(sx + step * 3, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Height };
                nx.ValueChanged += (s, e) => UpdateSettingsFromUI(); ny.ValueChanged += (s, e) => UpdateSettingsFromUI();
                nw.ValueChanged += (s, e) => UpdateSettingsFromUI(); nh.ValueChanged += (s, e) => UpdateSettingsFromUI();
                parent.Controls.Add(nx); parent.Controls.Add(ny); parent.Controls.Add(nw); parent.Controls.Add(nh); y += lh;
            }

            GroupBox grpInspectionSettings = new GroupBox { Text = "検査パラメータ設定", Location = new Point(10, 10), Size = new Size(540, 750) };
            tab.Controls.Add(grpInspectionSettings);

            y = 20;
            grpInspectionSettings.Controls.Add(new Label { Text = "--- 検査モード 選択 (複数ONで並列・OR判定) ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Magenta, Font = new Font(this.Font, FontStyle.Bold) }); y += 22;
            _chkEnableJigCheck = new CheckBox { Text = "エッジ間距離測定を有効にする", Location = new Point(10, y), AutoSize = true, Checked = m.EnableJigCheck };
            _chkEnableJigCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); grpInspectionSettings.Controls.Add(_chkEnableJigCheck); y += 22;
            _chkEnableOuterTiltCheck = new CheckBox { Text = "【モードA】 外形エッジで製品の傾き・ズレを検査する", Location = new Point(10, y), AutoSize = true, Checked = m.EnableOuterTiltCheck, ForeColor = Color.Teal };
            _chkEnableOuterTiltCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); grpInspectionSettings.Controls.Add(_chkEnableOuterTiltCheck); y += 22;
            _chkEnableHoleCheck = new CheckBox { Text = "【モードB】 穴で製品の傾き・ズレを検査する", Location = new Point(10, y), AutoSize = true, Checked = m.EnableHoleCheck, ForeColor = Color.Blue };
            _chkEnableHoleCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); grpInspectionSettings.Controls.Add(_chkEnableHoleCheck); y += 28;

            grpInspectionSettings.Controls.Add(new Label { Text = "--- 【モードA】 外形エッジ パラメータ ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Teal }); y += 22;
            AddRect("左エッジROI(青):", ref _nudTiltLX, ref _nudTiltLY, ref _nudTiltLW, ref _nudTiltLH, m.TiltLeftRoi, grpInspectionSettings);
            AddRect("右エッジROI(青):", ref _nudTiltRX, ref _nudTiltRY, ref _nudTiltRW, ref _nudTiltRH, m.TiltRightRoi, grpInspectionSettings);
            AddN("目標 Xずれ(mm):", ref _nudOuterTargetX, -100, 100, (decimal)m.TargetOuterXOffsetMm, 2, 0.1M, grpInspectionSettings);
            AddN("Xずれ許容(mm):", ref _nudOuterOffsetX, 0, 50, (decimal)m.OuterOffsetToleranceMm, 2, 0.1M, grpInspectionSettings);
            AddN("目標 Θ(deg):", ref _nudOuterTargetA, -180, 180, (decimal)m.TargetOuterAngleDeg, 2, 0.1M, grpInspectionSettings);
            AddN("Θ許容(deg):", ref _nudOuterOffsetA, 0, 90, (decimal)m.OuterAngleToleranceDeg, 2, 0.1M, grpInspectionSettings); y += 10;

            grpInspectionSettings.Controls.Add(new Label { Text = "--- 【モードB】 穴 パラメータ ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddRect("基準穴 ROI:", ref _nudHolesX, ref _nudHolesY, ref _nudHolesW, ref _nudHolesH, m.HolesRoi, grpInspectionSettings);
            AddN("穴 最小面積:", ref _nudMinHoleArea, 0, 100000, m.MinHoleArea, 0, 1M, grpInspectionSettings);
            AddN("穴 最大面積:", ref _nudMaxHoleArea, 0, 1000000, m.MaxHoleArea, 0, 1M, grpInspectionSettings);
            AddN("真円度ししきい値:", ref _nudMinCircularity, 0, 1, (decimal)m.MinCircularity, 2, 0.05M, grpInspectionSettings);
            AddN("目標 Xずれ(mm):", ref _nudTargetXOffset, -100, 100, (decimal)m.TargetXOffsetMm, 2, 0.1M, grpInspectionSettings);
            AddN("Xずれ許容(mm):", ref _nudOffsetTolerance, 0, 50, (decimal)m.OffsetToleranceMm, 2, 0.1M, grpInspectionSettings);
            AddN("目標 Θ(deg):", ref _nudTargetAngle, -180, 180, (decimal)m.TargetAngleDeg, 2, 0.1M, grpInspectionSettings);
            AddN("Θ許容(deg):", ref _nudAngleTolerance, 0, 90, (decimal)m.AngleToleranceDeg, 2, 0.1M, grpInspectionSettings); y += 10;

            grpInspectionSettings.Controls.Add(new Label { Text = "--- エッジ間距離 測定設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Olive }); y += 22;
            AddRect("左エッジ ROI:", ref _nudJigLX, ref _nudJigLY, ref _nudJigLW, ref _nudJigLH, m.JigLeftRoi, grpInspectionSettings);
            AddRect("右エッジ ROI:", ref _nudJigRX, ref _nudJigRY, ref _nudJigRW, ref _nudJigRH, m.JigRightRoi, grpInspectionSettings);
            AddN("エッジ目標距離(mm):", ref _nudJigTarget, 0, 500, (decimal)m.TargetJigDistanceMm, 2, 0.1M, grpInspectionSettings);
            AddN("エッジ許容誤差(mm):", ref _nudJigTolerance, 0, 50, (decimal)m.JigToleranceMm, 2, 0.1M, grpInspectionSettings); y += 10;

            grpInspectionSettings.Controls.Add(new Label { Text = "--- 下部測定 ROI ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkGoldenrod }); y += 22;
            AddRect("Btm線 ROI(黄):", ref _nudBtmRoiX, ref _nudBtmRoiY, ref _nudBtmRoiW, ref _nudBtmRoiH, m.BtmMeasureRoi, grpInspectionSettings);
            AddRect("Btm内側左 ROI(赤):", ref _nudBtmInnerLX, ref _nudBtmInnerLY, ref _nudBtmInnerLW, ref _nudBtmInnerLH, m.BtmInnerLeftRoi, grpInspectionSettings);
            AddRect("Btm内側右 ROI(赤):", ref _nudBtmInnerRX, ref _nudBtmInnerRY, ref _nudBtmInnerRW, ref _nudBtmInnerRH, m.BtmInnerRightRoi, grpInspectionSettings); y += 10;

            GroupBox grpCalibration = new GroupBox { Text = "キャリブレーション (Pixel/mm比率)", Location = new Point(10, 770), Size = new Size(540, 150) };
            tab.Controls.Add(grpCalibration);
            y = 20;

            _lblCurrentHoleDistPx = new Label { Text = "現在の穴/エッジ間距離: 0.0 px", Location = new Point(10, y), Size = new Size(300, 20), ForeColor = Color.DarkOrange, Font = new Font(this.Font, FontStyle.Bold) };
            grpCalibration.Controls.Add(_lblCurrentHoleDistPx); y += lh;
            _nudActualWidthMm = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0.1M, Maximum = 500, Value = 50, DecimalPlaces = 2, Increment = 0.1M };
            grpCalibration.Controls.Add(new Label { Text = "実測の距離(mm):", Location = new Point(10, y + 2), Size = new Size(lw, 20) }); grpCalibration.Controls.Add(_nudActualWidthMm); y += lh;
            _btnCalcRatio = new Button { Text = "比率を自動計算", Location = new Point(10, y), Size = new Size(360, 30), BackColor = Color.LightYellow };
            _btnCalcRatio.Click += (s, e) => {
                if (m.LastHoleDistancePx <= 0) { MessageBox.Show("先にテスト実行して検出させてください。"); return; }
                _nudPixelToMm.Value = _nudActualWidthMm.Value / (decimal)m.LastHoleDistancePx;
                MessageBox.Show("更新しました。各種目標(mm)を再設定してください。");
            }; grpCalibration.Controls.Add(_btnCalcRatio); y += 45;
            AddN("Pixel->mm比率:", ref _nudPixelToMm, 0.0001M, 1, (decimal)m.PixelToMmRatio, 5, 0.001M, grpCalibration);
        }

        private void InitializeDebugTab(TabPage tab)
        {
            int y = 10;
            var m = _measurement;
            int lw = 150, cw = 100, lh = 28;
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0, decimal step = 1M)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = step };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(n); y += lh;
            }

            _pictureBoxDebug = new PictureBox { Location = new Point(10, y), Size = new Size(380, 280), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(_pictureBoxDebug); y += 300;

            tab.Controls.Add(new Label { Text = "--- ★外形エッジ(青枠) 専用閾値調整 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Teal }); y += 22;
            AddN("左エッジ(青) 閾値:", ref _nudThreshOuterL, 0, 255, m.ThreshOuterL, 0, 1M);
            AddN("右エッジ(青) 閾値:", ref _nudThreshOuterR, 0, 255, m.ThreshOuterR, 0, 1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- ★BTM内側(赤枠) 専用閾値調整 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkRed }); y += 22;
            AddN("左内側(赤) 閾値:", ref _nudThreshBtmInnerL, 0, 255, m.ThreshBtmInnerL, 0, 1M);
            AddN("右内側(赤) 閾値:", ref _nudThreshBtmInnerR, 0, 255, m.ThreshBtmInnerR, 0, 1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- ★4分割 二値化 閾値調整 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddN("上下分割 Y境界線:", ref _nudSplitY, 0, 3000, m.SplitBoundaryY, 0, 1M);
            AddN("左右分割 X境界線:", ref _nudSplitX, 0, 3000, m.SplitBoundaryX, 0, 1M);
            AddN("左上 (TL) 閾値:", ref _nudThreshTL, 0, 255, m.ThreshTopLeft, 0, 1M);
            AddN("右上 (TR) 閾値:", ref _nudThreshTR, 0, 255, m.ThreshTopRight, 0, 1M);
            AddN("左下 (BL) 閾値:", ref _nudThreshBL, 0, 255, m.ThreshBtmLeft, 0, 1M);
            AddN("右下 (BR) 閾値:", ref _nudThreshBR, 0, 255, m.ThreshBtmRight, 0, 1M);
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            _isUiLoaded = true;
            if (_camera.Initialize(0)) _camera.StartCapture();
            _ = MonitorPlcTriggerAsync();
            Task.Run(() => DeleteOldLogs());
            RestoreDailyCounter();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _isMonitoring = false; _isUiLoaded = false;
            _camera.StopCapture(); _camera.Dispose();
            _plc.Disconnect(); SaveConfig();
        }
        private void AppendDailyLog(InspectionResult result)
        {
            try
            {
                string dir = Path.Combine(_logDirPath, DateTime.Now.ToString("yyyyMMdd"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string logFile = Path.Combine(dir, "InspectionLog.csv");
                bool isNewFile = !File.Exists(logFile);

                using (StreamWriter sw = new StreamWriter(logFile, true, System.Text.Encoding.UTF8))
                {
                    if (isNewFile)
                    {
                        var measurementKeys = result.Measurements.Keys.ToList();
                        string header = "日時,総合判定,アライメント判定,不合格理由(結合文字列)," + string.Join(",", measurementKeys);
                        sw.WriteLine(header);
                    }

                    string resStr = result.IsOk ? "OK" : "NG";
                    string alignStr = result.AlignmentStatus == 1 ? "OK" : (result.AlignmentStatus == 2 ? "NG(許容外)" : "検出エラー");
                    string reasons = result.FailureReasons.Count > 0 ? "\"" + string.Join(" | ", result.FailureReasons).Replace("\"", "\"\"") + "\"" : "";

                    var measurementValues = result.Measurements.Values.Select(v => v.ToString("F3")).ToList();

                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{resStr},{alignStr},{reasons}," + string.Join(",", measurementValues);
                    sw.WriteLine(line);
                }
            }
            catch { }
        }

        private void RestoreDailyCounter()
        {
            try
            {
                string logFile = Path.Combine(_logDirPath, DateTime.Now.ToString("yyyyMMdd"), "InspectionLog.csv");
                if (File.Exists(logFile))
                {
                    var lines = File.ReadAllLines(logFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                    _totalCount = 0;
                    _okCount = 0;
                    _ngCount = 0;
                    for (int i = 1; i < lines.Count; i++) // Skip header
                    {
                        var cols = lines[i].Split(',');
                        if (cols.Length > 1)
                        {
                            _totalCount++;
                            if (cols[1].Trim() == "OK") _okCount++;
                            else _ngCount++;
                        }
                    }
                }
                SafeInvoke(() => UpdateCounterDisplay());
            }
            catch { }
        }
        private void DeleteOldLogs() { if (_logKeepDays <= 0) return; try { if (!Directory.Exists(_logDirPath)) return; DateTime thresholdDate = DateTime.Now.Date.AddDays(-_logKeepDays); var dirs = Directory.GetDirectories(_logDirPath); foreach (var dir in dirs) { string dirName = new DirectoryInfo(dir).Name; if (DateTime.TryParseExact(dirName, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime dirDate)) { if (dirDate.Date < thresholdDate) Directory.Delete(dir, true); } } } catch { } }

        private async Task MonitorPlcTriggerAsync()
        {
            _isMonitoring = true;
            AppendLog("PLC接続・監視ループを開始しました。");
            while (_isMonitoring && !this.IsDisposed)
            {
                if (!_plc.IsConnected)
                {
                    await Task.Run(() => _plc.Connect());
                }

                if (_plc.IsConnected)
                {
                    if (_appSettings.TriggerMode == "Plc")
                    {
                        int t = await Task.Run(() => _plc.ReadDevice(_appSettings.Cam.ReadDeviceAddress));
                        if (t == 1)
                        {
                            if (!_plcTriggerReceived) AppendLog($"M{_appSettings.Cam.ReadDeviceAddress} より検査トリガを受信しました");
                            _plcTriggerReceived = true;
                        }
                    }
                }
                else
                {
                    await Task.Delay(1000);
                }

                int delayMs = _appSettings.InspectionIntervalMs;
                if (delayMs <= 0) delayMs = 10;
                await Task.Delay(delayMs);
            }
            AppendLog("PLC接続・監視ループを停止しました。");
        }

        private void AppendLog(string msg, bool isError = false)
        {
            SafeInvoke(() => {
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string type = isError ? "[ERROR]" : "[INFO]";
                string logLine = $"[{time}] {type} {msg}{Environment.NewLine}";

                if (_txtLog != null)
                {
                    if (_txtLog.TextLength > 50000)
                    {
                        _txtLog.Text = _txtLog.Text.Substring(30000);
                    }
                    _txtLog.AppendText(logLine);
                }

                try
                {
                    string dir = Path.Combine(_logDirPath, DateTime.Now.ToString("yyyyMMdd"));
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string logFile = Path.Combine(dir, "PlcCommunicationLog.txt");
                    File.AppendAllText(logFile, logLine);
                }
                catch { }
            });
        }

        private void SafeInvoke(Action action) { if (!_isUiLoaded || this.IsDisposed || !this.IsHandleCreated || this.Disposing) return; try { this.Invoke(new MethodInvoker(action)); } catch { } }
        private void SafeBeginInvoke(Action action) { if (!_isUiLoaded || this.IsDisposed || !this.IsHandleCreated || this.Disposing) return; try { this.BeginInvoke(new MethodInvoker(action)); } catch { } }

        private void Camera_OnFrameCaptured(object? sender, Mat frame)
        {
            if (frame == null || frame.Empty()) return;
            if (!_isUiLoaded || this.IsDisposed || this.Disposing || _isTestModeEnabled)
            {
                frame.Dispose();
                return;
            }

            _camFrameCount++;
            if ((DateTime.Now - _lastFpsTime).TotalMilliseconds >= 1000) {
                _currentFpsText = $"Cam FPS: Cam:{_camFrameCount} / Proc:{_procFrameCount} / UI:{_uiFrameCount}";
                _camFrameCount = 0; _procFrameCount = 0; _uiFrameCount = 0;
                _lastFpsTime = DateTime.Now;
            }

            double limitMs = 33.0;
            if (!_isRunning && _autoStartCount == 0) limitMs = 200.0;
            else if (_appSettings.TriggerMode == "Plc" && !_isRunning) limitMs = 200.0;

            bool hasForceAction = _plcTriggerReceived || _requestManualTest || _requestErrorTest || _requestOkTest || _pendingSaveResult != -1;
            if (!hasForceAction && (DateTime.Now - _lastFrameProcessTime).TotalMilliseconds < limitMs)
            {
                frame.Dispose();
                return;
            }
            if (_isProcessing)
            {
                frame.Dispose();
                return;
            }
            _isProcessing = true;
            _lastFrameProcessTime = DateTime.Now;

            Task.Run(() => {
                bool frameDisposed = false;
                try
                {
                    _procFrameCount++;
                    bool isDebug = _isDebugTabActive;
                    double b = 0;
                    if (_appSettings.TriggerMode == "Visual" || _isRunning || _autoStartCount > 0)
                        b = _measurement.CalculateBrightness(frame, _roi);

                    if (isDebug) _measurement.UpdateDebugImageRealtime(frame, _saveRoi);
                    bool forceUiUpdate = false;

                    if (_requestManualTest) {
                        _requestManualTest = false;
                        _measurement.DebugRoi = _saveRoi;
                        _measurement.IsDebugMode = isDebug;
                        InspectionResult manualResult = _inspectionEngine.Inspect(frame);
                        SafeInvoke(() => UpdateResultDisplay(manualResult, true, b));

                        _lastInspectionResult?.Dispose();
                        _lastInspectionResult = manualResult;
                        _pendingSaveResult = manualResult.IsOk ? 1 : 2;
                        forceUiUpdate = true;
                    }
                    if (_requestOkTest)
                    {
                        _requestOkTest = false;
                        int forceOkResult = 1;
                        AppendLog("[TEST] 強制OKテスト要求を受信。");
                        var camSettings = _appSettings.Cam;
                        _plc.SendResult(true, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                        if (_appSettings.TriggerMode == "Plc")
                        {
                            Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                        }

                        _lastInspectionResult?.Dispose();
                        var dummyResultOk = new InspectionResult { IsOk = true, ResultText = "OK" };
                        dummyResultOk.OutputImage = frame.Clone();
                        _lastInspectionResult = dummyResultOk;

                        SafeInvoke(() => UpdateResultDisplay(dummyResultOk, true, b));
                        _pendingSaveResult = forceOkResult;
                        forceUiUpdate = true;
                    }

                    if (_requestErrorTest)
                    {
                        _requestErrorTest = false;
                        int forceNgResult = 2;
                        AppendLog("[TEST] 強制NGテスト要求を受信。");
                        var camSettings = _appSettings.Cam;
                        _plc.SendResult(false, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                        if (_appSettings.TriggerMode == "Plc")
                        {
                            Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                        }

                        _lastInspectionResult?.Dispose();
                        var dummyResultNg = new InspectionResult { IsOk = false, ResultText = "ERR" };
                        dummyResultNg.OutputImage = frame.Clone();
                        _lastInspectionResult = dummyResultNg;

                        SafeInvoke(() => UpdateResultDisplay(dummyResultNg, true, b));
                        _pendingSaveResult = forceNgResult;
                        forceUiUpdate = true;
                    }

                    UpdateStateMachine(frame, b, isDebug);
                    if (_pendingSaveResult != -1) forceUiUpdate = true;
                    double uiLimitMs = _isRunning ? 66.0 : 200.0;

                    if ((forceUiUpdate || (DateTime.Now - _lastUiUpdateTime).TotalMilliseconds > uiLimitMs) && _isUiLoaded && !this.IsDisposed && !this.Disposing && this.IsHandleCreated) {
                        _lastUiUpdateTime = DateTime.Now;
                        frameDisposed = true;
                        SafeBeginInvoke(() => {
                            try {
                                _uiFrameCount++;
                                UpdateUIDisplay(frame, b, isDebug);
                            }
                            finally {
                                if (frame != null && !frame.IsDisposed) frame.Dispose();
                                _isProcessing = false;
                            }
                        });
                    } else {
                        frame.Dispose();
                        frameDisposed = true;
                        _isProcessing = false;
                    }
                }
                catch
                {
                    if (!frameDisposed && frame != null && !frame.IsDisposed) frame.Dispose();
                    _isProcessing = false;
                }
            });
        }

        private void UpdateStateMachine(Mat frame, double b, bool isDebug)
        {
            bool rawTriggered = (_appSettings.TriggerMode == "Plc" ? _plcTriggerReceived : (_triggerOnBright ? (b > _triggerThreshold) : (b < _triggerThreshold)));
            bool isReset = (_appSettings.TriggerMode == "Plc" ? false : (_triggerOnBright ? (b < _resetThreshold) : (b > _resetThreshold)));
            bool isTriggerEdge = rawTriggered && !_wasTriggeredLastFrame;
            _wasTriggeredLastFrame = rawTriggered;

            var camSettings = _appSettings.Cam;

            if (!_isRunning)
            {
                if (isTriggerEdge)
                {
                    if (_autoStartCount > 0)
                    {
                        _missedTriggerCount++;
                        if (_missedTriggerCount >= _autoStartCount) { _isRunning = true; _missedTriggerCount = 0; SafeInvoke(() => { _btnRunToggle.Text = "■ 運転停止 (STOP)"; _btnRunToggle.BackColor = Color.Salmon; }); }
                        else {
                            SafeInvoke(() => lblStateUpdate($"STARTING SOON... ({_missedTriggerCount}/{_autoStartCount})", Color.Orange));
                            if (_appSettings.TriggerMode == "Plc") {
                                _plcTriggerReceived = false;
                                _plc.SendResult(true, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                                Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                            }
                            return;
                        }
                    }
                    else {
                        if (_appSettings.TriggerMode == "Plc") {
                            _plcTriggerReceived = false;
                            _plc.SendResult(true, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                            Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                        }
                        return;
                    }
                }
                else return;
            }

            switch (_currentState)
            {
                case STATE_WAITING:
                    if (rawTriggered)
                    {
                        _currentRetry = 0;
                        _measurement.EnableOuterTiltCheck = _configEnableOuterTilt;
                        _measurement.EnableHoleCheck = _configEnableHole;

                        AppendLog("検査開始のため前回の判定出力をクリア");
                        Task.Run(() => {
                            _plc.WriteDevice(camSettings.OkDeviceAddress, 0);
                            _plc.WriteDevice(camSettings.NgDeviceAddress, 0);
                        });

                        if (_appSettings.TriggerMode == "Plc") {
                            _plcTriggerReceived = false; _currentState = STATE_STABILIZING; _stabilityStartTime = DateTime.Now;
                            SafeInvoke(() => lblStateUpdate("DELAYING...", Color.Yellow));
                        }
                        else {
                            if (isTriggerEdge) { _currentState = STATE_STABILIZING; _stabilityStartTime = DateTime.Now; SafeInvoke(() => lblStateUpdate("TESTING...", Color.Yellow)); }
                        }
                    }
                    break;

                case STATE_STABILIZING:
                    if (_appSettings.TriggerMode == "Plc")
                    {
                        double targetDelay = _currentRetry == 0 ? _plcDelayMs : _retryDelayMs;
                        if ((DateTime.Now - _stabilityStartTime).TotalMilliseconds > targetDelay)
                        {
                            _measurement.DebugRoi = _saveRoi;
                            _measurement.IsDebugMode = isDebug;
                            InspectionResult inspectResult = _inspectionEngine.Inspect(frame);

                            if (!inspectResult.IsOk && _currentRetry >= _maxRetryCount && _measurement.EnableOuterTiltCheck && !_measurement.EnableHoleCheck)
                            {
                                _measurement.EnableOuterTiltCheck = false; _measurement.EnableHoleCheck = true;
                                inspectResult.Dispose();
                                inspectResult = _inspectionEngine.Inspect(frame);
                                SafeInvoke(() => lblStateUpdate("FALLBACK HOLE...", Color.Orange));
                            }

                            if (inspectResult.IsOk || _currentRetry >= _maxRetryCount)
                            {
                                ProcessInspectionResult(inspectResult, b);
                                _currentState = STATE_COOLING;
                                _cooldownStartTime = DateTime.Now;
                            }
                            else
                            {
                                inspectResult.Dispose();
                                _currentRetry++;
                                _stabilityStartTime = DateTime.Now;
                                SafeInvoke(() => lblStateUpdate($"RETRY {_currentRetry}/{_maxRetryCount}", Color.Orange));
                            }
                        }
                    }
                    else
                    {
                        if (isReset) { _currentState = STATE_WAITING; SafeInvoke(() => lblStateUpdate("READY", Color.LightGray)); }
                        else
                        {
                            double targetDelay = _currentRetry == 0 ? _stabilityDurationMs : _retryDelayMs;
                            if ((DateTime.Now - _stabilityStartTime).TotalMilliseconds > targetDelay)
                            {
                                _measurement.DebugRoi = _saveRoi;
                                _measurement.IsDebugMode = isDebug;
                                InspectionResult inspectResult = _inspectionEngine.Inspect(frame);

                                if (!inspectResult.IsOk && _currentRetry >= _maxRetryCount && _measurement.EnableOuterTiltCheck && !_measurement.EnableHoleCheck)
                                {
                                    _measurement.EnableOuterTiltCheck = false; _measurement.EnableHoleCheck = true;
                                    inspectResult.Dispose();
                                    inspectResult = _inspectionEngine.Inspect(frame);
                                    SafeInvoke(() => lblStateUpdate("FALLBACK HOLE...", Color.Orange));
                                }

                                if (inspectResult.IsOk || _currentRetry >= _maxRetryCount)
                                {
                                    ProcessInspectionResult(inspectResult, b);
                                    _currentState = STATE_COOLING;
                                    _cooldownStartTime = DateTime.Now;
                                }
                                else
                                {
                                    inspectResult.Dispose();
                                    _currentRetry++;
                                    _stabilityStartTime = DateTime.Now;
                                    SafeInvoke(() => lblStateUpdate($"RETRY {_currentRetry}/{_maxRetryCount}", Color.Orange));
                                }
                            }
                        }
                    }
                    break;

                case STATE_COOLING:
                    if ((DateTime.Now - _cooldownStartTime).TotalMilliseconds > _cooldownDurationMs)
                        if (_appSettings.TriggerMode == "Plc" || isReset) { _currentState = STATE_WAITING; SafeInvoke(() => lblStateUpdate("READY", Color.LightGray)); }
                    break;
            }
        }

        private void lblStateUpdate(string text, Color color) {
            if (_lblBigResult != null && !_lblBigResult.IsDisposed) {
                _lblBigResult.Text = text;
                _lblBigResult.BackColor = color;
            }
            if (_lblAlignmentStatus != null && !_lblAlignmentStatus.IsDisposed) {
                _lblAlignmentStatus.Text = "アライメント: " + text;
                _lblAlignmentStatus.BackColor = color;
            }
        }

        private void ProcessInspectionResult(InspectionResult result, double brightness)
        {
            var camSettings = _appSettings.Cam;
            bool isOk = result.IsOk;
            AppendLog($"検査完了。結果: {(isOk ? "OK" : "NG")} (D{camSettings.WriteDeviceAddress} に送信)");
            _plc.SendResult(isOk, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);

            if (_appSettings.TriggerMode == "Plc")
            {
                AppendLog($"PLCトリガ M{camSettings.ReadDeviceAddress} をクリア");
                Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
            }

            if (!isOk && result.FailureReasons.Count > 0)
            {
                AppendLog("【不合格理由】: " + string.Join(" / ", result.FailureReasons));
            }

            SafeInvoke(() => UpdateResultDisplay(result, false, brightness));

            _lastInspectionResult?.Dispose();
            _lastInspectionResult = result;
            _pendingSaveResult = result.IsOk ? 1 : 2;

            // ★分析モジュールへの登録処理
            double lastAngle = 0;
            if (result.Measurements.ContainsKey("OuterAngleDeg")) lastAngle = result.Measurements["OuterAngleDeg"];
            else if (result.Measurements.ContainsKey("HoleAngleDeg")) lastAngle = result.Measurements["HoleAngleDeg"];

            _analyzer.AddRecord(lastAngle, brightness, isOk, _logDirPath);

            // フォールバックしていた場合に備えて本来の設定を復元
            _measurement.EnableOuterTiltCheck = _configEnableOuterTilt;
            _measurement.EnableHoleCheck = _configEnableHole;
        }

        private void UpdateUIDisplay(Mat frame, double b, bool isDebug)
        {
            if (!_isUiLoaded || this.IsDisposed) return;
            using (Mat disp = new Mat())
            {
                if (_currentState == STATE_WAITING && _lastInspectionResult == null)
                {
                    if (frame.Channels() == 1) Cv2.CvtColor(frame, disp, ColorConversionCodes.GRAY2BGR); else frame.CopyTo(disp);
                    if (_chkShowOverlay.Checked)
                    {
                        Cv2.Rectangle(disp, _roi, Scalar.Yellow, 2);
                        Cv2.Rectangle(disp, _measurement.BtmMeasureRoi, new Scalar(0, 150, 150), 2);

                        Cv2.Rectangle(disp, _measurement.BtmInnerLeftRoi, new Scalar(0, 0, 255), 2);
                        Cv2.Rectangle(disp, _measurement.BtmInnerRightRoi, new Scalar(0, 0, 255), 2);

                        if (_measurement.EnableOuterTiltCheck) { Cv2.Rectangle(disp, _measurement.TiltLeftRoi, Scalar.Cyan, 2); Cv2.Rectangle(disp, _measurement.TiltRightRoi, Scalar.Cyan, 2); }
                        if (_measurement.EnableHoleCheck) { Cv2.Rectangle(disp, _measurement.HolesRoi, Scalar.Orange, 2); }
                        if (_measurement.EnableJigCheck) { Cv2.Rectangle(disp, _measurement.JigLeftRoi, Scalar.Yellow, 2); Cv2.Rectangle(disp, _measurement.JigRightRoi, Scalar.Yellow, 2); }
                        Cv2.Rectangle(disp, _saveRoi, Scalar.LightSkyBlue, 1);
                        Cv2.Line(disp, new CvPoint(0, _measurement.SplitBoundaryY), new CvPoint(disp.Width, _measurement.SplitBoundaryY), Scalar.LightGray, 2);
                        Cv2.Line(disp, new CvPoint(_measurement.SplitBoundaryX, 0), new CvPoint(_measurement.SplitBoundaryX, disp.Height), Scalar.LightGray, 2);
                    }
                }
                else
                {
                    if (_lastInspectionResult != null && _lastInspectionResult.OutputImage != null && !_lastInspectionResult.OutputImage.Empty())
                    {
                        _lastInspectionResult.OutputImage.CopyTo(disp);
                    }
                    else
                    {
                        if (frame.Channels() == 1) Cv2.CvtColor(frame, disp, ColorConversionCodes.GRAY2BGR); else frame.CopyTo(disp);
                    }
                }

                if (_pendingSaveResult != -1 && _lastInspectionResult != null) {
                    if (_saveMode == 2 || (_saveMode == 1 && _pendingSaveResult != 1)) {
                        SaveInspectionImage(_lastInspectionResult);
                    }
                    _pendingSaveResult = -1;
                }
                Bitmap bmp = BitmapConverter.ToBitmap(disp); Image? old = _pictureBoxMain.Image; _pictureBoxMain.Image = bmp; old?.Dispose();
            }
            if (isDebug)
            {
                using (Mat binImg = new Mat())
                {
                    if (_lastInspectionResult != null && _lastInspectionResult.BinaryImage != null && !_lastInspectionResult.BinaryImage.Empty())
                    {
                        _lastInspectionResult.BinaryImage.CopyTo(binImg);
                    }
                    else
                    {
                        _measurement.GetDebugImage(binImg);
                    }
                    if (!binImg.Empty())
                    {
                        Bitmap bmpD = BitmapConverter.ToBitmap(binImg); Image? oldD = _pictureBoxDebug.Image; _pictureBoxDebug.Image = bmpD; oldD?.Dispose();
                    }
                }
            }
            if (_lblCurrentHoleDistPx != null && !_lblCurrentHoleDistPx.IsDisposed && _measurement.LastHoleDistancePx > 0) _lblCurrentHoleDistPx.Text = "現在の穴/エッジ間距離: " + _measurement.LastHoleDistancePx.ToString("F1") + " px";

            if (_lblStatus != null && !_lblStatus.IsDisposed) {
                if (!_isRunning) { _lblStatus.Text = "Status: STOPPED"; _lblStatus.ForeColor = Color.Red; }
                else {
                    _lblStatus.Text = "Status: " + (_currentState == 0 ? "WAITING" : (_currentState == 1 ? "STABILIZING" : "COOLING"));
                    _lblStatus.ForeColor = _currentState == 0 ? Color.Gray : (_currentState == 1 ? Color.Goldenrod : Color.LimeGreen);
                }
            }
            if (_lblBrightness != null && !_lblBrightness.IsDisposed) _lblBrightness.Text = "Brightness: " + b.ToString("F1");
            if (_lblFps != null && !_lblFps.IsDisposed) _lblFps.Text = _currentFpsText;
        }

        private void SaveInspectionImage(InspectionResult result)
        {
            if (result == null || result.OutputImage == null) return;

            try
            {
                Mat outputClone = result.OutputImage.Clone();
                Mat? binaryClone = result.BinaryImage?.Clone();

                Task.Run(() => {
                    try
                    {
                        string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SavedImages", DateTime.Now.ToString("yyyy-MM-dd"));
                        if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

                        string resStr = result.IsOk ? "OK" : "NG";
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

                        string outPath = Path.Combine(baseDir, $"{timestamp}_{resStr}_Result.png");

                        CvRect crop = _saveRoi & new CvRect(0, 0, outputClone.Width, outputClone.Height);
                        using (Mat cropped = new Mat(outputClone, crop))
                        {
                            var p = new ImageEncodingParam(ImwriteFlags.PngCompression, 3);
                            Cv2.ImWrite(outPath, cropped, p);
                        }

                        if (binaryClone != null && !binaryClone.Empty())
                        {
                            string binPath = Path.Combine(baseDir, $"{timestamp}_{resStr}_Binary.png");
                            CvRect binCrop = _saveRoi & new CvRect(0, 0, binaryClone.Width, binaryClone.Height);
                            using (Mat binCropped = new Mat(binaryClone, binCrop))
                            {
                                var p = new ImageEncodingParam(ImwriteFlags.PngCompression, 3);
                                Cv2.ImWrite(binPath, binCropped, p);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        outputClone?.Dispose();
                        binaryClone?.Dispose();
                    }
                });
            } catch { }
        }

        private void UpdateResultDisplay(InspectionResult result, bool manual, double brightness = 0.0)
        {
            if (_lblBigResult != null && !_lblBigResult.IsDisposed)
            {
                _lblBigResult.Text = result.IsOk ? "総合判定: OK" : "総合判定: NG";
                _lblBigResult.BackColor = result.IsOk ? Color.LimeGreen : Color.Red;
            }

            if (_lblAlignmentStatus != null && !_lblAlignmentStatus.IsDisposed)
            {
                string alignText;
                Color alignColor;

                if (result.AlignmentStatus == 1)
                {
                    alignText = "アライメント: OK";
                    alignColor = Color.LimeGreen;
                }
                else if (result.AlignmentStatus == 2)
                {
                    alignText = "アライメント: NG (許容外)";
                    alignColor = Color.Red;
                }
                else
                {
                    alignText = "アライメント: 検出エラー";
                    alignColor = Color.OrangeRed;
                }

                _lblAlignmentStatus.Text = alignText;
                _lblAlignmentStatus.BackColor = alignColor;
            }

            if (!manual)
            {
                _totalCount++;
                if (result.IsOk) _okCount++; else _ngCount++;
                UpdateCounterDisplay();
                AppendDailyLog(result);
            }
        }

        private void UpdateCounterDisplay() { if (_lblTotal == null || _lblTotal.IsDisposed) return; _lblTotal.Text = "総検査数 : " + _totalCount; _lblOk.Text = "良品 (OK): " + _okCount; _lblNg.Text = "不良 (NG): " + _ngCount; }

        private void CopySettingsToMeasurement(MeasurementCore m)
        {
            if (m == null) return;

            if (_chkEnableJigCheck != null) m.EnableJigCheck = _chkEnableJigCheck.Checked;
            if (_chkEnableOuterTiltCheck != null)
            {
                m.EnableOuterTiltCheck = _chkEnableOuterTiltCheck.Checked;
                _configEnableOuterTilt = _chkEnableOuterTiltCheck.Checked;
            }
            if (_chkEnableHoleCheck != null)
            {
                m.EnableHoleCheck = _chkEnableHoleCheck.Checked;
                _configEnableHole = _chkEnableHoleCheck.Checked;
            }

            m.TiltLeftRoi = new CvRect((int)_nudTiltLX.Value, (int)_nudTiltLY.Value, (int)_nudTiltLW.Value, (int)_nudTiltLH.Value);
            m.TiltRightRoi = new CvRect((int)_nudTiltRX.Value, (int)_nudTiltRY.Value, (int)_nudTiltRW.Value, (int)_nudTiltRH.Value);
            m.ThreshOuterL = (int)_nudThreshOuterL.Value; m.ThreshOuterR = (int)_nudThreshOuterR.Value;

            m.ThreshBtmInnerL = (int)_nudThreshBtmInnerL.Value; m.ThreshBtmInnerR = (int)_nudThreshBtmInnerR.Value;

            m.TargetOuterXOffsetMm = (double)_nudOuterTargetX.Value; m.OuterOffsetToleranceMm = (double)_nudOuterOffsetX.Value;
            m.TargetOuterAngleDeg = (double)_nudOuterTargetA.Value; m.OuterAngleToleranceDeg = (double)_nudOuterOffsetA.Value;

            m.BtmMeasureRoi = new CvRect((int)_nudBtmRoiX.Value, (int)_nudBtmRoiY.Value, (int)_nudBtmRoiW.Value, (int)_nudBtmRoiH.Value);
            m.BtmInnerLeftRoi = new CvRect((int)_nudBtmInnerLX.Value, (int)_nudBtmInnerLY.Value, (int)_nudBtmInnerLW.Value, (int)_nudBtmInnerLH.Value);
            m.BtmInnerRightRoi = new CvRect((int)_nudBtmInnerRX.Value, (int)_nudBtmInnerRY.Value, (int)_nudBtmInnerRW.Value, (int)_nudBtmInnerRH.Value);

            m.HolesRoi = new CvRect((int)_nudHolesX.Value, (int)_nudHolesY.Value, (int)_nudHolesW.Value, (int)_nudHolesH.Value);
            m.MinHoleArea = (int)_nudMinHoleArea.Value; m.MaxHoleArea = (int)_nudMaxHoleArea.Value; m.MinCircularity = (double)_nudMinCircularity.Value;
            m.SplitBoundaryX = (int)_nudSplitX.Value; m.SplitBoundaryY = (int)_nudSplitY.Value;
            m.ThreshTopLeft = (int)_nudThreshTL.Value; m.ThreshTopRight = (int)_nudThreshTR.Value; m.ThreshBtmLeft = (int)_nudThreshBL.Value; m.ThreshBtmRight = (int)_nudThreshBR.Value;

            m.JigLeftRoi = new CvRect((int)_nudJigLX.Value, (int)_nudJigLY.Value, (int)_nudJigLW.Value, (int)_nudJigLH.Value);
            m.JigRightRoi = new CvRect((int)_nudJigRX.Value, (int)_nudJigRY.Value, (int)_nudJigRW.Value, (int)_nudJigRH.Value);

            m.TargetJigDistanceMm = (double)_nudJigTarget.Value; m.JigToleranceMm = (double)_nudJigTolerance.Value;
            m.PixelToMmRatio = (double)_nudPixelToMm.Value;
            m.TargetXOffsetMm = (double)_nudTargetXOffset.Value; m.OffsetToleranceMm = (double)_nudOffsetTolerance.Value;
            m.TargetAngleDeg = (double)_nudTargetAngle.Value; m.AngleToleranceDeg = (double)_nudAngleTolerance.Value;
        }

        private void LoadSettingsToUI()
        {
            _isUpdatingUI = true;
            try
            {
                var m = _measurement;

                _chkEnableJigCheck.Checked = m.EnableJigCheck;
                _chkEnableOuterTiltCheck.Checked = m.EnableOuterTiltCheck;
                _chkEnableHoleCheck.Checked = m.EnableHoleCheck;

                _nudTiltLX.Value = m.TiltLeftRoi.X; _nudTiltLY.Value = m.TiltLeftRoi.Y; _nudTiltLW.Value = m.TiltLeftRoi.Width; _nudTiltLH.Value = m.TiltLeftRoi.Height;
                _nudTiltRX.Value = m.TiltRightRoi.X; _nudTiltRY.Value = m.TiltRightRoi.Y; _nudTiltRW.Value = m.TiltRightRoi.Width; _nudTiltRH.Value = m.TiltRightRoi.Height;
                _nudThreshOuterL.Value = m.ThreshOuterL; _nudThreshOuterR.Value = m.ThreshOuterR;

                _nudThreshBtmInnerL.Value = m.ThreshBtmInnerL; _nudThreshBtmInnerR.Value = m.ThreshBtmInnerR;

                _nudOuterTargetX.Value = (decimal)m.TargetOuterXOffsetMm; _nudOuterOffsetX.Value = (decimal)m.OuterOffsetToleranceMm;
                _nudOuterTargetA.Value = (decimal)m.TargetOuterAngleDeg; _nudOuterOffsetA.Value = (decimal)m.OuterAngleToleranceDeg;

                _nudBtmRoiX.Value = m.BtmMeasureRoi.X; _nudBtmRoiY.Value = m.BtmMeasureRoi.Y; _nudBtmRoiW.Value = m.BtmMeasureRoi.Width; _nudBtmRoiH.Value = m.BtmMeasureRoi.Height;
                _nudBtmInnerLX.Value = m.BtmInnerLeftRoi.X; _nudBtmInnerLY.Value = m.BtmInnerLeftRoi.Y; _nudBtmInnerLW.Value = m.BtmInnerLeftRoi.Width; _nudBtmInnerLH.Value = m.BtmInnerLeftRoi.Height;
                _nudBtmInnerRX.Value = m.BtmInnerRightRoi.X; _nudBtmInnerRY.Value = m.BtmInnerRightRoi.Y; _nudBtmInnerRW.Value = m.BtmInnerRightRoi.Width; _nudBtmInnerRH.Value = m.BtmInnerRightRoi.Height;

                _nudHolesX.Value = m.HolesRoi.X; _nudHolesY.Value = m.HolesRoi.Y; _nudHolesW.Value = m.HolesRoi.Width; _nudHolesH.Value = m.HolesRoi.Height;
                _nudMinHoleArea.Value = m.MinHoleArea; _nudMaxHoleArea.Value = m.MaxHoleArea; _nudMinCircularity.Value = (decimal)m.MinCircularity;
                _nudSplitX.Value = m.SplitBoundaryX; _nudSplitY.Value = m.SplitBoundaryY;
                _nudThreshTL.Value = m.ThreshTopLeft; _nudThreshTR.Value = m.ThreshTopRight;
                _nudThreshBL.Value = m.ThreshBtmLeft; _nudThreshBR.Value = m.ThreshBtmRight;

                _nudJigLX.Value = m.JigLeftRoi.X; _nudJigLY.Value = m.JigLeftRoi.Y; _nudJigLW.Value = m.JigLeftRoi.Width; _nudJigLH.Value = m.JigLeftRoi.Height;
                _nudJigRX.Value = m.JigRightRoi.X; _nudJigRY.Value = m.JigRightRoi.Y; _nudJigRW.Value = m.JigRightRoi.Width; _nudJigRH.Value = m.JigRightRoi.Height;

                _nudJigTarget.Value = (decimal)m.TargetJigDistanceMm; _nudJigTolerance.Value = (decimal)m.JigToleranceMm;
                _nudPixelToMm.Value = (decimal)m.PixelToMmRatio;
                _nudTargetXOffset.Value = (decimal)m.TargetXOffsetMm; _nudOffsetTolerance.Value = (decimal)m.OffsetToleranceMm;
                _nudTargetAngle.Value = (decimal)m.TargetAngleDeg; _nudAngleTolerance.Value = (decimal)m.AngleToleranceDeg;
            }
            catch { }
            finally { _isUpdatingUI = false; }
        }

        private void UpdateSettingsFromUI()
        {
            if (_isLoadingConfig || _isUpdatingUI) return;
            CopySettingsToMeasurement(_measurement);
            if (_nudExposureTime != null)
            {
                _appSettings.Cam.ExposureTime = (double)_nudExposureTime.Value;
                _camera.SetExposure(_appSettings.Cam.ExposureTime);
            }
        }

        private void LoadConfig()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt"); if (!File.Exists(path)) return;
            _isLoadingConfig = true;
            try
            {
                var d = File.ReadAllLines(path).Select(l => l.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
                int GetI(string k, int def) => d.TryGetValue(k, out var v) && int.TryParse(v, out int i) ? i : def;
                double GetD(string k, double def) => d.TryGetValue(k, out var v) && double.TryParse(v, out double num) ? num : def;

                var defM = new MeasurementCore();
                var m = _measurement;

                // 後方互換性：Cam0_プレフィックス付きの設定がある場合はそれを優先する
                bool enableJigCheck = d.TryGetValue("EnableJigCheck", out var ej) ? bool.Parse(ej) : (d.TryGetValue("Cam0_EnableJigCheck", out ej) ? bool.Parse(ej) : defM.EnableJigCheck);
                bool enableOuterTiltCheck = defM.EnableOuterTiltCheck;
                bool enableHoleCheck = defM.EnableHoleCheck;

                if (d.TryGetValue("UseOuterEdgeForTilt", out var uoe) || d.TryGetValue("Cam0_UseOuterEdgeForTilt", out uoe))
                {
                    bool useOuter = bool.Parse(uoe); enableOuterTiltCheck = useOuter; enableHoleCheck = !useOuter;
                }
                else
                {
                    enableOuterTiltCheck = d.TryGetValue("EnableOuterTiltCheck", out var eot) ? bool.Parse(eot) : (d.TryGetValue("Cam0_EnableOuterTiltCheck", out eot) ? bool.Parse(eot) : defM.EnableOuterTiltCheck);
                    enableHoleCheck = d.TryGetValue("EnableHoleCheck", out var ehc) ? bool.Parse(ehc) : (d.TryGetValue("Cam0_EnableHoleCheck", out ehc) ? bool.Parse(ehc) : defM.EnableHoleCheck);
                }

                m.EnableJigCheck = enableJigCheck;
                m.EnableOuterTiltCheck = enableOuterTiltCheck;
                m.EnableHoleCheck = enableHoleCheck;
                _configEnableOuterTilt = enableOuterTiltCheck;
                _configEnableHole = enableHoleCheck;

                int GetCamI(string key, int defVal) => d.TryGetValue(key, out var v) && int.TryParse(v, out int val) ? val : (d.TryGetValue("Cam0_" + key, out v) && int.TryParse(v, out val) ? val : defVal);
                double GetCamD(string key, double defVal) => d.TryGetValue(key, out var v) && double.TryParse(v, out double val) ? val : (d.TryGetValue("Cam0_" + key, out v) && double.TryParse(v, out val) ? val : defVal);

                m.TiltLeftRoi = new CvRect(GetCamI("TiltLX", defM.TiltLeftRoi.X), GetCamI("TiltLY", defM.TiltLeftRoi.Y), GetCamI("TiltLW", defM.TiltLeftRoi.Width), GetCamI("TiltLH", defM.TiltLeftRoi.Height));
                m.TiltRightRoi = new CvRect(GetCamI("TiltRX", defM.TiltRightRoi.X), GetCamI("TiltRY", defM.TiltRightRoi.Y), GetCamI("TiltRW", defM.TiltRightRoi.Width), GetCamI("TiltRH", defM.TiltRightRoi.Height));
                m.ThreshOuterL = GetCamI("ThreshOuterL", defM.ThreshOuterL); m.ThreshOuterR = GetCamI("ThreshOuterR", defM.ThreshOuterR);

                m.ThreshBtmInnerL = GetCamI("ThreshBtmInnerL", defM.ThreshBtmInnerL); m.ThreshBtmInnerR = GetCamI("ThreshBtmInnerR", defM.ThreshBtmInnerR);

                m.TargetOuterXOffsetMm = GetCamD("TargetOuterXOffsetMm", defM.TargetOuterXOffsetMm); m.OuterOffsetToleranceMm = GetCamD("OuterOffsetToleranceMm", defM.OuterOffsetToleranceMm);
                m.TargetOuterAngleDeg = GetCamD("TargetOuterAngleDeg", defM.TargetOuterAngleDeg); m.OuterAngleToleranceDeg = GetCamD("OuterAngleToleranceDeg", defM.OuterAngleToleranceDeg);

                m.BtmMeasureRoi = new CvRect(GetCamI("BtmRoiX", defM.BtmMeasureRoi.X), GetCamI("BtmRoiY", defM.BtmMeasureRoi.Y), GetCamI("BtmRoiW", defM.BtmMeasureRoi.Width), GetCamI("BtmRoiH", defM.BtmMeasureRoi.Height));
                m.BtmInnerLeftRoi = new CvRect(GetCamI("BtmInnerLX", defM.BtmInnerLeftRoi.X), GetCamI("BtmInnerLY", defM.BtmInnerLeftRoi.Y), GetCamI("BtmInnerLW", defM.BtmInnerLeftRoi.Width), GetCamI("BtmInnerLH", defM.BtmInnerLeftRoi.Height));
                m.BtmInnerRightRoi = new CvRect(GetCamI("BtmInnerRX", defM.BtmInnerRightRoi.X), GetCamI("BtmInnerRY", defM.BtmInnerRightRoi.Y), GetCamI("BtmInnerRW", defM.BtmInnerRightRoi.Width), GetCamI("BtmInnerRH", defM.BtmInnerRightRoi.Height));

                m.HolesRoi = new CvRect(GetCamI("HolesX", defM.HolesRoi.X), GetCamI("HolesY", defM.HolesRoi.Y), GetCamI("HolesW", defM.HolesRoi.Width), GetCamI("HolesH", defM.HolesRoi.Height));
                m.MinHoleArea = GetCamI("MinHoleArea", defM.MinHoleArea); m.MaxHoleArea = GetCamI("MaxHoleArea", defM.MaxHoleArea); m.MinCircularity = GetCamD("MinCirc", defM.MinCircularity);
                m.SplitBoundaryX = GetCamI("SplitBoundaryX", defM.SplitBoundaryX); m.SplitBoundaryY = GetCamI("SplitBoundaryY", defM.SplitBoundaryY);

                int oldEdge = GetCamI("EdgeThresh", defM.ThreshTopLeft); int oldHole = GetCamI("HoleThresh", defM.ThreshBtmLeft);
                m.ThreshTopLeft = GetCamI("ThreshTL", oldEdge); m.ThreshTopRight = GetCamI("ThreshTR", oldEdge);
                m.ThreshBtmLeft = GetCamI("ThreshBL", oldHole); m.ThreshBtmRight = GetCamI("ThreshBR", oldHole);

                m.JigLeftRoi = new CvRect(GetCamI("JigLX", defM.JigLeftRoi.X), GetCamI("JigLY", defM.JigLeftRoi.Y), GetCamI("JigLW", defM.JigLeftRoi.Width), GetCamI("JigLH", defM.JigLeftRoi.Height));
                m.JigRightRoi = new CvRect(GetCamI("JigRX", defM.JigRightRoi.X), GetCamI("JigRY", defM.JigRightRoi.Y), GetCamI("JigRW", defM.JigRightRoi.Width), GetCamI("JigRH", defM.JigRightRoi.Height));

                m.TargetJigDistanceMm = GetCamD("JigTargetMm", defM.TargetJigDistanceMm); m.JigToleranceMm = GetCamD("JigTolMm", defM.JigToleranceMm);
                m.PixelToMmRatio = GetCamD("PixelToMmRatio", defM.PixelToMmRatio);
                m.TargetXOffsetMm = GetCamD("TargetXOffsetMm", defM.TargetXOffsetMm); m.OffsetToleranceMm = GetCamD("OffsetToleranceMm", defM.OffsetToleranceMm);
                m.TargetAngleDeg = GetCamD("TargetAngleDeg", defM.TargetAngleDeg); m.AngleToleranceDeg = GetCamD("AngleToleranceDeg", defM.AngleToleranceDeg);

                _triggerOnBright = d.TryGetValue("TriggerOnBright", out var tb) ? bool.Parse(tb) : true;
                _triggerThreshold = GetD("TriggerThreshold", _triggerThreshold); _stabilityDurationMs = GetI("StabilityDurationMs", _stabilityDurationMs);
                _plcDelayMs = GetI("PlcDelayMs", _plcDelayMs); _maxRetryCount = GetI("MaxRetryCount", _maxRetryCount); _retryDelayMs = GetI("RetryDelayMs", _retryDelayMs);
                _autoStartCount = GetI("AutoStartCount", _autoStartCount); _resetThreshold = GetD("ResetThreshold", _resetThreshold); _saveMode = GetI("SaveMode", _saveMode);
                _roi = new CvRect(GetI("RoiX", _roi.X), GetI("RoiY", _roi.Y), GetI("RoiW", _roi.Width), GetI("RoiH", _roi.Height));
                _saveRoi = new CvRect(GetI("SaveRoiX", _saveRoi.X), GetI("SaveRoiY", _saveRoi.Y), GetI("SaveRoiW", _saveRoi.Width), GetI("SaveRoiH", _saveRoi.Height));
                _logKeepDays = GetI("LogKeepDays", _logKeepDays);

                _cmbTriggerMode.SelectedIndex = _triggerOnBright ? 0 : 1; _cmbSaveMode.SelectedIndex = _saveMode;
                _nudTriggerThreshold.Value = (decimal)_triggerThreshold; _nudStabilityDuration.Value = _stabilityDurationMs;
                _nudPlcDelayMs.Value = _plcDelayMs; _nudRetryCount.Value = _maxRetryCount; _nudRetryDelayMs.Value = _retryDelayMs;
                _nudAutoStartCount.Value = _autoStartCount; _nudResetThreshold.Value = (decimal)_resetThreshold;
                _nudRoiX.Value = _roi.X; _nudRoiY.Value = _roi.Y; _nudRoiW.Value = _roi.Width; _nudRoiH.Value = _roi.Height;
                _nudSaveRoiX.Value = _saveRoi.X; _nudSaveRoiY.Value = _saveRoi.Y; _nudSaveRoiW.Value = _saveRoi.Width; _nudSaveRoiH.Value = _saveRoi.Height;
                _nudLogKeepDays.Value = _logKeepDays;

                LoadSettingsToUI();
            }
            catch { }
            finally { _isLoadingConfig = false; }
        }

        private void SaveConfig()
        {
            try
            {
                using (var sw = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt")))
                {
                    sw.WriteLine("TriggerOnBright=" + _triggerOnBright); sw.WriteLine("TriggerThreshold=" + _triggerThreshold);
                    sw.WriteLine("StabilityDurationMs=" + _stabilityDurationMs);
                    sw.WriteLine("PlcDelayMs=" + _plcDelayMs); sw.WriteLine("MaxRetryCount=" + _maxRetryCount); sw.WriteLine("RetryDelayMs=" + _retryDelayMs);
                    sw.WriteLine("AutoStartCount=" + _autoStartCount); sw.WriteLine("ResetThreshold=" + _resetThreshold); sw.WriteLine("SaveMode=" + _saveMode);
                    sw.WriteLine("RoiX=" + _roi.X); sw.WriteLine("RoiY=" + _roi.Y); sw.WriteLine("RoiW=" + _roi.Width); sw.WriteLine("RoiH=" + _roi.Height);
                    sw.WriteLine("SaveRoiX=" + _saveRoi.X); sw.WriteLine("SaveRoiY=" + _saveRoi.Y); sw.WriteLine("SaveRoiW=" + _saveRoi.Width); sw.WriteLine("SaveRoiH=" + _saveRoi.Height);
                    sw.WriteLine("LogKeepDays=" + _logKeepDays);

                    var m = _measurement;

                    sw.WriteLine("EnableJigCheck=" + m.EnableJigCheck);
                    sw.WriteLine("EnableOuterTiltCheck=" + m.EnableOuterTiltCheck);
                    sw.WriteLine("EnableHoleCheck=" + m.EnableHoleCheck);

                    sw.WriteLine("TiltLX=" + m.TiltLeftRoi.X); sw.WriteLine("TiltLY=" + m.TiltLeftRoi.Y); sw.WriteLine("TiltLW=" + m.TiltLeftRoi.Width); sw.WriteLine("TiltLH=" + m.TiltLeftRoi.Height);
                    sw.WriteLine("TiltRX=" + m.TiltRightRoi.X); sw.WriteLine("TiltRY=" + m.TiltRightRoi.Y); sw.WriteLine("TiltRW=" + m.TiltRightRoi.Width); sw.WriteLine("TiltRH=" + m.TiltRightRoi.Height);
                    sw.WriteLine("ThreshOuterL=" + m.ThreshOuterL); sw.WriteLine("ThreshOuterR=" + m.ThreshOuterR);

                    sw.WriteLine("ThreshBtmInnerL=" + m.ThreshBtmInnerL); sw.WriteLine("ThreshBtmInnerR=" + m.ThreshBtmInnerR);

                    sw.WriteLine("TargetOuterXOffsetMm=" + m.TargetOuterXOffsetMm); sw.WriteLine("OuterOffsetToleranceMm=" + m.OuterOffsetToleranceMm);
                    sw.WriteLine("TargetOuterAngleDeg=" + m.TargetOuterAngleDeg); sw.WriteLine("OuterAngleToleranceDeg=" + m.OuterAngleToleranceDeg);

                    sw.WriteLine("BtmRoiX=" + m.BtmMeasureRoi.X); sw.WriteLine("BtmRoiY=" + m.BtmMeasureRoi.Y); sw.WriteLine("BtmRoiW=" + m.BtmMeasureRoi.Width); sw.WriteLine("BtmRoiH=" + m.BtmMeasureRoi.Height);
                    sw.WriteLine("BtmInnerLX=" + m.BtmInnerLeftRoi.X); sw.WriteLine("BtmInnerLY=" + m.BtmInnerLeftRoi.Y); sw.WriteLine("BtmInnerLW=" + m.BtmInnerLeftRoi.Width); sw.WriteLine("BtmInnerLH=" + m.BtmInnerLeftRoi.Height);
                    sw.WriteLine("BtmInnerRX=" + m.BtmInnerRightRoi.X); sw.WriteLine("BtmInnerRY=" + m.BtmInnerRightRoi.Y); sw.WriteLine("BtmInnerRW=" + m.BtmInnerRightRoi.Width); sw.WriteLine("BtmInnerRH=" + m.BtmInnerRightRoi.Height);

                    sw.WriteLine("HolesX=" + m.HolesRoi.X); sw.WriteLine("HolesY=" + m.HolesRoi.Y); sw.WriteLine("HolesW=" + m.HolesRoi.Width); sw.WriteLine("HolesH=" + m.HolesRoi.Height);
                    sw.WriteLine("MinHoleArea=" + m.MinHoleArea); sw.WriteLine("MaxHoleArea=" + m.MaxHoleArea);
                    sw.WriteLine("MinCirc=" + m.MinCircularity);
                    sw.WriteLine("SplitBoundaryX=" + m.SplitBoundaryX); sw.WriteLine("SplitBoundaryY=" + m.SplitBoundaryY);
                    sw.WriteLine("ThreshTL=" + m.ThreshTopLeft); sw.WriteLine("ThreshTR=" + m.ThreshTopRight);
                    sw.WriteLine("ThreshBL=" + m.ThreshBtmLeft); sw.WriteLine("ThreshBR=" + m.ThreshBtmRight);

                    sw.WriteLine("JigLX=" + m.JigLeftRoi.X); sw.WriteLine("JigLY=" + m.JigLeftRoi.Y); sw.WriteLine("JigLW=" + m.JigLeftRoi.Width); sw.WriteLine("JigLH=" + m.JigLeftRoi.Height);
                    sw.WriteLine("JigRX=" + m.JigRightRoi.X); sw.WriteLine("JigRY=" + m.JigRightRoi.Y); sw.WriteLine("JigRW=" + m.JigRightRoi.Width); sw.WriteLine("JigRH=" + m.JigRightRoi.Height);

                    sw.WriteLine("JigTargetMm=" + m.TargetJigDistanceMm); sw.WriteLine("JigTolMm=" + m.JigToleranceMm);
                    sw.WriteLine("PixelToMmRatio=" + m.PixelToMmRatio);
                    sw.WriteLine("TargetXOffsetMm=" + m.TargetXOffsetMm); sw.WriteLine("OffsetToleranceMm=" + m.OffsetToleranceMm);
                    sw.WriteLine("TargetAngleDeg=" + m.TargetAngleDeg); sw.WriteLine("AngleToleranceDeg=" + m.AngleToleranceDeg);
                }
            }
            catch (Exception ex) { MessageBox.Show("設定ファイルの保存に失敗しました。\n\n" + ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}