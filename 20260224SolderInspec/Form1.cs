using System;
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

namespace _20260224SolderInspec
{
    public class Form1 : Form
    {
        private const int STATE_WAITING = 0;
        private const int STATE_STABILIZING = 1;
        private const int STATE_COOLING = 2;

        private TeliCamera[] _cameras = new TeliCamera[2];
        private MeasurementCore[] _measurements = new MeasurementCore[2];
        private PlcCommunicator _plc;
        private AppSettings _appSettings;
        private ProductionAnalyzer _analyzer = new ProductionAnalyzer();

        private PictureBox[] _pictureBoxes = new PictureBox[2];
        private PictureBox _pictureBoxDebug;
        private TabControl _tabControl;
        private TextBox _txtLog;

        private Label[] _lblStatuses = new Label[2];
        private Label[] _lblBrightnesses = new Label[2];
        private Label[] _lblFpsList = new Label[2];
        private Label[] _lblBigResults = new Label[2];
        private Label _lblTotal, _lblOk, _lblNg, _lblCurrentHoleDistPx;

        private CheckBox _chkShowOverlay, _chkEnableJigCheck;
        private CheckBox _chkEnableOuterTiltCheck, _chkEnableHoleCheck;

        private ComboBox _cmbTriggerMode, _cmbSaveMode;

        private Button _btnRunToggle;
        private bool _isRunning = false;
        private bool[] _requestErrorTests = new bool[2];
        private bool[] _requestOkTests = new bool[2]; // ★テスト用: 強制OKフラグ

        private NumericUpDown _nudTriggerThreshold, _nudStabilityDuration, _nudResetThreshold;
        private NumericUpDown _nudRoiX, _nudRoiY, _nudRoiW, _nudRoiH;
        private NumericUpDown _nudSaveRoiX, _nudSaveRoiY, _nudSaveRoiW, _nudSaveRoiH;

        private NumericUpDown _nudLogKeepDays;
        private int _logKeepDays = 30;

        private NumericUpDown _nudAutoStartCount;
        private int _autoStartCount = 3;
        private int _missedTriggerCount = 0;
        private bool[] _wasTriggeredLastFrames = new bool[2];

        private NumericUpDown _nudBtmRoiX, _nudBtmRoiY, _nudBtmRoiW, _nudBtmRoiH;

        private NumericUpDown _nudBtmInnerLX, _nudBtmInnerLY, _nudBtmInnerLW, _nudBtmInnerLH;
        private NumericUpDown _nudBtmInnerRX, _nudBtmInnerRY, _nudBtmInnerRW, _nudBtmInnerRH;

        private NumericUpDown _nudHolesX, _nudHolesY, _nudHolesW, _nudHolesH;
        private NumericUpDown _nudMinHoleArea, _nudMaxHoleArea, _nudMinCircularity;

        private NumericUpDown _nudTiltLX, _nudTiltLY, _nudTiltLW, _nudTiltLH;
        private NumericUpDown _nudTiltRX, _nudTiltRY, _nudTiltRW, _nudTiltRH;

        private NumericUpDown _nudThreshOuterL, _nudThreshOuterR;
        private NumericUpDown _nudThreshBtmInnerL, _nudThreshBtmInnerR;

        private NumericUpDown _nudSplitX, _nudSplitY;
        private NumericUpDown _nudThreshTL, _nudThreshTR, _nudThreshBL, _nudThreshBR;

        private NumericUpDown _nudJigLX, _nudJigLY, _nudJigLW, _nudJigLH;
        private NumericUpDown _nudJigRX, _nudJigRY, _nudJigRW, _nudJigRH;
        private NumericUpDown _nudJigTarget, _nudJigTolerance, _nudPixelToMm;

        private NumericUpDown _nudOuterTargetX, _nudOuterOffsetX, _nudOuterTargetA, _nudOuterOffsetA;
        private NumericUpDown _nudTargetXOffset, _nudOffsetTolerance, _nudTargetAngle, _nudAngleTolerance;
        private NumericUpDown _nudActualWidthMm;

        private NumericUpDown _nudPlcDelayMs;
        private int _plcDelayMs = 100;

        private NumericUpDown _nudRetryCount;
        private NumericUpDown _nudRetryDelayMs;
        private int _maxRetryCount = 3;
        private int _retryDelayMs = 100;
        private int _currentRetry = 0;

        private Button _btnCalcRatio;

        private int[] _currentStates = new int[] { STATE_WAITING, STATE_WAITING };
        private DateTime[] _stabilityStartTimes = new DateTime[2];
        private DateTime[] _cooldownStartTimes = new DateTime[2];
        private int _cooldownDurationMs = 500;

        private CvRect _roi = new CvRect(300, 200, 100, 100);
        private CvRect _saveRoi = new CvRect(100, 50, 440, 380);

        private bool[] _requestManualTests = new bool[2];
        private bool _isLoadingConfig = false, _isUiLoaded = false;
        private bool[] _isProcessing = new bool[2];
        private bool _isMonitoring = false;
        private bool[] _plcTriggerReceived = new bool[2];

        private int _selectedCamIndex = 0;
        private ComboBox _cmbInspectionCam;
        private ComboBox _cmbDebugCam;
        private bool _isUpdatingUI = false;

        private int _totalCount = 0, _okCount = 0, _ngCount = 0, _saveMode = 0, _stabilityDurationMs = 300;
        private int[] _pendingSaveResults = new int[] { -1, -1 };
        private bool _triggerOnBright = true;
        private double _triggerThreshold = 100.0, _resetThreshold = 50.0;
        private string _logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        private DateTime[] _lastUiUpdateTimes = new DateTime[] { DateTime.MinValue, DateTime.MinValue };
        private DateTime[] _lastFrameProcessTimes = new DateTime[] { DateTime.MinValue, DateTime.MinValue };
        private bool _isDebugTabActive = false;

        private int[] _camFrameCounts = new int[2];
        private int[] _procFrameCounts = new int[2];
        private int[] _uiFrameCounts = new int[2];
        private DateTime _lastFpsTime = DateTime.Now;
        private string[] _currentFpsTexts = new string[] { "Cam1 FPS: --", "Cam2 FPS: --" };

        public Form1()
        {
            _appSettings = AppSettings.Load();
            _cameras[0] = new TeliCamera();
            _cameras[1] = new TeliCamera();
            _measurements[0] = new MeasurementCore();
            _measurements[1] = new MeasurementCore();
            _plc = new PlcCommunicator(_appSettings);

            InitializeCustomUI();
            _cameras[0].OnFrameCaptured += (s, e) => Camera_OnFrameCaptured(s, e, 0);
            _cameras[1].OnFrameCaptured += (s, e) => Camera_OnFrameCaptured(s, e, 1);

            _plc.OnLog += (msg, isErr) => AppendLog(msg, isErr);

            LoadConfig();

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;

            AppendLog("アプリケーションを起動しました。");
        }

        private void InitializeCustomUI()
        {
            this.Text = "Punching Metal Auto Inspection System (Bulletproof Dual-Engine)";
            this.Size = new Size(1920, 1000);
            this.StartPosition = FormStartPosition.CenterScreen;

            for (int i = 0; i < 2; i++)
            {
                int px = 10 + (i * 650);
                _pictureBoxes[i] = new PictureBox { Location = new Point(px, 10), Size = new Size(640, 480), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
                this.Controls.Add(_pictureBoxes[i]);

                _lblStatuses[i] = new Label { Text = $"Cam{i+1} Status: STOPPED", Location = new Point(px, 500), AutoSize = true, Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold), ForeColor = Color.Red };
                _lblBrightnesses[i] = new Label { Text = $"Cam{i+1} Brightness: 0.0", Location = new Point(px, 530), AutoSize = true, Font = new Font(this.Font.FontFamily, 12) };
                _lblFpsList[i] = new Label { Text = $"Cam{i+1} FPS: --", Location = new Point(px + 180, 530), AutoSize = true, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold), ForeColor = Color.Blue };
                this.Controls.Add(_lblStatuses[i]); this.Controls.Add(_lblBrightnesses[i]); this.Controls.Add(_lblFpsList[i]);
            }

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
            // _txtLog will be added to the Settings tab later instead of main Form

