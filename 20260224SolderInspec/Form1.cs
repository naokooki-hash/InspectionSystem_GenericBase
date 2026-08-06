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

        private TeliCamera _camera;
        private MeasurementCore _measurement;
        private PlcCommunicator _plc;
        private AppSettings _appSettings;
        private ProductionAnalyzer _analyzer = new ProductionAnalyzer();

        private PictureBox _pictureBox, _pictureBoxDebug;
        private TabControl _tabControl;
        private TextBox _txtLog;

        private Label _lblStatus, _lblBrightness, _lblFps, _lblBigResult, _lblTotal, _lblOk, _lblNg, _lblCurrentHoleDistPx;

        private CheckBox _chkShowOverlay, _chkEnableJigCheck;
        private CheckBox _chkEnableOuterTiltCheck, _chkEnableHoleCheck;

        private ComboBox _cmbTriggerMode, _cmbSaveMode;

        private Button _btnRunToggle;
        private bool _isRunning = false;
        private bool _requestErrorTest = false;
        private bool _requestOkTest = false; // ★テスト用: 強制OKフラグ

        private NumericUpDown _nudTriggerThreshold, _nudStabilityDuration, _nudResetThreshold;
        private NumericUpDown _nudRoiX, _nudRoiY, _nudRoiW, _nudRoiH;
        private NumericUpDown _nudSaveRoiX, _nudSaveRoiY, _nudSaveRoiW, _nudSaveRoiH;

        private NumericUpDown _nudLogKeepDays;
        private int _logKeepDays = 30;

        private NumericUpDown _nudAutoStartCount;
        private int _autoStartCount = 3;
        private int _missedTriggerCount = 0;
        private bool _wasTriggeredLastFrame = false;

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

        private int _currentState = STATE_WAITING;
        private DateTime _stabilityStartTime, _cooldownStartTime;
        private int _cooldownDurationMs = 500;

        private CvRect _roi = new CvRect(300, 200, 100, 100);
        private CvRect _saveRoi = new CvRect(100, 50, 440, 380);

        private bool _requestManualTest = false, _isProcessing = false, _isLoadingConfig = false, _isUiLoaded = false;
        private bool _isMonitoring = false, _plcTriggerReceived = false;

        private int _totalCount = 0, _okCount = 0, _ngCount = 0, _saveMode = 0, _stabilityDurationMs = 300, _pendingSaveResult = -1;
        private bool _triggerOnBright = true;
        private double _triggerThreshold = 100.0, _resetThreshold = 50.0;
        private string _logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        private DateTime _lastUiUpdateTime = DateTime.MinValue;
        private DateTime _lastFrameProcessTime = DateTime.MinValue;
        private bool _isDebugTabActive = false;

        private int _camFrameCount = 0, _procFrameCount = 0, _uiFrameCount = 0;
        private DateTime _lastFpsTime = DateTime.Now;
        private string _currentFpsText = "FPS: --";

        public Form1()
        {
            _appSettings = AppSettings.Load();
            _camera = new TeliCamera();
            _measurement = new MeasurementCore();
            _plc = new PlcCommunicator(_appSettings);

            InitializeCustomUI();
            _camera.OnFrameCaptured += Camera_OnFrameCaptured;

            _plc.OnLog += (msg, isErr) => AppendLog(msg, isErr);

            LoadConfig();

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;

            AppendLog("アプリケーションを起動しました。");
        }

        private void InitializeCustomUI()
        {
            this.Text = "Punching Metal Auto Inspection System (Bulletproof Dual-Engine)";
            this.Size = new Size(1280, 820);
            this.StartPosition = FormStartPosition.CenterScreen;

            _pictureBox = new PictureBox { Location = new Point(10, 10), Size = new Size(640, 480), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(_pictureBox);

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

            int px = 660;
            _lblStatus = new Label { Text = "Status: STOPPED", Location = new Point(px, 10), AutoSize = true, Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold), ForeColor = Color.Red };
            _lblBrightness = new Label { Text = "Brightness: 0.0", Location = new Point(px, 40), AutoSize = true, Font = new Font(this.Font.FontFamily, 12) };
            _lblFps = new Label { Text = "FPS: --", Location = new Point(px + 180, 40), AutoSize = true, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold), ForeColor = Color.Blue };
            this.Controls.Add(_lblStatus); this.Controls.Add(_lblBrightness); this.Controls.Add(_lblFps);

            _tabControl = new TabControl { Location = new Point(px, 80), Size = new Size(580, 680), Font = new Font(this.Font.FontFamily, 10) };
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
                if (_isRunning) { _btnRunToggle.Text = "■ 運転停止 (STOP)"; _btnRunToggle.BackColor = Color.Salmon; _currentState = STATE_WAITING; SafeInvoke(() => lblStateUpdate("READY", Color.LightGray)); }
                else { _btnRunToggle.Text = "▶ 運転開始 (START)"; _btnRunToggle.BackColor = Color.LightGreen; _currentState = STATE_WAITING; SafeInvoke(() => lblStateUpdate("STOPPED", Color.DarkGray)); }
            };
            tab.Controls.Add(_btnRunToggle); y += 75;

            _chkShowOverlay = new CheckBox { Text = "計測パラメータを表示する", Location = new Point(10, y), AutoSize = true, Checked = true };
            tab.Controls.Add(_chkShowOverlay); y += 30;

            _lblBigResult = new Label { Text = "STOPPED", Location = new Point(10, y), Size = new Size(540, 80), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 36, FontStyle.Bold), BackColor = Color.DarkGray };
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
            btnTest.Click += (s, e) => { _requestManualTest = true; _requestOkTest = false; _requestErrorTest = false; }; tab.Controls.Add(btnTest); y += 40;
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
            btnTestOk.Click += (s, e) => { _requestOkTest = true; _requestErrorTest = false; }; tab.Controls.Add(btnTestOk);

            Button btnTestNg = new Button { Text = "強制NGテスト", Location = new Point(290, y), Size = new Size(260, 35), BackColor = Color.Orange, Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold) };
            btnTestNg.Click += (s, e) => { _requestErrorTest = true; _requestOkTest = false; }; tab.Controls.Add(btnTestNg); y += 45;

            if (_txtLog != null) {
                _txtLog.Location = new Point(10, y);
                _txtLog.Size = new Size(540, 250);
                tab.Controls.Add(_txtLog);
                y += 260;
            }
        }

        private void InitializeInspectionTab(TabPage tab)
        {
            int y = 10, lw = 160, cw = 100, lh = 28;
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
            _chkEnableJigCheck = new CheckBox { Text = "エッジ間距離測定を有効にする", Location = new Point(10, y), AutoSize = true, Checked = _measurement.EnableJigCheck };
            _chkEnableJigCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(_chkEnableJigCheck); y += 22;
            _chkEnableOuterTiltCheck = new CheckBox { Text = "【モードA】 外形エッジで製品の傾き・ズレを検査する", Location = new Point(10, y), AutoSize = true, Checked = _measurement.EnableOuterTiltCheck, ForeColor = Color.Teal };
            _chkEnableOuterTiltCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(_chkEnableOuterTiltCheck); y += 22;
            _chkEnableHoleCheck = new CheckBox { Text = "【モードB】 穴で製品の傾き・ズレを検査する", Location = new Point(10, y), AutoSize = true, Checked = _measurement.EnableHoleCheck, ForeColor = Color.Blue };
            _chkEnableHoleCheck.CheckedChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(_chkEnableHoleCheck); y += 28;

            tab.Controls.Add(new Label { Text = "--- 【モードA】 外形エッジ パラメータ ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Teal }); y += 22;
            AddRect("左エッジROI(青):", ref _nudTiltLX, ref _nudTiltLY, ref _nudTiltLW, ref _nudTiltLH, _measurement.TiltLeftRoi);
            AddRect("右エッジROI(青):", ref _nudTiltRX, ref _nudTiltRY, ref _nudTiltRW, ref _nudTiltRH, _measurement.TiltRightRoi);
            AddN("目標 Xずれ(mm):", ref _nudOuterTargetX, -100, 100, (decimal)_measurement.TargetOuterXOffsetMm, 2, 0.1M);
            AddN("Xずれ許容(mm):", ref _nudOuterOffsetX, 0, 50, (decimal)_measurement.OuterOffsetToleranceMm, 2, 0.1M);
            AddN("目標 Θ(deg):", ref _nudOuterTargetA, -180, 180, (decimal)_measurement.TargetOuterAngleDeg, 2, 0.1M);
            AddN("Θ許容(deg):", ref _nudOuterOffsetA, 0, 90, (decimal)_measurement.OuterAngleToleranceDeg, 2, 0.1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- 【モードB】 穴 パラメータ ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddRect("基準穴 ROI:", ref _nudHolesX, ref _nudHolesY, ref _nudHolesW, ref _nudHolesH, _measurement.HolesRoi);
            AddN("穴 最小面積:", ref _nudMinHoleArea, 0, 100000, _measurement.MinHoleArea);
            AddN("穴 最大面積:", ref _nudMaxHoleArea, 0, 1000000, _measurement.MaxHoleArea);
            AddN("真円度しきい値:", ref _nudMinCircularity, 0, 1, (decimal)_measurement.MinCircularity, 2, 0.05M);
            AddN("目標 Xずれ(mm):", ref _nudTargetXOffset, -100, 100, (decimal)_measurement.TargetXOffsetMm, 2, 0.1M);
            AddN("Xずれ許容(mm):", ref _nudOffsetTolerance, 0, 50, (decimal)_measurement.OffsetToleranceMm, 2, 0.1M);
            AddN("目標 Θ(deg):", ref _nudTargetAngle, -180, 180, (decimal)_measurement.TargetAngleDeg, 2, 0.1M);
            AddN("Θ許容(deg):", ref _nudAngleTolerance, 0, 90, (decimal)_measurement.AngleToleranceDeg, 2, 0.1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- エッジ間距離 測定設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Olive }); y += 22;
            AddRect("左エッジ ROI:", ref _nudJigLX, ref _nudJigLY, ref _nudJigLW, ref _nudJigLH, _measurement.JigLeftRoi);
            AddRect("右エッジ ROI:", ref _nudJigRX, ref _nudJigRY, ref _nudJigRW, ref _nudJigRH, _measurement.JigRightRoi);
            AddN("エッジ目標距離(mm):", ref _nudJigTarget, 0, 500, (decimal)_measurement.TargetJigDistanceMm, 2, 0.1M);
            AddN("エッジ許容誤差(mm):", ref _nudJigTolerance, 0, 50, (decimal)_measurement.JigToleranceMm, 2, 0.1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- 下部測定 ROI ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkGoldenrod }); y += 22;
            AddRect("Btm線 ROI(黄):", ref _nudBtmRoiX, ref _nudBtmRoiY, ref _nudBtmRoiW, ref _nudBtmRoiH, _measurement.BtmMeasureRoi);
            AddRect("Btm内側左 ROI(赤):", ref _nudBtmInnerLX, ref _nudBtmInnerLY, ref _nudBtmInnerLW, ref _nudBtmInnerLH, _measurement.BtmInnerLeftRoi);
            AddRect("Btm内側右 ROI(赤):", ref _nudBtmInnerRX, ref _nudBtmInnerRY, ref _nudBtmInnerRW, ref _nudBtmInnerRH, _measurement.BtmInnerRightRoi); y += 10;

            tab.Controls.Add(new Label { Text = "--- キャリブレーション (Pixel/mm比率) ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkOrange }); y += 22;
            _lblCurrentHoleDistPx = new Label { Text = "現在の穴/エッジ間距離: 0.0 px", Location = new Point(10, y), Size = new Size(300, 20), ForeColor = Color.DarkOrange, Font = new Font(this.Font, FontStyle.Bold) };
            tab.Controls.Add(_lblCurrentHoleDistPx); y += lh;
            _nudActualWidthMm = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0.1M, Maximum = 500, Value = 50, DecimalPlaces = 2, Increment = 0.1M };
            tab.Controls.Add(new Label { Text = "実測の距離(mm):", Location = new Point(10, y + 2), Size = new Size(lw, 20) }); tab.Controls.Add(_nudActualWidthMm); y += lh;
            _btnCalcRatio = new Button { Text = "比率を自動計算", Location = new Point(10, y), Size = new Size(360, 30), BackColor = Color.LightYellow };
            _btnCalcRatio.Click += (s, e) => {
                if (_measurement.LastHoleDistancePx <= 0) { MessageBox.Show("先にテスト実行して検出させてください。"); return; }
                _nudPixelToMm.Value = _nudActualWidthMm.Value / (decimal)_measurement.LastHoleDistancePx;
                MessageBox.Show("更新しました。各種目標(mm)を再設定してください。");
            }; tab.Controls.Add(_btnCalcRatio); y += 45;
            AddN("Pixel->mm比率:", ref _nudPixelToMm, 0.0001M, 1, (decimal)_measurement.PixelToMmRatio, 5, 0.001M);
        }

        private void InitializeDebugTab(TabPage tab)
        {
            int y = 10, lw = 150, cw = 100, lh = 28;
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0, decimal step = 1M)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = step };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(n); y += lh;
            }

            _pictureBoxDebug = new PictureBox { Location = new Point(10, y), Size = new Size(380, 280), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(_pictureBoxDebug); y += 300;

            tab.Controls.Add(new Label { Text = "--- ★外形エッジ(青枠) 専用閾値調整 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Teal }); y += 22;
            AddN("左エッジ(青) 閾値:", ref _nudThreshOuterL, 0, 255, _measurement.ThreshOuterL, 0, 1M);
            AddN("右エッジ(青) 閾値:", ref _nudThreshOuterR, 0, 255, _measurement.ThreshOuterR, 0, 1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- ★BTM内側(赤枠) 専用閾値調整 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkRed }); y += 22;
            AddN("左内側(赤) 閾値:", ref _nudThreshBtmInnerL, 0, 255, _measurement.ThreshBtmInnerL, 0, 1M);
            AddN("右内側(赤) 閾値:", ref _nudThreshBtmInnerR, 0, 255, _measurement.ThreshBtmInnerR, 0, 1M); y += 10;

            tab.Controls.Add(new Label { Text = "--- ★4分割 二値化 閾値調整 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddN("上下分割 Y境界線:", ref _nudSplitY, 0, 3000, _measurement.SplitBoundaryY, 0, 1M);
            AddN("左右分割 X境界線:", ref _nudSplitX, 0, 3000, _measurement.SplitBoundaryX, 0, 1M);
            AddN("左上 (TL) 閾値:", ref _nudThreshTL, 0, 255, _measurement.ThreshTopLeft, 0, 1M);
            AddN("右上 (TR) 閾値:", ref _nudThreshTR, 0, 255, _measurement.ThreshTopRight, 0, 1M);
            AddN("左下 (BL) 閾値:", ref _nudThreshBL, 0, 255, _measurement.ThreshBtmLeft, 0, 1M);
            AddN("右下 (BR) 閾値:", ref _nudThreshBR, 0, 255, _measurement.ThreshBtmRight, 0, 1M);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _isUiLoaded = true;
            if (_camera.Initialize()) _camera.StartCapture();
            _ = MonitorPlcTriggerAsync();
            Task.Run(() => DeleteOldLogs());
            RestoreDailyCounter();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e) { _isMonitoring = false; _isUiLoaded = false; _camera.StopCapture(); _camera.Dispose(); _plc.Disconnect(); SaveConfig(); }

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
                        int triggerValue = await Task.Run(() => _plc.ReadDevice(_appSettings.ReadDeviceAddress));
                        if (triggerValue == 1)
                        {
                            if (!_plcTriggerReceived)
                            {
                                AppendLog($"D{_appSettings.ReadDeviceAddress} より検査トリガを受信しました");
                            }
                            _plcTriggerReceived = true;
                        }
                    }
                }
                else
                {
                    // 未接続の場合は再接続を試みる前に少し待機
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

        private void Camera_OnFrameCaptured(object sender, Mat frame)
        {
            if (!_isUiLoaded || this.IsDisposed || frame == null || frame.Empty()) return;

            _camFrameCount++;
            if ((DateTime.Now - _lastFpsTime).TotalMilliseconds >= 1000) { _currentFpsText = $"FPS: Cam:{_camFrameCount} / Proc:{_procFrameCount} / UI:{_uiFrameCount}"; _camFrameCount = 0; _procFrameCount = 0; _uiFrameCount = 0; _lastFpsTime = DateTime.Now; }

            double limitMs = 33.0; if (!_isRunning && _autoStartCount == 0) limitMs = 200.0; else if (_appSettings.TriggerMode == "Plc" && !_isRunning) limitMs = 200.0;

            bool hasForceAction = _plcTriggerReceived || _requestManualTest || _requestErrorTest || _requestOkTest || _pendingSaveResult != -1;
            if (!hasForceAction && (DateTime.Now - _lastFrameProcessTime).TotalMilliseconds < limitMs) { frame.Dispose(); return; }
            if (_isProcessing) { frame.Dispose(); return; }
            _isProcessing = true; _lastFrameProcessTime = DateTime.Now;

            Task.Run(() => {
                try
                {
                    _procFrameCount++; bool isDebug = _isDebugTabActive; double b = 0;
                    if (_appSettings.TriggerMode == "Visual" || _isRunning || _autoStartCount > 0)
                        b = _measurement.CalculateBrightness(frame, _roi);

                    if (isDebug) _measurement.UpdateDebugImageRealtime(frame, _saveRoi);
                    bool forceUiUpdate = false;

                    if (_requestManualTest) { _requestManualTest = false; int manualResult = _measurement.Inspect(frame, _saveRoi, isDebug); SafeInvoke(() => UpdateResultDisplay(manualResult, true, b)); _pendingSaveResult = manualResult; forceUiUpdate = true; }
                    // ★ テスト用: 強制OKテスト（不要になったら削除可能）
                    if (_requestOkTest)
                    {
                        _requestOkTest = false;
                        int forceOkResult = 1;
                        AppendLog($"[TEST] 強制OKテスト要求を受信しました。判定結果(OK)を送信します");
                        _plc.SendResult(true);
                        if (_appSettings.TriggerMode == "Plc")
                        {
                            AppendLog($"[TEST] PLCのトリガアドレス M{_appSettings.ReadDeviceAddress} をクリア (0書き込み) します");
                            Task.Run(() => _plc.WriteDevice(_appSettings.ReadDeviceAddress, 0));
                        }
                        SafeInvoke(() => UpdateResultDisplay(forceOkResult, true, b));
                        _pendingSaveResult = forceOkResult;
                        forceUiUpdate = true;
                    }

                    if (_requestErrorTest)
                    {
                        _requestErrorTest = false;
                        int forceNgResult = 2;
                        AppendLog($"[TEST] 強制NGテスト要求を受信しました。判定結果(NG)を送信します");
                        _plc.SendResult(false);
                        if (_appSettings.TriggerMode == "Plc")
                        {
                            AppendLog($"[TEST] PLCのトリガアドレス M{_appSettings.ReadDeviceAddress} をクリア (0書き込み) します");
                            Task.Run(() => _plc.WriteDevice(_appSettings.ReadDeviceAddress, 0));
                        }
                        SafeInvoke(() => UpdateResultDisplay(forceNgResult, true, b));
                        _pendingSaveResult = forceNgResult;
                        forceUiUpdate = true;
                    }

                    UpdateStateMachine(frame, b, isDebug);
                    if (_pendingSaveResult != -1) forceUiUpdate = true;
                    double uiLimitMs = _isRunning ? 66.0 : 200.0;

                    if (forceUiUpdate || (DateTime.Now - _lastUiUpdateTime).TotalMilliseconds > uiLimitMs) { _lastUiUpdateTime = DateTime.Now; SafeBeginInvoke(() => { _uiFrameCount++; UpdateUIDisplay(frame, b, isDebug); frame.Dispose(); _isProcessing = false; }); } else { frame.Dispose(); _isProcessing = false; }
                }
                catch { if (frame != null && !frame.IsDisposed) frame.Dispose(); _isProcessing = false; }
            });
        }

        private void UpdateStateMachine(Mat frame, double b, bool isDebug)
        {
            bool rawTriggered = (_appSettings.TriggerMode == "Plc" ? _plcTriggerReceived : (_triggerOnBright ? (b > _triggerThreshold) : (b < _triggerThreshold)));
            bool isReset = (_appSettings.TriggerMode == "Plc" ? false : (_triggerOnBright ? (b < _resetThreshold) : (b > _resetThreshold)));
            bool isTriggerEdge = rawTriggered && !_wasTriggeredLastFrame; _wasTriggeredLastFrame = rawTriggered;

            if (!_isRunning)
            {
                if (isTriggerEdge)
                {
                    if (_autoStartCount > 0)
                    {
                        _missedTriggerCount++;
                        if (_missedTriggerCount >= _autoStartCount) { _isRunning = true; _missedTriggerCount = 0; SafeInvoke(() => { _btnRunToggle.Text = "■ 運転停止 (STOP)"; _btnRunToggle.BackColor = Color.Salmon; }); }
                        else { SafeInvoke(() => lblStateUpdate($"STARTING SOON... ({_missedTriggerCount}/{_autoStartCount})", Color.Orange)); if (_appSettings.TriggerMode == "Plc") { _plcTriggerReceived = false; _plc.SendResult(true); Task.Run(() => _plc.WriteDevice(_appSettings.ReadDeviceAddress, 0)); } return; }
                    }
                    else { if (_appSettings.TriggerMode == "Plc") { _plcTriggerReceived = false; _plc.SendResult(true); Task.Run(() => _plc.WriteDevice(_appSettings.ReadDeviceAddress, 0)); } return; }
                }
                else return;
            }

            switch (_currentState)
            {
                case STATE_WAITING:
                    if (rawTriggered)
                    {
                        _currentRetry = 0;
                        if (_chkEnableOuterTiltCheck != null) _measurement.EnableOuterTiltCheck = _chkEnableOuterTiltCheck.Checked;
                        if (_chkEnableHoleCheck != null) _measurement.EnableHoleCheck = _chkEnableHoleCheck.Checked;

                        // 検査開始前に前回の良品/不良品出力をクリア
                        AppendLog("検査開始のため前回の判定出力をクリアします。");
                        Task.Run(() => {
                            _plc.WriteDevice(_appSettings.OkDeviceAddress, 0);
                            _plc.WriteDevice(_appSettings.NgDeviceAddress, 0);
                        });

                        if (_appSettings.TriggerMode == "Plc") { _plcTriggerReceived = false; _currentState = STATE_STABILIZING; _stabilityStartTime = DateTime.Now; SafeInvoke(() => lblStateUpdate("DELAYING...", Color.Yellow)); }
                        else { if (isTriggerEdge) { _currentState = STATE_STABILIZING; _stabilityStartTime = DateTime.Now; SafeInvoke(() => lblStateUpdate("TESTING...", Color.Yellow)); } }
                    }
                    break;

                case STATE_STABILIZING:
                    if (_appSettings.TriggerMode == "Plc")
                    {
                        double targetDelay = _currentRetry == 0 ? _plcDelayMs : _retryDelayMs;
                        if ((DateTime.Now - _stabilityStartTime).TotalMilliseconds > targetDelay)
                        {
                            int inspectResult = _measurement.Inspect(frame, _saveRoi, isDebug);

                            if (inspectResult != 1 && _currentRetry >= _maxRetryCount && _measurement.EnableOuterTiltCheck && !_measurement.EnableHoleCheck)
                            {
                                _measurement.EnableOuterTiltCheck = false; _measurement.EnableHoleCheck = true;
                                inspectResult = _measurement.Inspect(frame, _saveRoi, isDebug);
                                SafeInvoke(() => lblStateUpdate("FALLBACK HOLE...", Color.Orange));
                            }

                            if (inspectResult == 1 || _currentRetry >= _maxRetryCount) { ProcessInspectionResult(inspectResult, b); _currentState = STATE_COOLING; _cooldownStartTime = DateTime.Now; }
                            else { _currentRetry++; _stabilityStartTime = DateTime.Now; SafeInvoke(() => lblStateUpdate($"RETRY {_currentRetry}/{_maxRetryCount}", Color.Orange)); }
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
                                int inspectResult = _measurement.Inspect(frame, _saveRoi, isDebug);

                                if (inspectResult != 1 && _currentRetry >= _maxRetryCount && _measurement.EnableOuterTiltCheck && !_measurement.EnableHoleCheck)
                                {
                                    _measurement.EnableOuterTiltCheck = false; _measurement.EnableHoleCheck = true;
                                    inspectResult = _measurement.Inspect(frame, _saveRoi, isDebug);
                                    SafeInvoke(() => lblStateUpdate("FALLBACK HOLE...", Color.Orange));
                                }

                                if (inspectResult == 1 || _currentRetry >= _maxRetryCount) { ProcessInspectionResult(inspectResult, b); _currentState = STATE_COOLING; _cooldownStartTime = DateTime.Now; }
                                else { _currentRetry++; _stabilityStartTime = DateTime.Now; SafeInvoke(() => lblStateUpdate($"RETRY {_currentRetry}/{_maxRetryCount}", Color.Orange)); }
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

        private void lblStateUpdate(string text, Color color) { if (_lblBigResult != null && !_lblBigResult.IsDisposed) { _lblBigResult.Text = text; _lblBigResult.BackColor = color; } }

        private void ProcessInspectionResult(int inspectResult, double brightness)
        {
            bool isOk = (inspectResult == 1);
            AppendLog($"検査完了。結果: {(isOk ? "OK" : "NG")} (D{_appSettings.WriteDeviceAddress} に判定結果を送信します)");
            _plc.SendResult(isOk);
            if (_appSettings.TriggerMode == "Plc")
            {
                AppendLog($"PLCのトリガアドレス D{_appSettings.ReadDeviceAddress} をクリア (0書き込み) します");
                Task.Run(() => _plc.WriteDevice(_appSettings.ReadDeviceAddress, 0));
            }
            SafeInvoke(() => UpdateResultDisplay(inspectResult, false, brightness));
            _pendingSaveResult = inspectResult;

            // ★分析モジュールへの登録処理
            double lastAngle = _measurement.LastOuterAngleDeg; // 最後の実測角度
            _analyzer.AddRecord(lastAngle, brightness, isOk, _logDirPath);
        }

        private void UpdateUIDisplay(Mat frame, double b, bool isDebug)
        {
            if (!_isUiLoaded || this.IsDisposed) return;
            using (Mat disp = new Mat())
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
                    _measurement.DrawOverlay(disp);
                }

                if (_pendingSaveResult != -1) { if (_saveMode == 2 || (_saveMode == 1 && _pendingSaveResult != 1)) SaveInspectionImage(disp, _pendingSaveResult); _pendingSaveResult = -1; }
                Bitmap bmp = BitmapConverter.ToBitmap(disp); Image old = _pictureBox.Image; _pictureBox.Image = bmp; old?.Dispose();
            }
            if (isDebug) { using (Mat binImg = new Mat()) { _measurement.GetDebugImage(binImg); if (!binImg.Empty()) { Bitmap bmpD = BitmapConverter.ToBitmap(binImg); Image oldD = _pictureBoxDebug.Image; _pictureBoxDebug.Image = bmpD; oldD?.Dispose(); } } }
            if (_lblCurrentHoleDistPx != null && !_lblCurrentHoleDistPx.IsDisposed && _measurement.LastHoleDistancePx > 0) _lblCurrentHoleDistPx.Text = "現在の穴/エッジ間距離: " + _measurement.LastHoleDistancePx.ToString("F1") + " px";
            if (_lblStatus != null && !_lblStatus.IsDisposed) { if (!_isRunning) { _lblStatus.Text = "Status: STOPPED"; _lblStatus.ForeColor = Color.Red; } else { _lblStatus.Text = "Status: " + (_currentState == 0 ? "WAITING" : (_currentState == 1 ? "STABILIZING" : "COOLING")); _lblStatus.ForeColor = _currentState == 0 ? Color.Gray : (_currentState == 1 ? Color.Goldenrod : Color.LimeGreen); } }
            if (_lblBrightness != null && !_lblBrightness.IsDisposed) _lblBrightness.Text = "Brightness: " + b.ToString("F1");
            if (_lblFps != null && !_lblFps.IsDisposed) _lblFps.Text = _currentFpsText;
        }

        private void SaveInspectionImage(Mat img, int res)
        {
            try { Mat imgToSave = img.Clone(); Task.Run(() => { try { string dir = Path.Combine(_logDirPath, DateTime.Now.ToString("yyyyMMdd")); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); string resStr = (res == 1) ? "OK" : "NG"; string fileName = string.Format("{0:HHmmss_fff}_{1}.jpg", DateTime.Now, resStr); string path = Path.Combine(dir, fileName); CvRect crop = _saveRoi & new CvRect(0, 0, imgToSave.Width, imgToSave.Height); using (Mat cropped = new Mat(imgToSave, crop)) using (Mat resized = new Mat()) { Cv2.Resize(cropped, resized, new CvSize(cropped.Width / 2, cropped.Height / 2)); var p = new ImageEncodingParam(ImwriteFlags.JpegQuality, 65); Cv2.ImWrite(path, resized, p); } } catch { } finally { if (imgToSave != null && !imgToSave.IsDisposed) imgToSave.Dispose(); } }); } catch { }
        }

        private void UpdateResultDisplay(int res, bool manual, double brightness = 0.0)
        {
            if (_lblBigResult == null || _lblBigResult.IsDisposed) return;
            _lblBigResult.Text = res == 1 ? "OK" : "NG";
            _lblBigResult.BackColor = res == 1 ? Color.LimeGreen : Color.Red;
            if (!manual)
            {
                _totalCount++;
                if (res == 1) _okCount++; else _ngCount++;
                UpdateCounterDisplay();
                AppendDailyLog(res, brightness);
            }
        }

        private void UpdateCounterDisplay() { if (_lblTotal == null || _lblTotal.IsDisposed) return; _lblTotal.Text = "総検査数 : " + _totalCount; _lblOk.Text = "良品 (OK): " + _okCount; _lblNg.Text = "不良 (NG): " + _ngCount; }

        private void UpdateSettingsFromUI()
        {
            if (_isLoadingConfig) return;

            if (_chkEnableJigCheck != null) _measurement.EnableJigCheck = _chkEnableJigCheck.Checked;
            if (_chkEnableOuterTiltCheck != null) _measurement.EnableOuterTiltCheck = _chkEnableOuterTiltCheck.Checked;
            if (_chkEnableHoleCheck != null) _measurement.EnableHoleCheck = _chkEnableHoleCheck.Checked;

            _triggerThreshold = (double)_nudTriggerThreshold.Value; _stabilityDurationMs = (int)_nudStabilityDuration.Value; _resetThreshold = (double)_nudResetThreshold.Value;
            _plcDelayMs = (int)_nudPlcDelayMs.Value; _maxRetryCount = (int)_nudRetryCount.Value; _retryDelayMs = (int)_nudRetryDelayMs.Value;
            _autoStartCount = (int)_nudAutoStartCount.Value; _roi = new CvRect((int)_nudRoiX.Value, (int)_nudRoiY.Value, (int)_nudRoiW.Value, (int)_nudRoiH.Value);
            _saveRoi = new CvRect((int)_nudSaveRoiX.Value, (int)_nudSaveRoiY.Value, (int)_nudSaveRoiW.Value, (int)_nudSaveRoiH.Value); _logKeepDays = (int)_nudLogKeepDays.Value;

            _measurement.TiltLeftRoi = new CvRect((int)_nudTiltLX.Value, (int)_nudTiltLY.Value, (int)_nudTiltLW.Value, (int)_nudTiltLH.Value);
            _measurement.TiltRightRoi = new CvRect((int)_nudTiltRX.Value, (int)_nudTiltRY.Value, (int)_nudTiltRW.Value, (int)_nudTiltRH.Value);
            _measurement.ThreshOuterL = (int)_nudThreshOuterL.Value; _measurement.ThreshOuterR = (int)_nudThreshOuterR.Value;

            _measurement.ThreshBtmInnerL = (int)_nudThreshBtmInnerL.Value; _measurement.ThreshBtmInnerR = (int)_nudThreshBtmInnerR.Value;

            _measurement.TargetOuterXOffsetMm = (double)_nudOuterTargetX.Value; _measurement.OuterOffsetToleranceMm = (double)_nudOuterOffsetX.Value;
            _measurement.TargetOuterAngleDeg = (double)_nudOuterTargetA.Value; _measurement.OuterAngleToleranceDeg = (double)_nudOuterOffsetA.Value;

            _measurement.BtmMeasureRoi = new CvRect((int)_nudBtmRoiX.Value, (int)_nudBtmRoiY.Value, (int)_nudBtmRoiW.Value, (int)_nudBtmRoiH.Value);
            _measurement.BtmInnerLeftRoi = new CvRect((int)_nudBtmInnerLX.Value, (int)_nudBtmInnerLY.Value, (int)_nudBtmInnerLW.Value, (int)_nudBtmInnerLH.Value);
            _measurement.BtmInnerRightRoi = new CvRect((int)_nudBtmInnerRX.Value, (int)_nudBtmInnerRY.Value, (int)_nudBtmInnerRW.Value, (int)_nudBtmInnerRH.Value);

            _measurement.HolesRoi = new CvRect((int)_nudHolesX.Value, (int)_nudHolesY.Value, (int)_nudHolesW.Value, (int)_nudHolesH.Value);
            _measurement.MinHoleArea = (int)_nudMinHoleArea.Value; _measurement.MaxHoleArea = (int)_nudMaxHoleArea.Value; _measurement.MinCircularity = (double)_nudMinCircularity.Value;
            _measurement.SplitBoundaryX = (int)_nudSplitX.Value; _measurement.SplitBoundaryY = (int)_nudSplitY.Value;
            _measurement.ThreshTopLeft = (int)_nudThreshTL.Value; _measurement.ThreshTopRight = (int)_nudThreshTR.Value; _measurement.ThreshBtmLeft = (int)_nudThreshBL.Value; _measurement.ThreshBtmRight = (int)_nudThreshBR.Value;

            _measurement.JigLeftRoi = new CvRect((int)_nudJigLX.Value, (int)_nudJigLY.Value, (int)_nudJigLW.Value, (int)_nudJigLH.Value);
            _measurement.JigRightRoi = new CvRect((int)_nudJigRX.Value, (int)_nudJigRY.Value, (int)_nudJigRW.Value, (int)_nudJigRH.Value);
            _measurement.TargetJigDistanceMm = (double)_nudJigTarget.Value; _measurement.JigToleranceMm = (double)_nudJigTolerance.Value;

            _measurement.PixelToMmRatio = (double)_nudPixelToMm.Value;
            _measurement.TargetXOffsetMm = (double)_nudTargetXOffset.Value; _measurement.OffsetToleranceMm = (double)_nudOffsetTolerance.Value;
            _measurement.TargetAngleDeg = (double)_nudTargetAngle.Value; _measurement.AngleToleranceDeg = (double)_nudAngleTolerance.Value;
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

                _measurement.EnableJigCheck = d.TryGetValue("EnableJigCheck", out var ej) ? bool.Parse(ej) : true;

                if (d.TryGetValue("UseOuterEdgeForTilt", out var uoe))
                {
                    bool useOuter = bool.Parse(uoe); _measurement.EnableOuterTiltCheck = useOuter; _measurement.EnableHoleCheck = !useOuter;
                }
                else
                {
                    _measurement.EnableOuterTiltCheck = d.TryGetValue("EnableOuterTiltCheck", out var eot) ? bool.Parse(eot) : true;
                    _measurement.EnableHoleCheck = d.TryGetValue("EnableHoleCheck", out var ehc) ? bool.Parse(ehc) : true;
                }

                _triggerOnBright = d.TryGetValue("TriggerOnBright", out var tb) ? bool.Parse(tb) : true;
                _triggerThreshold = GetD("TriggerThreshold", _triggerThreshold); _stabilityDurationMs = GetI("StabilityDurationMs", _stabilityDurationMs);
                _plcDelayMs = GetI("PlcDelayMs", _plcDelayMs); _maxRetryCount = GetI("MaxRetryCount", _maxRetryCount); _retryDelayMs = GetI("RetryDelayMs", _retryDelayMs);
                _autoStartCount = GetI("AutoStartCount", _autoStartCount); _resetThreshold = GetD("ResetThreshold", _resetThreshold); _saveMode = GetI("SaveMode", _saveMode);
                _roi = new CvRect(GetI("RoiX", _roi.X), GetI("RoiY", _roi.Y), GetI("RoiW", _roi.Width), GetI("RoiH", _roi.Height));
                _saveRoi = new CvRect(GetI("SaveRoiX", _saveRoi.X), GetI("SaveRoiY", _saveRoi.Y), GetI("SaveRoiW", _saveRoi.Width), GetI("SaveRoiH", _saveRoi.Height));
                _logKeepDays = GetI("LogKeepDays", _logKeepDays);

                _measurement.TiltLeftRoi = new CvRect(GetI("TiltLX", _measurement.TiltLeftRoi.X), GetI("TiltLY", _measurement.TiltLeftRoi.Y), GetI("TiltLW", _measurement.TiltLeftRoi.Width), GetI("TiltLH", _measurement.TiltLeftRoi.Height));
                _measurement.TiltRightRoi = new CvRect(GetI("TiltRX", _measurement.TiltRightRoi.X), GetI("TiltRY", _measurement.TiltRightRoi.Y), GetI("TiltRW", _measurement.TiltRightRoi.Width), GetI("TiltRH", _measurement.TiltRightRoi.Height));
                _measurement.ThreshOuterL = GetI("ThreshOuterL", 100); _measurement.ThreshOuterR = GetI("ThreshOuterR", 100);

                _measurement.ThreshBtmInnerL = GetI("ThreshBtmInnerL", 51); _measurement.ThreshBtmInnerR = GetI("ThreshBtmInnerR", 51);

                _measurement.TargetOuterXOffsetMm = GetD("TargetOuterXOffsetMm", _measurement.TargetOuterXOffsetMm);
                _measurement.OuterOffsetToleranceMm = GetD("OuterOffsetToleranceMm", _measurement.OuterOffsetToleranceMm);
                _measurement.TargetOuterAngleDeg = GetD("TargetOuterAngleDeg", _measurement.TargetOuterAngleDeg);
                _measurement.OuterAngleToleranceDeg = GetD("OuterAngleToleranceDeg", _measurement.OuterAngleToleranceDeg);

                _measurement.BtmMeasureRoi = new CvRect(GetI("BtmRoiX", _measurement.BtmMeasureRoi.X), GetI("BtmRoiY", _measurement.BtmMeasureRoi.Y), GetI("BtmRoiW", _measurement.BtmMeasureRoi.Width), GetI("BtmRoiH", _measurement.BtmMeasureRoi.Height));
                _measurement.BtmInnerLeftRoi = new CvRect(GetI("BtmInnerLX", _measurement.BtmInnerLeftRoi.X), GetI("BtmInnerLY", _measurement.BtmInnerLeftRoi.Y), GetI("BtmInnerLW", _measurement.BtmInnerLeftRoi.Width), GetI("BtmInnerLH", _measurement.BtmInnerLeftRoi.Height));
                _measurement.BtmInnerRightRoi = new CvRect(GetI("BtmInnerRX", _measurement.BtmInnerRightRoi.X), GetI("BtmInnerRY", _measurement.BtmInnerRightRoi.Y), GetI("BtmInnerRW", _measurement.BtmInnerRightRoi.Width), GetI("BtmInnerRH", _measurement.BtmInnerRightRoi.Height));

                _measurement.HolesRoi = new CvRect(GetI("HolesX", _measurement.HolesRoi.X), GetI("HolesY", _measurement.HolesRoi.Y), GetI("HolesW", _measurement.HolesRoi.Width), GetI("HolesH", _measurement.HolesRoi.Height));
                _measurement.MinHoleArea = GetI("MinHoleArea", _measurement.MinHoleArea); _measurement.MaxHoleArea = GetI("MaxHoleArea", _measurement.MaxHoleArea);
                _measurement.MinCircularity = GetD("MinCirc", _measurement.MinCircularity);
                _measurement.SplitBoundaryX = GetI("SplitBoundaryX", 320); _measurement.SplitBoundaryY = GetI("SplitBoundaryY", _measurement.SplitBoundaryY);
                int oldEdge = GetI("EdgeThresh", 12); int oldHole = GetI("HoleThresh", 51);
                _measurement.ThreshTopLeft = GetI("ThreshTL", oldEdge); _measurement.ThreshTopRight = GetI("ThreshTR", oldEdge);
                _measurement.ThreshBtmLeft = GetI("ThreshBL", oldHole); _measurement.ThreshBtmRight = GetI("ThreshBR", oldHole);

                _measurement.JigLeftRoi = new CvRect(GetI("JigLX", _measurement.JigLeftRoi.X), GetI("JigLY", _measurement.JigLeftRoi.Y), GetI("JigLW", _measurement.JigLeftRoi.Width), GetI("JigLH", _measurement.JigLeftRoi.Height));
                _measurement.JigRightRoi = new CvRect(GetI("JigRX", _measurement.JigRightRoi.X), GetI("JigRY", _measurement.JigRightRoi.Y), GetI("JigRW", _measurement.JigRightRoi.Width), GetI("JigRH", _measurement.JigRightRoi.Height));

                _measurement.TargetJigDistanceMm = GetD("JigTargetMm", _measurement.TargetJigDistanceMm); _measurement.JigToleranceMm = GetD("JigTolMm", _measurement.JigToleranceMm);
                _measurement.PixelToMmRatio = GetD("PixelToMmRatio", _measurement.PixelToMmRatio);
                _measurement.TargetXOffsetMm = GetD("TargetXOffsetMm", _measurement.TargetXOffsetMm); _measurement.OffsetToleranceMm = GetD("OffsetToleranceMm", _measurement.OffsetToleranceMm);
                _measurement.TargetAngleDeg = GetD("TargetAngleDeg", _measurement.TargetAngleDeg); _measurement.AngleToleranceDeg = GetD("AngleToleranceDeg", _measurement.AngleToleranceDeg);

                if (_chkEnableJigCheck != null) _chkEnableJigCheck.Checked = _measurement.EnableJigCheck;
                if (_chkEnableOuterTiltCheck != null) _chkEnableOuterTiltCheck.Checked = _measurement.EnableOuterTiltCheck;
                if (_chkEnableHoleCheck != null) _chkEnableHoleCheck.Checked = _measurement.EnableHoleCheck;

                _cmbTriggerMode.SelectedIndex = _triggerOnBright ? 0 : 1; _cmbSaveMode.SelectedIndex = _saveMode;
                _nudTriggerThreshold.Value = (decimal)_triggerThreshold; _nudStabilityDuration.Value = _stabilityDurationMs;
                _nudPlcDelayMs.Value = _plcDelayMs; _nudRetryCount.Value = _maxRetryCount; _nudRetryDelayMs.Value = _retryDelayMs;
                _nudAutoStartCount.Value = _autoStartCount; _nudResetThreshold.Value = (decimal)_resetThreshold;
                _nudRoiX.Value = _roi.X; _nudRoiY.Value = _roi.Y; _nudRoiW.Value = _roi.Width; _nudRoiH.Value = _roi.Height;
                _nudSaveRoiX.Value = _saveRoi.X; _nudSaveRoiY.Value = _saveRoi.Y; _nudSaveRoiW.Value = _saveRoi.Width; _nudSaveRoiH.Value = _saveRoi.Height;
                _nudLogKeepDays.Value = _logKeepDays;

                _nudTiltLX.Value = _measurement.TiltLeftRoi.X; _nudTiltLY.Value = _measurement.TiltLeftRoi.Y; _nudTiltLW.Value = _measurement.TiltLeftRoi.Width; _nudTiltLH.Value = _measurement.TiltLeftRoi.Height;
                _nudTiltRX.Value = _measurement.TiltRightRoi.X; _nudTiltRY.Value = _measurement.TiltRightRoi.Y; _nudTiltRW.Value = _measurement.TiltRightRoi.Width; _nudTiltRH.Value = _measurement.TiltRightRoi.Height;
                _nudThreshOuterL.Value = _measurement.ThreshOuterL; _nudThreshOuterR.Value = _measurement.ThreshOuterR;

                _nudThreshBtmInnerL.Value = _measurement.ThreshBtmInnerL; _nudThreshBtmInnerR.Value = _measurement.ThreshBtmInnerR;

                _nudOuterTargetX.Value = (decimal)_measurement.TargetOuterXOffsetMm; _nudOuterOffsetX.Value = (decimal)_measurement.OuterOffsetToleranceMm;
                _nudOuterTargetA.Value = (decimal)_measurement.TargetOuterAngleDeg; _nudOuterOffsetA.Value = (decimal)_measurement.OuterAngleToleranceDeg;

                _nudBtmRoiX.Value = _measurement.BtmMeasureRoi.X; _nudBtmRoiY.Value = _measurement.BtmMeasureRoi.Y; _nudBtmRoiW.Value = _measurement.BtmMeasureRoi.Width; _nudBtmRoiH.Value = _measurement.BtmMeasureRoi.Height;
                _nudBtmInnerLX.Value = _measurement.BtmInnerLeftRoi.X; _nudBtmInnerLY.Value = _measurement.BtmInnerLeftRoi.Y; _nudBtmInnerLW.Value = _measurement.BtmInnerLeftRoi.Width; _nudBtmInnerLH.Value = _measurement.BtmInnerLeftRoi.Height;
                _nudBtmInnerRX.Value = _measurement.BtmInnerRightRoi.X; _nudBtmInnerRY.Value = _measurement.BtmInnerRightRoi.Y; _nudBtmInnerRW.Value = _measurement.BtmInnerRightRoi.Width; _nudBtmInnerRH.Value = _measurement.BtmInnerRightRoi.Height;

                _nudHolesX.Value = _measurement.HolesRoi.X; _nudHolesY.Value = _measurement.HolesRoi.Y; _nudHolesW.Value = _measurement.HolesRoi.Width; _nudHolesH.Value = _measurement.HolesRoi.Height;
                _nudMinHoleArea.Value = _measurement.MinHoleArea; _nudMaxHoleArea.Value = _measurement.MaxHoleArea; _nudMinCircularity.Value = (decimal)_measurement.MinCircularity;
                _nudSplitX.Value = _measurement.SplitBoundaryX; _nudSplitY.Value = _measurement.SplitBoundaryY;
                _nudThreshTL.Value = _measurement.ThreshTopLeft; _nudThreshTR.Value = _measurement.ThreshTopRight;
                _nudThreshBL.Value = _measurement.ThreshBtmLeft; _nudThreshBR.Value = _measurement.ThreshBtmRight;

                _nudJigLX.Value = _measurement.JigLeftRoi.X; _nudJigLY.Value = _measurement.JigLeftRoi.Y; _nudJigLW.Value = _measurement.JigLeftRoi.Width; _nudJigLH.Value = _measurement.JigLeftRoi.Height;
                _nudJigRX.Value = _measurement.JigRightRoi.X; _nudJigRY.Value = _measurement.JigRightRoi.Y; _nudJigRW.Value = _measurement.JigRightRoi.Width; _nudJigRH.Value = _measurement.JigRightRoi.Height;

                _nudJigTarget.Value = (decimal)_measurement.TargetJigDistanceMm; _nudJigTolerance.Value = (decimal)_measurement.JigToleranceMm;
                _nudPixelToMm.Value = (decimal)_measurement.PixelToMmRatio;
                _nudTargetXOffset.Value = (decimal)_measurement.TargetXOffsetMm; _nudOffsetTolerance.Value = (decimal)_measurement.OffsetToleranceMm;
                _nudTargetAngle.Value = (decimal)_measurement.TargetAngleDeg; _nudAngleTolerance.Value = (decimal)_measurement.AngleToleranceDeg;
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
                    sw.WriteLine("EnableJigCheck=" + _measurement.EnableJigCheck);
                    sw.WriteLine("EnableOuterTiltCheck=" + _measurement.EnableOuterTiltCheck);
                    sw.WriteLine("EnableHoleCheck=" + _measurement.EnableHoleCheck);

                    sw.WriteLine("TriggerOnBright=" + _triggerOnBright); sw.WriteLine("TriggerThreshold=" + _triggerThreshold);
                    sw.WriteLine("StabilityDurationMs=" + _stabilityDurationMs);
                    sw.WriteLine("PlcDelayMs=" + _plcDelayMs); sw.WriteLine("MaxRetryCount=" + _maxRetryCount); sw.WriteLine("RetryDelayMs=" + _retryDelayMs);
                    sw.WriteLine("AutoStartCount=" + _autoStartCount); sw.WriteLine("ResetThreshold=" + _resetThreshold); sw.WriteLine("SaveMode=" + _saveMode);
                    sw.WriteLine("RoiX=" + _roi.X); sw.WriteLine("RoiY=" + _roi.Y); sw.WriteLine("RoiW=" + _roi.Width); sw.WriteLine("RoiH=" + _roi.Height);
                    sw.WriteLine("SaveRoiX=" + _saveRoi.X); sw.WriteLine("SaveRoiY=" + _saveRoi.Y); sw.WriteLine("SaveRoiW=" + _saveRoi.Width); sw.WriteLine("SaveRoiH=" + _saveRoi.Height);
                    sw.WriteLine("LogKeepDays=" + _logKeepDays);

                    sw.WriteLine("TiltLX=" + _measurement.TiltLeftRoi.X); sw.WriteLine("TiltLY=" + _measurement.TiltLeftRoi.Y); sw.WriteLine("TiltLW=" + _measurement.TiltLeftRoi.Width); sw.WriteLine("TiltLH=" + _measurement.TiltLeftRoi.Height);
                    sw.WriteLine("TiltRX=" + _measurement.TiltRightRoi.X); sw.WriteLine("TiltRY=" + _measurement.TiltRightRoi.Y); sw.WriteLine("TiltRW=" + _measurement.TiltRightRoi.Width); sw.WriteLine("TiltRH=" + _measurement.TiltRightRoi.Height);
                    sw.WriteLine("ThreshOuterL=" + _measurement.ThreshOuterL); sw.WriteLine("ThreshOuterR=" + _measurement.ThreshOuterR);

                    sw.WriteLine("ThreshBtmInnerL=" + _measurement.ThreshBtmInnerL); sw.WriteLine("ThreshBtmInnerR=" + _measurement.ThreshBtmInnerR);

                    sw.WriteLine("TargetOuterXOffsetMm=" + _measurement.TargetOuterXOffsetMm); sw.WriteLine("OuterOffsetToleranceMm=" + _measurement.OuterOffsetToleranceMm);
                    sw.WriteLine("TargetOuterAngleDeg=" + _measurement.TargetOuterAngleDeg); sw.WriteLine("OuterAngleToleranceDeg=" + _measurement.OuterAngleToleranceDeg);

                    sw.WriteLine("BtmRoiX=" + _measurement.BtmMeasureRoi.X); sw.WriteLine("BtmRoiY=" + _measurement.BtmMeasureRoi.Y); sw.WriteLine("BtmRoiW=" + _measurement.BtmMeasureRoi.Width); sw.WriteLine("BtmRoiH=" + _measurement.BtmMeasureRoi.Height);
                    sw.WriteLine("BtmInnerLX=" + _measurement.BtmInnerLeftRoi.X); sw.WriteLine("BtmInnerLY=" + _measurement.BtmInnerLeftRoi.Y); sw.WriteLine("BtmInnerLW=" + _measurement.BtmInnerLeftRoi.Width); sw.WriteLine("BtmInnerLH=" + _measurement.BtmInnerLeftRoi.Height);
                    sw.WriteLine("BtmInnerRX=" + _measurement.BtmInnerRightRoi.X); sw.WriteLine("BtmInnerRY=" + _measurement.BtmInnerRightRoi.Y); sw.WriteLine("BtmInnerRW=" + _measurement.BtmInnerRightRoi.Width); sw.WriteLine("BtmInnerRH=" + _measurement.BtmInnerRightRoi.Height);

                    sw.WriteLine("HolesX=" + _measurement.HolesRoi.X); sw.WriteLine("HolesY=" + _measurement.HolesRoi.Y); sw.WriteLine("HolesW=" + _measurement.HolesRoi.Width); sw.WriteLine("HolesH=" + _measurement.HolesRoi.Height);
                    sw.WriteLine("MinHoleArea=" + _measurement.MinHoleArea); sw.WriteLine("MaxHoleArea=" + _measurement.MaxHoleArea);
                    sw.WriteLine("MinCirc=" + _measurement.MinCircularity);
                    sw.WriteLine("SplitBoundaryX=" + _measurement.SplitBoundaryX); sw.WriteLine("SplitBoundaryY=" + _measurement.SplitBoundaryY);
                    sw.WriteLine("ThreshTL=" + _measurement.ThreshTopLeft); sw.WriteLine("ThreshTR=" + _measurement.ThreshTopRight);
                    sw.WriteLine("ThreshBL=" + _measurement.ThreshBtmLeft); sw.WriteLine("ThreshBR=" + _measurement.ThreshBtmRight);

                    sw.WriteLine("JigLX=" + _measurement.JigLeftRoi.X); sw.WriteLine("JigLY=" + _measurement.JigLeftRoi.Y); sw.WriteLine("JigLW=" + _measurement.JigLeftRoi.Width); sw.WriteLine("JigLH=" + _measurement.JigLeftRoi.Height);
                    sw.WriteLine("JigRX=" + _measurement.JigRightRoi.X); sw.WriteLine("JigRY=" + _measurement.JigRightRoi.Y); sw.WriteLine("JigRW=" + _measurement.JigRightRoi.Width); sw.WriteLine("JigRH=" + _measurement.JigRightRoi.Height);

                    sw.WriteLine("JigTargetMm=" + _measurement.TargetJigDistanceMm); sw.WriteLine("JigTolMm=" + _measurement.JigToleranceMm);
                    sw.WriteLine("PixelToMmRatio=" + _measurement.PixelToMmRatio);
                    sw.WriteLine("TargetXOffsetMm=" + _measurement.TargetXOffsetMm); sw.WriteLine("OffsetToleranceMm=" + _measurement.OffsetToleranceMm);
                    sw.WriteLine("TargetAngleDeg=" + _measurement.TargetAngleDeg); sw.WriteLine("AngleToleranceDeg=" + _measurement.AngleToleranceDeg);
                }
            }
            catch (Exception ex) { MessageBox.Show("設定ファイルの保存に失敗しました。\n\n" + ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}