            int tabX = 1320;
            _tabControl = new TabControl { Location = new Point(tabX, 10), Size = new Size(580, 750), Font = new Font(this.Font.FontFamily, 10) };
            _tabControl.SelectedIndexChanged += (s, e) => { _isDebugTabActive = (_tabControl.SelectedIndex == 3); };
            this.Controls.Add(_tabControl);

            TabPage t1 = new TabPage("運用 (Main)"); InitializeMainTab(t1); _tabControl.TabPages.Add(t1);
            TabPage t2 = new TabPage("設定 (Settings)") { AutoScroll = true }; InitializeSettingsTab(t2); _tabControl.TabPages.Add(t2);
            TabPage t3 = new TabPage("検査設定 (Inspection)") { AutoScroll = true }; InitializeInspectionTab(t3); _tabControl.TabPages.Add(t3);
            TabPage t4 = new TabPage("画像確認 (Debug)") { AutoScroll = true }; InitializeDebugTab(t4); _tabControl.TabPages.Add(t4);
        }

        private void InitializeMainTab(TabPage tab)
        {
            int y = 10;
            _btnRunToggle = new Button { Text = "▶ 運転開始 (START)", Location = new Point(10, y), Size = new Size(540, 60), BackColor = Color.LightGreen, Font = new Font(this.Font.FontFamily, 16, FontStyle.Bold) };
            _btnRunToggle.Click += (s, e) => {
                _isRunning = !_isRunning; _missedTriggerCount = 0;
                if (_isRunning) {
                    _btnRunToggle.Text = "■ 運転停止 (STOP)"; _btnRunToggle.BackColor = Color.Salmon;
                    _currentStates[0] = STATE_WAITING; _currentStates[1] = STATE_WAITING;
                    SafeInvoke(() => lblStateUpdate(0, "READY", Color.LightGray));
                    SafeInvoke(() => lblStateUpdate(1, "READY", Color.LightGray));
                }
                else {
                    _btnRunToggle.Text = "▶ 運転開始 (START)"; _btnRunToggle.BackColor = Color.LightGreen;
                    _currentStates[0] = STATE_WAITING; _currentStates[1] = STATE_WAITING;
                    SafeInvoke(() => lblStateUpdate(0, "STOPPED", Color.DarkGray));
                    SafeInvoke(() => lblStateUpdate(1, "STOPPED", Color.DarkGray));
                }
            };
            tab.Controls.Add(_btnRunToggle); y += 75;

            _chkShowOverlay = new CheckBox { Text = "計測パラメータを表示する", Location = new Point(10, y), AutoSize = true, Checked = true };
            tab.Controls.Add(_chkShowOverlay); y += 30;

            _lblBigResults[0] = new Label { Text = "Cam1: STOPPED", Location = new Point(10, y), Size = new Size(260, 80), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 24, FontStyle.Bold), BackColor = Color.DarkGray };
            _lblBigResults[1] = new Label { Text = "Cam2: STOPPED", Location = new Point(280, y), Size = new Size(260, 80), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 24, FontStyle.Bold), BackColor = Color.DarkGray };
            tab.Controls.Add(_lblBigResults[0]); tab.Controls.Add(_lblBigResults[1]); y += 95;

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
                _requestManualTests[0] = true; _requestManualTests[1] = true;
                _requestOkTests[0] = false; _requestOkTests[1] = false;
                _requestErrorTests[0] = false; _requestErrorTests[1] = false;
            };
            tab.Controls.Add(btnTest); y += 40;
        }

        private void InitializeSettingsTab(TabPage tab)
        {
            int y = 10, lw = 150, cw = 100, lh = 28;
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0, decimal step = 1M)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = step };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(n); y += lh;
            }

            tab.Controls.Add(new Label { Text = "--- システム動作モード ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Red }); y += 22;
            ComboBox cmbAppMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw + 50, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbAppMode.Items.AddRange(new object[] { "Visual (カメラ輝度自動)", "Plc (ネットワーク指令)" });
            cmbAppMode.SelectedIndex = _appSettings.TriggerMode == "Visual" ? 0 : 1;
            cmbAppMode.SelectedIndexChanged += (s, e) => { _appSettings.TriggerMode = cmbAppMode.SelectedIndex == 0 ? "Visual" : "Plc"; _appSettings.Save(); };
            tab.Controls.Add(new Label { Text = "検査トリガー元:", Location = new Point(10, y + 2), Size = new Size(lw, 20) }); tab.Controls.Add(cmbAppMode); y += lh;

            AddN("PLC Delay(待機) ms:", ref _nudPlcDelayMs, 0, 5000, _plcDelayMs); y += 10;

            tab.Controls.Add(new Label { Text = "--- ポカヨケ (自動起動) 設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Purple }); y += 22;
            AddN("自動起動トリガー回数:", ref _nudAutoStartCount, 0, 100, _autoStartCount);
            tab.Controls.Add(new Label { Text = "※0で無効。指定回数トリガーが来たら自動で運転開始します", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Gray }); y += 20;

            tab.Controls.Add(new Label { Text = "--- 検査リトライ設定 (煙対策) ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Red }); y += 22;
            AddN("最大リトライ回数:", ref _nudRetryCount, 0, 10, _maxRetryCount);
            AddN("リトライ間隔(ms):", ref _nudRetryDelayMs, 0, 5000, _retryDelayMs); y += 10;

            tab.Controls.Add(new Label { Text = "--- トリガー設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            _cmbTriggerMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTriggerMode.Items.AddRange(new object[] { "明転 (>)", "暗転 (<)" });
            _cmbTriggerMode.SelectedIndex = _triggerOnBright ? 0 : 1;
            _cmbTriggerMode.SelectedIndexChanged += (s, e) => { if (!_isLoadingConfig) _triggerOnBright = _cmbTriggerMode.SelectedIndex == 0; };
            tab.Controls.Add(new Label { Text = "Visual Trigger:", Location = new Point(10, y + 2), Size = new Size(lw, 20) }); tab.Controls.Add(_cmbTriggerMode); y += lh;

            AddN("Trigger Thresh:", ref _nudTriggerThreshold, 0, 255, (decimal)_triggerThreshold, 1, 0.5M);
            AddN("Stability (ms):", ref _nudStabilityDuration, 0, 5000, _stabilityDurationMs);
            AddN("Reset Thresh:", ref _nudResetThreshold, 0, 255, (decimal)_resetThreshold, 1, 0.5M); y += 10;

            tab.Controls.Add(new Label { Text = "--- 輝度監視 ROI 設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddN("ROI X:", ref _nudRoiX, 0, 3000, _roi.X); AddN("ROI Y:", ref _nudRoiY, 0, 3000, _roi.Y);
            AddN("ROI W:", ref _nudRoiW, 1, 3000, _roi.Width); AddN("ROI H:", ref _nudRoiH, 1, 3000, _roi.Height); y += 10;

            tab.Controls.Add(new Label { Text = "--- 画像・ログ保存設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            _cmbSaveMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw + 50, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSaveMode.Items.AddRange(new object[] { "0: 保存しない", "1: NGのみ保存", "2: 全て保存" });
            _cmbSaveMode.SelectedIndex = _saveMode;
            _cmbSaveMode.SelectedIndexChanged += (s, e) => { if (!_isLoadingConfig) _saveMode = _cmbSaveMode.SelectedIndex; };
            tab.Controls.Add(new Label { Text = "画像保存モード:", Location = new Point(10, y + 2), Size = new Size(lw, 20) }); tab.Controls.Add(_cmbSaveMode); y += lh;

            AddN("ログ保存期間(日) ※0で無期限:", ref _nudLogKeepDays, 0, 3650, _logKeepDays);
            AddN("Save ROI X:", ref _nudSaveRoiX, 0, 3000, _saveRoi.X); AddN("Save ROI Y:", ref _nudSaveRoiY, 0, 3000, _saveRoi.Y);
            AddN("Save ROI W:", ref _nudSaveRoiW, 1, 3000, _saveRoi.Width); AddN("Save ROI H:", ref _nudSaveRoiH, 1, 3000, _saveRoi.Height); y += 20;

            Button btnSave = new Button { Text = "設定を保存する (Save)", Location = new Point(10, y), Size = new Size(540, 40), BackColor = Color.LightGreen };
            btnSave.Click += (s, e) => { SaveConfig(); MessageBox.Show("保存しました。"); }; tab.Controls.Add(btnSave); y += 60;

            tab.Controls.Add(new Label { Text = "--- デバッグ / メンテナンス ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkOrange }); y += 22;

            Button btnTestOk = new Button { Text = "強制OKテスト", Location = new Point(10, y), Size = new Size(260, 35), BackColor = Color.LightGreen, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            btnTestOk.Click += (s, e) => {
                _requestOkTests[0] = true; _requestOkTests[1] = true;
                _requestErrorTests[0] = false; _requestErrorTests[1] = false;
            };
            tab.Controls.Add(btnTestOk);

            Button btnTestNg = new Button { Text = "強制NGテスト", Location = new Point(290, y), Size = new Size(260, 35), BackColor = Color.Orange, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            btnTestNg.Click += (s, e) => {
                _requestErrorTests[0] = true; _requestErrorTests[1] = true;
                _requestOkTests[0] = false; _requestOkTests[1] = false;
            };
            tab.Controls.Add(btnTestNg); y += 45;

            if (_txtLog != null) {
                _txtLog.Location = new Point(10, y);
                _txtLog.Size = new Size(540, 250);
                tab.Controls.Add(_txtLog);
                y += 260;
            }
        }

        private void InitializeInspectionTab(TabPage tab)
        {
            int y = 10;

            tab.Controls.Add(new Label { Text = "対象カメラ:", Location = new Point(10, y + 2), Size = new Size(100, 20), Font = new Font(this.Font, FontStyle.Bold) });
            _cmbInspectionCam = new ComboBox { Location = new Point(110, y), Size = new Size(150, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbInspectionCam.Items.AddRange(new object[] { "Camera 1", "Camera 2" });
            _cmbInspectionCam.SelectedIndex = 0;
            _cmbInspectionCam.SelectedIndexChanged += (s, e) => {
                if (_isUpdatingUI) return;
                _selectedCamIndex = _cmbInspectionCam.SelectedIndex;
                if (_cmbDebugCam != null && _cmbDebugCam.SelectedIndex != _selectedCamIndex)
                    _cmbDebugCam.SelectedIndex = _selectedCamIndex;
                LoadSettingsToUI();
            };
            tab.Controls.Add(_cmbInspectionCam);
            y += 40;

            var m = _measurements[_selectedCamIndex];
            int lw = 160, cw = 100, lh = 28;
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0, decimal step = 1M)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = step };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(n); y += lh;
            }
            void AddRect(string txt, ref NumericUpDown nx, ref NumericUpDown ny, ref NumericUpDown nw, ref NumericUpDown nh, CvRect r)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(110, 20) });
                int sx = 120, step = 65, boxW = 60;
                nx = new NumericUpDown { Location = new Point(sx, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.X };
                ny = new NumericUpDown { Location = new Point(sx + step, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.Y };
                nw = new NumericUpDown { Location = new Point(sx + step * 2, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Width };
                nh = new NumericUpDown { Location = new Point(sx + step * 3, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Height };
                nx.ValueChanged += (s, e) => UpdateSettingsFromUI(); ny.ValueChanged += (s, e) => UpdateSettingsFromUI();
                nw.ValueChanged += (s, e) => UpdateSettingsFromUI(); nh.ValueChanged += (s, e) => UpdateSettingsFromUI();
                tab.Controls.Add(nx); tab.Controls.Add(ny); tab.Controls.Add(nw); tab.Controls.Add(nh); y += lh;
            }

            tab.Controls.Add(new Label { Text = "--- 検査モード 選択 (複数ONで並列・OR判定) ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Magenta, Font = new Font(this.Font, FontStyle.Bold) }); y += 22;
            _chkEnableJigCheck = new CheckBox { Text = "エッジ間距離測定を有効にする", Location = new Point(10, y), AutoSize = true, Checked = m.EnableJigCheck };
            _chkEnableJigCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(_chkEnableJigCheck); y += 22;
            _chkEnableOuterTiltCheck = new CheckBox { Text = "【モードA】 外形エッジで製品の傾き・ズレを検査する", Location = new Point(10, y), AutoSize = true, Checked = m.EnableOuterTiltCheck, ForeColor = Color.Teal };
            _chkEnableOuterTiltCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(_chkEnableOuterTiltCheck); y += 22;
            _chkEnableHoleCheck = new CheckBox { Text = "【モードB】 穴で製品の傾き・ズレを検査する", Location = new Point(10, y), AutoSize = true, Checked = m.EnableHoleCheck, ForeColor = Color.Blue };
            _chkEnableHoleCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(_chkEnableHoleCheck); y += 28;

            tab.Controls.Add(new Label { Text = "--- 【モードA】 外形エッジ パラメータ ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Teal }); y += 22;
            AddRect("左エッジROI(青):", ref _nudTiltLX, ref _nudTiltLY, ref _nudTiltLW, ref _nudTiltLH, m.TiltLeftRoi);
            AddRect("右エッジROI(青):", ref _nudTiltRX, ref _nudTiltRY, ref _nudTiltRW, ref _nudTiltRH, m.TiltRightRoi);
            AddN("目標 Xずれ(mm):", ref _nudOuterTargetX, -100, 100, (decimal)m.TargetOuterXOffsetMm, 2, 0.1M);
            AddN("Xずれ許容(mm):", ref _nudOuterOffsetX, 0, 50, (decimal)m.OuterOffsetToleranceMm, 2, 0.1M);
            AddN("目標 Θ(deg):", ref _nudOuterTargetA, -180, 180, (decimal)m.TargetOuterAngleDeg, 2, 0.1M);
            AddN("Θ許容(deg):", ref _nudOuterOffsetA, 0, 90, (decimal)m.OuterAngleToleranceDeg, 2, 0.1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- 【モードB】 穴 パラメータ ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddRect("基準穴 ROI:", ref _nudHolesX, ref _nudHolesY, ref _nudHolesW, ref _nudHolesH, m.HolesRoi);
            AddN("穴 最小面積:", ref _nudMinHoleArea, 0, 100000, m.MinHoleArea);
            AddN("穴 最大面積:", ref _nudMaxHoleArea, 0, 1000000, m.MaxHoleArea);
            AddN("真円度しきい値:", ref _nudMinCircularity, 0, 1, (decimal)m.MinCircularity, 2, 0.05M);
            AddN("目標 Xずれ(mm):", ref _nudTargetXOffset, -100, 100, (decimal)m.TargetXOffsetMm, 2, 0.1M);
            AddN("Xずれ許容(mm):", ref _nudOffsetTolerance, 0, 50, (decimal)m.OffsetToleranceMm, 2, 0.1M);
            AddN("目標 Θ(deg):", ref _nudTargetAngle, -180, 180, (decimal)m.TargetAngleDeg, 2, 0.1M);
            AddN("Θ許容(deg):", ref _nudAngleTolerance, 0, 90, (decimal)m.AngleToleranceDeg, 2, 0.1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- エッジ間距離 測定設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Olive }); y += 22;
            AddRect("左エッジ ROI:", ref _nudJigLX, ref _nudJigLY, ref _nudJigLW, ref _nudJigLH, m.JigLeftRoi);
            AddRect("右エッジ ROI:", ref _nudJigRX, ref _nudJigRY, ref _nudJigRW, ref _nudJigRH, m.JigRightRoi);
            AddN("エッジ目標距離(mm):", ref _nudJigTarget, 0, 500, (decimal)m.TargetJigDistanceMm, 2, 0.1M);
            AddN("エッジ許容誤差(mm):", ref _nudJigTolerance, 0, 50, (decimal)m.JigToleranceMm, 2, 0.1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- 下部測定 ROI ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkGoldenrod }); y += 22;
            AddRect("Btm線 ROI(黄):", ref _nudBtmRoiX, ref _nudBtmRoiY, ref _nudBtmRoiW, ref _nudBtmRoiH, m.BtmMeasureRoi);
            AddRect("Btm内側左 ROI(赤):", ref _nudBtmInnerLX, ref _nudBtmInnerLY, ref _nudBtmInnerLW, ref _nudBtmInnerLH, m.BtmInnerLeftRoi);
            AddRect("Btm内側右 ROI(赤):", ref _nudBtmInnerRX, ref _nudBtmInnerRY, ref _nudBtmInnerRW, ref _nudBtmInnerRH, m.BtmInnerRightRoi); y += 10;

            tab.Controls.Add(new Label { Text = "--- キャリブレーション (Pixel/mm比率) ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkOrange }); y += 22;
            _lblCurrentHoleDistPx = new Label { Text = "現在の穴/エッジ間距離: 0.0 px", Location = new Point(10, y), Size = new Size(300, 20), ForeColor = Color.DarkOrange, Font = new Font(this.Font, FontStyle.Bold) };
            tab.Controls.Add(_lblCurrentHoleDistPx); y += lh;
            _nudActualWidthMm = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0.1M, Maximum = 500, Value = 50, DecimalPlaces = 2, Increment = 0.1M };
            tab.Controls.Add(new Label { Text = "実測の距離(mm):", Location = new Point(10, y + 2), Size = new Size(lw, 20) }); tab.Controls.Add(_nudActualWidthMm); y += lh;
            _btnCalcRatio = new Button { Text = "比率を自動計算", Location = new Point(10, y), Size = new Size(360, 30), BackColor = Color.LightYellow };
            _btnCalcRatio.Click += (s, e) => {
                if (m.LastHoleDistancePx <= 0) { MessageBox.Show("先にテスト実行して検出させてください。"); return; }
                _nudPixelToMm.Value = _nudActualWidthMm.Value / (decimal)m.LastHoleDistancePx;
                MessageBox.Show("更新しました。各種目標(mm)を再設定してください。");
            }; tab.Controls.Add(_btnCalcRatio); y += 45;
            AddN("Pixel->mm比率:", ref _nudPixelToMm, 0.0001M, 1, (decimal)m.PixelToMmRatio, 5, 0.001M);
        }

        private void InitializeDebugTab(TabPage tab)
        {
            int y = 10;

            tab.Controls.Add(new Label { Text = "対象カメラ:", Location = new Point(10, y + 2), Size = new Size(100, 20), Font = new Font(this.Font, FontStyle.Bold) });
            _cmbDebugCam = new ComboBox { Location = new Point(110, y), Size = new Size(150, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbDebugCam.Items.AddRange(new object[] { "Camera 1", "Camera 2" });
            _cmbDebugCam.SelectedIndex = 0;
            _cmbDebugCam.SelectedIndexChanged += (s, e) => {
                if (_isUpdatingUI) return;
                _selectedCamIndex = _cmbDebugCam.SelectedIndex;
                if (_cmbInspectionCam != null && _cmbInspectionCam.SelectedIndex != _selectedCamIndex)
                    _cmbInspectionCam.SelectedIndex = _selectedCamIndex;
                LoadSettingsToUI();
            };
            tab.Controls.Add(_cmbDebugCam);
            y += 40;

            var m = _measurements[_selectedCamIndex];
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

        private void Form1_Load(object sender, EventArgs e)
        {
            _isUiLoaded = true;
            if (_cameras[0].Initialize(0)) _cameras[0].StartCapture();
            if (_cameras[1].Initialize(1)) _cameras[1].StartCapture();
            _ = MonitorPlcTriggerAsync();
            Task.Run(() => DeleteOldLogs());
            RestoreDailyCounter();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _isMonitoring = false; _isUiLoaded = false;
            _cameras[0].StopCapture(); _cameras[0].Dispose();
            _cameras[1].StopCapture(); _cameras[1].Dispose();
            _plc.Disconnect(); SaveConfig();
        }

        private void AppendDailyLog(int result, double brightness)
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
                        sw.WriteLine("時刻,判定,総検査数,良品数(OK),不良数(NG),トリガー時輝度(実測値)");
                    }

                    string resStr = (result == 1) ? "OK" : "NG";
                    sw.WriteLine($"{DateTime.Now:HH:mm:ss},{resStr},{_totalCount},{_okCount},{_ngCount},{brightness:F2}");
                }
            }
            catch { }
        }

        private void RestoreDailyCounter() { try { string logFile = Path.Combine(_logDirPath, DateTime.Now.ToString("yyyyMMdd"), "InspectionLog.csv"); if (File.Exists(logFile)) { var lines = File.ReadAllLines(logFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList(); if (lines.Count > 1) { var cols = lines.Last().Split(','); if (cols.Length >= 5) { int.TryParse(cols[2], out _totalCount); int.TryParse(cols[3], out _okCount); int.TryParse(cols[4], out _ngCount); } } } SafeInvoke(() => UpdateCounterDisplay()); } catch { } }

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
                        int t1 = await Task.Run(() => _plc.ReadDevice(_appSettings.Cam1.ReadDeviceAddress));
                        if (t1 == 1)
                        {
                            if (!_plcTriggerReceived[0]) AppendLog($"Cam1: M{_appSettings.Cam1.ReadDeviceAddress} より検査トリガを受信しました");
                            _plcTriggerReceived[0] = true;
                        }

                        int t2 = await Task.Run(() => _plc.ReadDevice(_appSettings.Cam2.ReadDeviceAddress));
                        if (t2 == 1)
                        {
                            if (!_plcTriggerReceived[1]) AppendLog($"Cam2: M{_appSettings.Cam2.ReadDeviceAddress} より検査トリガを受信しました");
                            _plcTriggerReceived[1] = true;
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
                        _txtLog.Text = _txtLog.Text.Substring(25000);
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

        private void Camera_OnFrameCaptured(object sender, Mat frame, int camIndex)
        {
            if (!_isUiLoaded || this.IsDisposed || frame == null || frame.Empty()) return;

            _camFrameCounts[camIndex]++;
            if ((DateTime.Now - _lastFpsTime).TotalMilliseconds >= 1000) {
                _currentFpsTexts[0] = $"Cam1 FPS: Cam:{_camFrameCounts[0]} / Proc:{_procFrameCounts[0]} / UI:{_uiFrameCounts[0]}";
                _currentFpsTexts[1] = $"Cam2 FPS: Cam:{_camFrameCounts[1]} / Proc:{_procFrameCounts[1]} / UI:{_uiFrameCounts[1]}";
                _camFrameCounts[0] = 0; _procFrameCounts[0] = 0; _uiFrameCounts[0] = 0;
                _camFrameCounts[1] = 0; _procFrameCounts[1] = 0; _uiFrameCounts[1] = 0;
                _lastFpsTime = DateTime.Now;
            }

            double limitMs = 33.0; if (!_isRunning && _autoStartCount == 0) limitMs = 200.0; else if (_appSettings.TriggerMode == "Plc" && !_isRunning) limitMs = 200.0;

            bool hasForceAction = _plcTriggerReceived[camIndex] || _requestManualTests[camIndex] || _requestErrorTests[camIndex] || _requestOkTests[camIndex] || _pendingSaveResults[camIndex] != -1;
            if (!hasForceAction && (DateTime.Now - _lastFrameProcessTimes[camIndex]).TotalMilliseconds < limitMs) { frame.Dispose(); return; }
            if (_isProcessing[camIndex]) { frame.Dispose(); return; }
            _isProcessing[camIndex] = true; _lastFrameProcessTimes[camIndex] = DateTime.Now;

            Task.Run(() => {
                try
                {
                    _procFrameCounts[camIndex]++; bool isDebug = _isDebugTabActive && camIndex == _selectedCamIndex; double b = 0;
                    if (_appSettings.TriggerMode == "Visual" || _isRunning || _autoStartCount > 0)
                        b = _measurements[camIndex].CalculateBrightness(frame, _roi);

                    if (isDebug) _measurements[camIndex].UpdateDebugImageRealtime(frame, _saveRoi);
                    bool forceUiUpdate = false;

                    if (_requestManualTests[camIndex]) {
                        _requestManualTests[camIndex] = false;
                        int manualResult = _measurements[camIndex].Inspect(frame, _saveRoi, isDebug);
                        SafeInvoke(() => UpdateResultDisplay(camIndex, manualResult, true, b));
                        _pendingSaveResults[camIndex] = manualResult; forceUiUpdate = true;
                    }
                    if (_requestOkTests[camIndex])
                    {
                        _requestOkTests[camIndex] = false;
                        int forceOkResult = 1;
                        AppendLog($"[TEST] Cam{camIndex+1} 強制OKテスト要求を受信。");
                        var camSettings = camIndex == 0 ? _appSettings.Cam1 : _appSettings.Cam2;
                        _plc.SendResult(true, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                        if (_appSettings.TriggerMode == "Plc")
                        {
                            Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                        }
                        SafeInvoke(() => UpdateResultDisplay(camIndex, forceOkResult, true, b));
                        _pendingSaveResults[camIndex] = forceOkResult;
                        forceUiUpdate = true;
                    }

                    if (_requestErrorTests[camIndex])
                    {
                        _requestErrorTests[camIndex] = false;
                        int forceNgResult = 2;
                        AppendLog($"[TEST] Cam{camIndex+1} 強制NGテスト要求を受信。");
                        var camSettings = camIndex == 0 ? _appSettings.Cam1 : _appSettings.Cam2;
                        _plc.SendResult(false, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                        if (_appSettings.TriggerMode == "Plc")
                        {
                            Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                        }
                        SafeInvoke(() => UpdateResultDisplay(camIndex, forceNgResult, true, b));
                        _pendingSaveResults[camIndex] = forceNgResult;
                        forceUiUpdate = true;
                    }

                    UpdateStateMachine(camIndex, frame, b, isDebug);
                    if (_pendingSaveResults[camIndex] != -1) forceUiUpdate = true;
                    double uiLimitMs = _isRunning ? 66.0 : 200.0;

                    if (forceUiUpdate || (DateTime.Now - _lastUiUpdateTimes[camIndex]).TotalMilliseconds > uiLimitMs) {
                        _lastUiUpdateTimes[camIndex] = DateTime.Now;
                        SafeBeginInvoke(() => {
                            _uiFrameCounts[camIndex]++;
                            UpdateUIDisplay(camIndex, frame, b, isDebug);
                            frame.Dispose(); _isProcessing[camIndex] = false;
                        });
                    } else { frame.Dispose(); _isProcessing[camIndex] = false; }
                }
                catch { if (frame != null && !frame.IsDisposed) frame.Dispose(); _isProcessing[camIndex] = false; }
            });
        }

        private void UpdateStateMachine(int camIndex, Mat frame, double b, bool isDebug)
        {
            bool rawTriggered = (_appSettings.TriggerMode == "Plc" ? _plcTriggerReceived[camIndex] : (_triggerOnBright ? (b > _triggerThreshold) : (b < _triggerThreshold)));
            bool isReset = (_appSettings.TriggerMode == "Plc" ? false : (_triggerOnBright ? (b < _resetThreshold) : (b > _resetThreshold)));
            bool isTriggerEdge = rawTriggered && !_wasTriggeredLastFrames[camIndex];
            _wasTriggeredLastFrames[camIndex] = rawTriggered;

            var camSettings = camIndex == 0 ? _appSettings.Cam1 : _appSettings.Cam2;

            if (!_isRunning)
            {
                if (isTriggerEdge)
                {
                    if (_autoStartCount > 0)
                    {
                        if (camIndex == 0) _missedTriggerCount++;
                        if (_missedTriggerCount >= _autoStartCount) { _isRunning = true; _missedTriggerCount = 0; SafeInvoke(() => { _btnRunToggle.Text = "■ 運転停止 (STOP)"; _btnRunToggle.BackColor = Color.Salmon; }); }
                        else {
                            SafeInvoke(() => lblStateUpdate(camIndex, $"STARTING SOON... ({_missedTriggerCount}/{_autoStartCount})", Color.Orange));
                            if (_appSettings.TriggerMode == "Plc") {
                                _plcTriggerReceived[camIndex] = false;
                                _plc.SendResult(true, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                                Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                            }
                            return;
                        }
                    }
                    else {
                        if (_appSettings.TriggerMode == "Plc") {
                            _plcTriggerReceived[camIndex] = false;
                            _plc.SendResult(true, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);
                            Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
                        }
                        return;
                    }
                }
                else return;
            }

            switch (_currentStates[camIndex])
            {
                case STATE_WAITING:
                    if (rawTriggered)
                    {
                        _currentRetry = 0;
                        if (_chkEnableOuterTiltCheck != null) _measurements[camIndex].EnableOuterTiltCheck = _chkEnableOuterTiltCheck.Checked;
                        if (_chkEnableHoleCheck != null) _measurements[camIndex].EnableHoleCheck = _chkEnableHoleCheck.Checked;

                        AppendLog($"Cam{camIndex+1}: 検査開始のため前回の判定出力をクリア");
                        Task.Run(() => {
                            _plc.WriteDevice(camSettings.OkDeviceAddress, 0);
                            _plc.WriteDevice(camSettings.NgDeviceAddress, 0);
                        });

                        if (_appSettings.TriggerMode == "Plc") {
                            _plcTriggerReceived[camIndex] = false; _currentStates[camIndex] = STATE_STABILIZING; _stabilityStartTimes[camIndex] = DateTime.Now;
                            SafeInvoke(() => lblStateUpdate(camIndex, "DELAYING...", Color.Yellow));
                        }
                        else {
                            if (isTriggerEdge) { _currentStates[camIndex] = STATE_STABILIZING; _stabilityStartTimes[camIndex] = DateTime.Now; SafeInvoke(() => lblStateUpdate(camIndex, "TESTING...", Color.Yellow)); }
                        }
                    }
                    break;

                case STATE_STABILIZING:
                    if (_appSettings.TriggerMode == "Plc")
                    {
                        double targetDelay = _currentRetry == 0 ? _plcDelayMs : _retryDelayMs;
                        if ((DateTime.Now - _stabilityStartTimes[camIndex]).TotalMilliseconds > targetDelay)
                        {
                            int inspectResult = _measurements[camIndex].Inspect(frame, _saveRoi, isDebug);

                            if (inspectResult != 1 && _currentRetry >= _maxRetryCount && _measurements[camIndex].EnableOuterTiltCheck && !_measurements[camIndex].EnableHoleCheck)
                            {
                                _measurements[camIndex].EnableOuterTiltCheck = false; _measurements[camIndex].EnableHoleCheck = true;
                                inspectResult = _measurements[camIndex].Inspect(frame, _saveRoi, isDebug);
                                SafeInvoke(() => lblStateUpdate(camIndex, "FALLBACK HOLE...", Color.Orange));
                            }

                            if (inspectResult == 1 || _currentRetry >= _maxRetryCount) { ProcessInspectionResult(camIndex, inspectResult, b); _currentStates[camIndex] = STATE_COOLING; _cooldownStartTimes[camIndex] = DateTime.Now; }
                            else { _currentRetry++; _stabilityStartTimes[camIndex] = DateTime.Now; SafeInvoke(() => lblStateUpdate(camIndex, $"RETRY {_currentRetry}/{_maxRetryCount}", Color.Orange)); }
                        }
                    }
                    else
                    {
                        if (isReset) { _currentStates[camIndex] = STATE_WAITING; SafeInvoke(() => lblStateUpdate(camIndex, "READY", Color.LightGray)); }
                        else
                        {
                            double targetDelay = _currentRetry == 0 ? _stabilityDurationMs : _retryDelayMs;
                            if ((DateTime.Now - _stabilityStartTimes[camIndex]).TotalMilliseconds > targetDelay)
                            {
                                int inspectResult = _measurements[camIndex].Inspect(frame, _saveRoi, isDebug);

                                if (inspectResult != 1 && _currentRetry >= _maxRetryCount && _measurements[camIndex].EnableOuterTiltCheck && !_measurements[camIndex].EnableHoleCheck)
                                {
                                    _measurements[camIndex].EnableOuterTiltCheck = false; _measurements[camIndex].EnableHoleCheck = true;
                                    inspectResult = _measurements[camIndex].Inspect(frame, _saveRoi, isDebug);
                                    SafeInvoke(() => lblStateUpdate(camIndex, "FALLBACK HOLE...", Color.Orange));
                                }

                                if (inspectResult == 1 || _currentRetry >= _maxRetryCount) { ProcessInspectionResult(camIndex, inspectResult, b); _currentStates[camIndex] = STATE_COOLING; _cooldownStartTimes[camIndex] = DateTime.Now; }
                                else { _currentRetry++; _stabilityStartTimes[camIndex] = DateTime.Now; SafeInvoke(() => lblStateUpdate(camIndex, $"RETRY {_currentRetry}/{_maxRetryCount}", Color.Orange)); }
                            }
                        }
                    }
                    break;

                case STATE_COOLING:
                    if ((DateTime.Now - _cooldownStartTimes[camIndex]).TotalMilliseconds > _cooldownDurationMs)
                        if (_appSettings.TriggerMode == "Plc" || isReset) { _currentStates[camIndex] = STATE_WAITING; SafeInvoke(() => lblStateUpdate(camIndex, "READY", Color.LightGray)); }
                    break;
            }
        }

        private void lblStateUpdate(int camIndex, string text, Color color) {
            if (_lblBigResults[camIndex] != null && !_lblBigResults[camIndex].IsDisposed) {
                _lblBigResults[camIndex].Text = $"Cam{camIndex+1}: {text}";
                _lblBigResults[camIndex].BackColor = color;
            }
        }

        private void ProcessInspectionResult(int camIndex, int inspectResult, double brightness)
        {
            var camSettings = camIndex == 0 ? _appSettings.Cam1 : _appSettings.Cam2;
            bool isOk = (inspectResult == 1);
            AppendLog($"Cam{camIndex+1} 検査完了。結果: {(isOk ? "OK" : "NG")} (D{camSettings.WriteDeviceAddress} に送信)");
            _plc.SendResult(isOk, camSettings.OkDeviceAddress, camSettings.NgDeviceAddress);

            if (_appSettings.TriggerMode == "Plc")
            {
                AppendLog($"Cam{camIndex+1} PLCトリガ M{camSettings.ReadDeviceAddress} をクリア");
                Task.Run(() => _plc.WriteDevice(camSettings.ReadDeviceAddress, 0));
            }
            SafeInvoke(() => UpdateResultDisplay(camIndex, inspectResult, false, brightness));
            _pendingSaveResults[camIndex] = inspectResult;

            // ★分析モジュールへの登録処理
            double lastAngle = _measurements[camIndex].LastOuterAngleDeg;
            _analyzer.AddRecord(lastAngle, brightness, isOk, _logDirPath);
        }

        private void UpdateUIDisplay(int camIndex, Mat frame, double b, bool isDebug)
        {
            if (!_isUiLoaded || this.IsDisposed) return;
            using (Mat disp = new Mat())
            {
                if (frame.Channels() == 1) Cv2.CvtColor(frame, disp, ColorConversionCodes.GRAY2BGR); else frame.CopyTo(disp);
                if (_chkShowOverlay.Checked)
                {
                    Cv2.Rectangle(disp, _roi, Scalar.Yellow, 2);
                    Cv2.Rectangle(disp, _measurements[camIndex].BtmMeasureRoi, new Scalar(0, 150, 150), 2);

                    Cv2.Rectangle(disp, _measurements[camIndex].BtmInnerLeftRoi, new Scalar(0, 0, 255), 2);
                    Cv2.Rectangle(disp, _measurements[camIndex].BtmInnerRightRoi, new Scalar(0, 0, 255), 2);

                    if (_measurements[camIndex].EnableOuterTiltCheck) { Cv2.Rectangle(disp, _measurements[camIndex].TiltLeftRoi, Scalar.Cyan, 2); Cv2.Rectangle(disp, _measurements[camIndex].TiltRightRoi, Scalar.Cyan, 2); }
                    if (_measurements[camIndex].EnableHoleCheck) { Cv2.Rectangle(disp, _measurements[camIndex].HolesRoi, Scalar.Orange, 2); }
                    if (_measurements[camIndex].EnableJigCheck) { Cv2.Rectangle(disp, _measurements[camIndex].JigLeftRoi, Scalar.Yellow, 2); Cv2.Rectangle(disp, _measurements[camIndex].JigRightRoi, Scalar.Yellow, 2); }
                    Cv2.Rectangle(disp, _saveRoi, Scalar.LightSkyBlue, 1);
                    Cv2.Line(disp, new CvPoint(0, _measurements[camIndex].SplitBoundaryY), new CvPoint(disp.Width, _measurements[camIndex].SplitBoundaryY), Scalar.LightGray, 2);
                    Cv2.Line(disp, new CvPoint(_measurements[camIndex].SplitBoundaryX, 0), new CvPoint(_measurements[camIndex].SplitBoundaryX, disp.Height), Scalar.LightGray, 2);
                    _measurements[camIndex].DrawOverlay(disp);
                }

                if (_pendingSaveResults[camIndex] != -1) { if (_saveMode == 2 || (_saveMode == 1 && _pendingSaveResults[camIndex] != 1)) SaveInspectionImage(disp, _pendingSaveResults[camIndex]); _pendingSaveResults[camIndex] = -1; }
                Bitmap bmp = BitmapConverter.ToBitmap(disp); Image old = _pictureBoxes[camIndex].Image; _pictureBoxes[camIndex].Image = bmp; old?.Dispose();
            }
            if (isDebug && camIndex == _selectedCamIndex) { using (Mat binImg = new Mat()) { _measurements[camIndex].GetDebugImage(binImg); if (!binImg.Empty()) { Bitmap bmpD = BitmapConverter.ToBitmap(binImg); Image oldD = _pictureBoxDebug.Image; _pictureBoxDebug.Image = bmpD; oldD?.Dispose(); } } }
            if (_lblCurrentHoleDistPx != null && !_lblCurrentHoleDistPx.IsDisposed && _measurements[camIndex].LastHoleDistancePx > 0 && camIndex == _selectedCamIndex) _lblCurrentHoleDistPx.Text = "現在の穴/エッジ間距離: " + _measurements[camIndex].LastHoleDistancePx.ToString("F1") + " px";

            if (_lblStatuses[camIndex] != null && !_lblStatuses[camIndex].IsDisposed) {
                if (!_isRunning) { _lblStatuses[camIndex].Text = $"Cam{camIndex+1} Status: STOPPED"; _lblStatuses[camIndex].ForeColor = Color.Red; }
                else {
                    _lblStatuses[camIndex].Text = $"Cam{camIndex+1} Status: " + (_currentStates[camIndex] == 0 ? "WAITING" : (_currentStates[camIndex] == 1 ? "STABILIZING" : "COOLING"));
                    _lblStatuses[camIndex].ForeColor = _currentStates[camIndex] == 0 ? Color.Gray : (_currentStates[camIndex] == 1 ? Color.Goldenrod : Color.LimeGreen);
                }
            }
            if (_lblBrightnesses[camIndex] != null && !_lblBrightnesses[camIndex].IsDisposed) _lblBrightnesses[camIndex].Text = $"Cam{camIndex+1} Brightness: " + b.ToString("F1");
            if (_lblFpsList[camIndex] != null && !_lblFpsList[camIndex].IsDisposed) _lblFpsList[camIndex].Text = _currentFpsTexts[camIndex];
        }

        private void SaveInspectionImage(Mat img, int res)
        {
            try { Mat imgToSave = img.Clone(); Task.Run(() => { try { string dir = Path.Combine(_logDirPath, DateTime.Now.ToString("yyyyMMdd")); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); string resStr = (res == 1) ? "OK" : "NG"; string fileName = string.Format("{0:HHmmss_fff}_{1}.jpg", DateTime.Now, resStr); string path = Path.Combine(dir, fileName); CvRect crop = _saveRoi & new CvRect(0, 0, imgToSave.Width, imgToSave.Height); using (Mat cropped = new Mat(imgToSave, crop)) using (Mat resized = new Mat()) { Cv2.Resize(cropped, resized, new CvSize(cropped.Width / 2, cropped.Height / 2)); var p = new ImageEncodingParam(ImwriteFlags.JpegQuality, 65); Cv2.ImWrite(path, resized, p); } } catch { } finally { if (imgToSave != null && !imgToSave.IsDisposed) imgToSave.Dispose(); } }); } catch { }
        }

        private void UpdateResultDisplay(int camIndex, int res, bool manual, double brightness = 0.0)
        {
            if (_lblBigResults[camIndex] == null || _lblBigResults[camIndex].IsDisposed) return;
            _lblBigResults[camIndex].Text = $"Cam{camIndex+1}: " + (res == 1 ? "OK" : "NG");
            _lblBigResults[camIndex].BackColor = res == 1 ? Color.LimeGreen : Color.Red;
            if (!manual)
            {
                _totalCount++;
                if (res == 1) _okCount++; else _ngCount++;
                UpdateCounterDisplay();
                AppendDailyLog(res, brightness);
            }
        }

        private void UpdateCounterDisplay() { if (_lblTotal == null || _lblTotal.IsDisposed) return; _lblTotal.Text = "総検査数 : " + _totalCount; _lblOk.Text = "良品 (OK): " + _okCount; _lblNg.Text = "不良 (NG): " + _ngCount; }

        private void CopySettingsToMeasurement(MeasurementCore m)
        {
            if (m == null) return;

            if (_chkEnableJigCheck != null) m.EnableJigCheck = _chkEnableJigCheck.Checked;
            if (_chkEnableOuterTiltCheck != null) m.EnableOuterTiltCheck = _chkEnableOuterTiltCheck.Checked;
            if (_chkEnableHoleCheck != null) m.EnableHoleCheck = _chkEnableHoleCheck.Checked;

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
            if (_isLoadingConfig || _isUpdatingUI) return;
            _isUpdatingUI = true;

            var m = _measurements[_selectedCamIndex];

            // Inspection Tab
            if (_chkEnableJigCheck != null) _chkEnableJigCheck.Checked = m.EnableJigCheck;
            if (_chkEnableOuterTiltCheck != null) _chkEnableOuterTiltCheck.Checked = m.EnableOuterTiltCheck;
            if (_chkEnableHoleCheck != null) _chkEnableHoleCheck.Checked = m.EnableHoleCheck;

            if (_nudTiltLX != null) _nudTiltLX.Value = m.TiltLeftRoi.X;
            if (_nudTiltLY != null) _nudTiltLY.Value = m.TiltLeftRoi.Y;
            if (_nudTiltLW != null) _nudTiltLW.Value = m.TiltLeftRoi.Width;
            if (_nudTiltLH != null) _nudTiltLH.Value = m.TiltLeftRoi.Height;

            if (_nudTiltRX != null) _nudTiltRX.Value = m.TiltRightRoi.X;
            if (_nudTiltRY != null) _nudTiltRY.Value = m.TiltRightRoi.Y;
            if (_nudTiltRW != null) _nudTiltRW.Value = m.TiltRightRoi.Width;
            if (_nudTiltRH != null) _nudTiltRH.Value = m.TiltRightRoi.Height;

            if (_nudOuterTargetX != null) _nudOuterTargetX.Value = (decimal)m.TargetOuterXOffsetMm;
            if (_nudOuterOffsetX != null) _nudOuterOffsetX.Value = (decimal)m.OuterOffsetToleranceMm;
            if (_nudOuterTargetA != null) _nudOuterTargetA.Value = (decimal)m.TargetOuterAngleDeg;
            if (_nudOuterOffsetA != null) _nudOuterOffsetA.Value = (decimal)m.OuterAngleToleranceDeg;

            if (_nudBtmRoiX != null) _nudBtmRoiX.Value = m.BtmMeasureRoi.X;
            if (_nudBtmRoiY != null) _nudBtmRoiY.Value = m.BtmMeasureRoi.Y;
            if (_nudBtmRoiW != null) _nudBtmRoiW.Value = m.BtmMeasureRoi.Width;
            if (_nudBtmRoiH != null) _nudBtmRoiH.Value = m.BtmMeasureRoi.Height;

            if (_nudBtmInnerLX != null) _nudBtmInnerLX.Value = m.BtmInnerLeftRoi.X;
            if (_nudBtmInnerLY != null) _nudBtmInnerLY.Value = m.BtmInnerLeftRoi.Y;
            if (_nudBtmInnerLW != null) _nudBtmInnerLW.Value = m.BtmInnerLeftRoi.Width;
            if (_nudBtmInnerLH != null) _nudBtmInnerLH.Value = m.BtmInnerLeftRoi.Height;

            if (_nudBtmInnerRX != null) _nudBtmInnerRX.Value = m.BtmInnerRightRoi.X;
            if (_nudBtmInnerRY != null) _nudBtmInnerRY.Value = m.BtmInnerRightRoi.Y;
            if (_nudBtmInnerRW != null) _nudBtmInnerRW.Value = m.BtmInnerRightRoi.Width;
            if (_nudBtmInnerRH != null) _nudBtmInnerRH.Value = m.BtmInnerRightRoi.Height;

            if (_nudHolesX != null) _nudHolesX.Value = m.HolesRoi.X;
            if (_nudHolesY != null) _nudHolesY.Value = m.HolesRoi.Y;
            if (_nudHolesW != null) _nudHolesW.Value = m.HolesRoi.Width;
            if (_nudHolesH != null) _nudHolesH.Value = m.HolesRoi.Height;
            if (_nudMinHoleArea != null) _nudMinHoleArea.Value = m.MinHoleArea;
            if (_nudMaxHoleArea != null) _nudMaxHoleArea.Value = m.MaxHoleArea;
            if (_nudMinCircularity != null) _nudMinCircularity.Value = (decimal)m.MinCircularity;
            if (_nudTargetXOffset != null) _nudTargetXOffset.Value = (decimal)m.TargetXOffsetMm;
            if (_nudOffsetTolerance != null) _nudOffsetTolerance.Value = (decimal)m.OffsetToleranceMm;
            if (_nudTargetAngle != null) _nudTargetAngle.Value = (decimal)m.TargetAngleDeg;
            if (_nudAngleTolerance != null) _nudAngleTolerance.Value = (decimal)m.AngleToleranceDeg;

            if (_nudJigLX != null) _nudJigLX.Value = m.JigLeftRoi.X;
            if (_nudJigLY != null) _nudJigLY.Value = m.JigLeftRoi.Y;
            if (_nudJigLW != null) _nudJigLW.Value = m.JigLeftRoi.Width;
            if (_nudJigLH != null) _nudJigLH.Value = m.JigLeftRoi.Height;

            if (_nudJigRX != null) _nudJigRX.Value = m.JigRightRoi.X;
            if (_nudJigRY != null) _nudJigRY.Value = m.JigRightRoi.Y;
            if (_nudJigRW != null) _nudJigRW.Value = m.JigRightRoi.Width;
            if (_nudJigRH != null) _nudJigRH.Value = m.JigRightRoi.Height;

            if (_nudJigTarget != null) _nudJigTarget.Value = (decimal)m.TargetJigDistanceMm;
            if (_nudJigTolerance != null) _nudJigTolerance.Value = (decimal)m.JigToleranceMm;
            if (_nudPixelToMm != null) _nudPixelToMm.Value = (decimal)m.PixelToMmRatio;

            // Debug Tab
            if (_nudThreshOuterL != null) _nudThreshOuterL.Value = m.ThreshOuterL;
            if (_nudThreshOuterR != null) _nudThreshOuterR.Value = m.ThreshOuterR;
            if (_nudThreshBtmInnerL != null) _nudThreshBtmInnerL.Value = m.ThreshBtmInnerL;
            if (_nudThreshBtmInnerR != null) _nudThreshBtmInnerR.Value = m.ThreshBtmInnerR;
            if (_nudSplitX != null) _nudSplitX.Value = m.SplitBoundaryX;
            if (_nudSplitY != null) _nudSplitY.Value = m.SplitBoundaryY;
            if (_nudThreshTL != null) _nudThreshTL.Value = m.ThreshTopLeft;
            if (_nudThreshTR != null) _nudThreshTR.Value = m.ThreshTopRight;
            if (_nudThreshBL != null) _nudThreshBL.Value = m.ThreshBtmLeft;
            if (_nudThreshBR != null) _nudThreshBR.Value = m.ThreshBtmRight;

            if (_lblCurrentHoleDistPx != null && !_lblCurrentHoleDistPx.IsDisposed)
                _lblCurrentHoleDistPx.Text = "現在の穴/エッジ間距離: " + m.LastHoleDistancePx.ToString("F1") + " px";

            _isUpdatingUI = false;
        }

        private void UpdateSettingsFromUI()
        {
            if (_isLoadingConfig || _isUpdatingUI) return;

            _triggerThreshold = (double)_nudTriggerThreshold.Value; _stabilityDurationMs = (int)_nudStabilityDuration.Value; _resetThreshold = (double)_nudResetThreshold.Value;
            _plcDelayMs = (int)_nudPlcDelayMs.Value; _maxRetryCount = (int)_nudRetryCount.Value; _retryDelayMs = (int)_nudRetryDelayMs.Value;
            _autoStartCount = (int)_nudAutoStartCount.Value; _roi = new CvRect((int)_nudRoiX.Value, (int)_nudRoiY.Value, (int)_nudRoiW.Value, (int)_nudRoiH.Value);
            _saveRoi = new CvRect((int)_nudSaveRoiX.Value, (int)_nudSaveRoiY.Value, (int)_nudSaveRoiW.Value, (int)_nudSaveRoiH.Value); _logKeepDays = (int)_nudLogKeepDays.Value;

            CopySettingsToMeasurement(_measurements[_selectedCamIndex]);
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

                // For backward compatibility, if Cam0_ isn't found, try without prefix.
                // We'll load both cameras.

                for (int c = 0; c < 2; c++)
                {
                    string pfx = $"Cam{c}_";
                    var m = _measurements[c];

                    var enableJigCheck = d.TryGetValue(pfx + "EnableJigCheck", out var ej) ? bool.Parse(ej) : (d.TryGetValue("EnableJigCheck", out ej) ? bool.Parse(ej) : defM.EnableJigCheck);
                    bool enableOuterTiltCheck = defM.EnableOuterTiltCheck;
                    bool enableHoleCheck = defM.EnableHoleCheck;

                    if (d.TryGetValue(pfx + "UseOuterEdgeForTilt", out var uoe) || d.TryGetValue("UseOuterEdgeForTilt", out uoe))
                    {
                        bool useOuter = bool.Parse(uoe); enableOuterTiltCheck = useOuter; enableHoleCheck = !useOuter;
                    }
                    else
                    {
                        enableOuterTiltCheck = d.TryGetValue(pfx + "EnableOuterTiltCheck", out var eot) ? bool.Parse(eot) : (d.TryGetValue("EnableOuterTiltCheck", out eot) ? bool.Parse(eot) : defM.EnableOuterTiltCheck);
                        enableHoleCheck = d.TryGetValue(pfx + "EnableHoleCheck", out var ehc) ? bool.Parse(ehc) : (d.TryGetValue("EnableHoleCheck", out ehc) ? bool.Parse(ehc) : defM.EnableHoleCheck);
                    }

                    m.EnableJigCheck = enableJigCheck;
                    m.EnableOuterTiltCheck = enableOuterTiltCheck;
                    m.EnableHoleCheck = enableHoleCheck;

                    int GetCamI(string key, int defVal) => d.ContainsKey(pfx + key) ? GetI(pfx + key, defVal) : GetI(key, defVal);
                    double GetCamD(string key, double defVal) => d.ContainsKey(pfx + key) ? GetD(pfx + key, defVal) : GetD(key, defVal);

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
                }

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

                    for (int c = 0; c < 2; c++)
                    {
                        var m = _measurements[c];
                        string pfx = $"Cam{c}_";

                        sw.WriteLine(pfx + "EnableJigCheck=" + m.EnableJigCheck);
                        sw.WriteLine(pfx + "EnableOuterTiltCheck=" + m.EnableOuterTiltCheck);
                        sw.WriteLine(pfx + "EnableHoleCheck=" + m.EnableHoleCheck);

                        sw.WriteLine(pfx + "TiltLX=" + m.TiltLeftRoi.X); sw.WriteLine(pfx + "TiltLY=" + m.TiltLeftRoi.Y); sw.WriteLine(pfx + "TiltLW=" + m.TiltLeftRoi.Width); sw.WriteLine(pfx + "TiltLH=" + m.TiltLeftRoi.Height);
                        sw.WriteLine(pfx + "TiltRX=" + m.TiltRightRoi.X); sw.WriteLine(pfx + "TiltRY=" + m.TiltRightRoi.Y); sw.WriteLine(pfx + "TiltRW=" + m.TiltRightRoi.Width); sw.WriteLine(pfx + "TiltRH=" + m.TiltRightRoi.Height);
                        sw.WriteLine(pfx + "ThreshOuterL=" + m.ThreshOuterL); sw.WriteLine(pfx + "ThreshOuterR=" + m.ThreshOuterR);

                        sw.WriteLine(pfx + "ThreshBtmInnerL=" + m.ThreshBtmInnerL); sw.WriteLine(pfx + "ThreshBtmInnerR=" + m.ThreshBtmInnerR);

                        sw.WriteLine(pfx + "TargetOuterXOffsetMm=" + m.TargetOuterXOffsetMm); sw.WriteLine(pfx + "OuterOffsetToleranceMm=" + m.OuterOffsetToleranceMm);
                        sw.WriteLine(pfx + "TargetOuterAngleDeg=" + m.TargetOuterAngleDeg); sw.WriteLine(pfx + "OuterAngleToleranceDeg=" + m.OuterAngleToleranceDeg);

                        sw.WriteLine(pfx + "BtmRoiX=" + m.BtmMeasureRoi.X); sw.WriteLine(pfx + "BtmRoiY=" + m.BtmMeasureRoi.Y); sw.WriteLine(pfx + "BtmRoiW=" + m.BtmMeasureRoi.Width); sw.WriteLine(pfx + "BtmRoiH=" + m.BtmMeasureRoi.Height);
                        sw.WriteLine(pfx + "BtmInnerLX=" + m.BtmInnerLeftRoi.X); sw.WriteLine(pfx + "BtmInnerLY=" + m.BtmInnerLeftRoi.Y); sw.WriteLine(pfx + "BtmInnerLW=" + m.BtmInnerLeftRoi.Width); sw.WriteLine(pfx + "BtmInnerLH=" + m.BtmInnerLeftRoi.Height);
                        sw.WriteLine(pfx + "BtmInnerRX=" + m.BtmInnerRightRoi.X); sw.WriteLine(pfx + "BtmInnerRY=" + m.BtmInnerRightRoi.Y); sw.WriteLine(pfx + "BtmInnerRW=" + m.BtmInnerRightRoi.Width); sw.WriteLine(pfx + "BtmInnerRH=" + m.BtmInnerRightRoi.Height);

                        sw.WriteLine(pfx + "HolesX=" + m.HolesRoi.X); sw.WriteLine(pfx + "HolesY=" + m.HolesRoi.Y); sw.WriteLine(pfx + "HolesW=" + m.HolesRoi.Width); sw.WriteLine(pfx + "HolesH=" + m.HolesRoi.Height);
                        sw.WriteLine(pfx + "MinHoleArea=" + m.MinHoleArea); sw.WriteLine(pfx + "MaxHoleArea=" + m.MaxHoleArea);
                        sw.WriteLine(pfx + "MinCirc=" + m.MinCircularity);
                        sw.WriteLine(pfx + "SplitBoundaryX=" + m.SplitBoundaryX); sw.WriteLine(pfx + "SplitBoundaryY=" + m.SplitBoundaryY);
                        sw.WriteLine(pfx + "ThreshTL=" + m.ThreshTopLeft); sw.WriteLine(pfx + "ThreshTR=" + m.ThreshTopRight);
                        sw.WriteLine(pfx + "ThreshBL=" + m.ThreshBtmLeft); sw.WriteLine(pfx + "ThreshBR=" + m.ThreshBtmRight);

                        sw.WriteLine(pfx + "JigLX=" + m.JigLeftRoi.X); sw.WriteLine(pfx + "JigLY=" + m.JigLeftRoi.Y); sw.WriteLine(pfx + "JigLW=" + m.JigLeftRoi.Width); sw.WriteLine(pfx + "JigLH=" + m.JigLeftRoi.Height);
                        sw.WriteLine(pfx + "JigRX=" + m.JigRightRoi.X); sw.WriteLine(pfx + "JigRY=" + m.JigRightRoi.Y); sw.WriteLine(pfx + "JigRW=" + m.JigRightRoi.Width); sw.WriteLine(pfx + "JigRH=" + m.JigRightRoi.Height);

                        sw.WriteLine(pfx + "JigTargetMm=" + m.TargetJigDistanceMm); sw.WriteLine(pfx + "JigTolMm=" + m.JigToleranceMm);
                        sw.WriteLine(pfx + "PixelToMmRatio=" + m.PixelToMmRatio);
                        sw.WriteLine(pfx + "TargetXOffsetMm=" + m.TargetXOffsetMm); sw.WriteLine(pfx + "OffsetToleranceMm=" + m.OffsetToleranceMm);
                        sw.WriteLine(pfx + "TargetAngleDeg=" + m.TargetAngleDeg); sw.WriteLine(pfx + "AngleToleranceDeg=" + m.AngleToleranceDeg);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("設定ファイルの保存に失敗しました。\n\n" + ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}