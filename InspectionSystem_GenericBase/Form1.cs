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

        internal TeliCamera _camera;
        internal MeasurementCore _measurement;
        internal InspectionEngine _inspectionEngine;
        internal PlcCommunicator _plc;
        internal AppSettings _appSettings;
        internal ProductionAnalyzer _analyzer = new ProductionAnalyzer();
        internal PictureBox _pictureBoxMain = null!;
        internal PictureBox? _pictureBoxDebug;
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



        private NumericUpDown _nudTriggerThreshold = null!, _nudStabilityDuration = null!, _nudResetThreshold = null!;
        private NumericUpDown _nudExposureTime = null!;
        private NumericUpDown _nudRoiX = null!, _nudRoiY = null!, _nudRoiW = null!, _nudRoiH = null!;

        internal bool _isTestModeEnabled = false;
        internal string _testImagesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestImages");
        internal List<string> _testImageFiles = new List<string>();
        internal int _currentTestImageIndex = 0;
        private NumericUpDown _nudSaveRoiX = null!, _nudSaveRoiY = null!, _nudSaveRoiW = null!, _nudSaveRoiH = null!;

        private NumericUpDown _nudLogKeepDays = null!;
        internal int _logKeepDays = 30;

        private NumericUpDown _nudAutoStartCount = null!;
        internal int _autoStartCount = 3;
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


        private NumericUpDown _nudPlcDelayMs = null!;
        internal int _plcDelayMs = 100;

        private NumericUpDown _nudRetryCount = null!;
        private NumericUpDown _nudRetryDelayMs = null!;
        internal int _maxRetryCount = 3;
        internal int _retryDelayMs = 100;
        private int _currentRetry = 0;



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
        internal bool _configEnableOuterTilt = true;
        internal bool _configEnableHole = true;

        private int _totalCount = 0, _okCount = 0, _ngCount = 0;
        internal int _stabilityDurationMs = 300;
        internal int _saveMode = 0;
        private int _pendingSaveResult = -1;
        private InspectionResult? _lastInspectionResult;
        internal bool _triggerOnBright = true;
        internal double _triggerThreshold = 100.0, _resetThreshold = 50.0;
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
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9)
            };

            SetupRightPanelControls();
        }

        private void BtnAdminSettings_Click(object? sender, EventArgs e)
        {
            using (var prompt = new Form())
            {
                prompt.Width = 300;
                prompt.Height = 150;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = "管理者認証";
                prompt.StartPosition = FormStartPosition.CenterParent;

                Label textLabel = new Label() { Left = 50, Top = 20, Text = "パスワードを入力してください:" };
                TextBox textBox = new TextBox() { Left = 50, Top = 50, Width = 180, PasswordChar = '*' };
                Button confirmation = new Button() { Text = "OK", Left = 130, Width = 100, Top = 80, DialogResult = DialogResult.OK };

                prompt.Controls.Add(textBox);
                prompt.Controls.Add(confirmation);
                prompt.Controls.Add(textLabel);
                prompt.AcceptButton = confirmation;

                if (prompt.ShowDialog() == DialogResult.OK)
                {
                    if (textBox.Text == _appSettings.AdminPassword)
                    {


                        using (var settingsForm = new FormSettings(this))
                        {
                            settingsForm.ShowDialog(this);
                        }
                    }
                    else
                    {
                        MessageBox.Show("パスワードが違います。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void SetupRightPanelControls()
        {
            int x = 1310;
            int y = 10;
            int width = 580;

            _btnRunToggle = new Button { Text = "▶ 運転開始 (START)", Location = new Point(x, y), Size = new Size(width, 60), BackColor = Color.LightGreen, Font = new Font(this.Font.FontFamily, 16, FontStyle.Bold) };
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
            this.Controls.Add(_btnRunToggle); y += 75;

            _chkShowOverlay = new CheckBox { Text = "計測パラメータを表示する", Location = new Point(x, y), AutoSize = true, Checked = true };
            this.Controls.Add(_chkShowOverlay); y += 30;

            _lblAlignmentStatus = new Label { Text = "ALIGNMENT: STOPPED", Location = new Point(x, y), Size = new Size(width, 50), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 18, FontStyle.Bold), BackColor = Color.DarkGray, ForeColor = Color.White };
            this.Controls.Add(_lblAlignmentStatus); y += 60;

            _lblBigResult = new Label { Text = "TOTAL: STOPPED", Location = new Point(x, y), Size = new Size(width, 80), TextAlign = ContentAlignment.MiddleCenter, Font = new Font(this.Font.FontFamily, 24, FontStyle.Bold), BackColor = Color.DarkGray, ForeColor = Color.White };
            this.Controls.Add(_lblBigResult); y += 95;

            GroupBox gp = new GroupBox { Text = "生産カウンター", Location = new Point(x, y), Size = new Size(width, 110) };
            _lblTotal = new Label { Text = "総検査数 : 0", Location = new Point(20, 25), AutoSize = true };
            _lblOk = new Label { Text = "良品 (OK): 0", Location = new Point(20, 50), AutoSize = true, ForeColor = Color.Green, Font = new Font(this.Font, FontStyle.Bold) };
            _lblNg = new Label { Text = "不良 (NG): 0", Location = new Point(20, 75), AutoSize = true, ForeColor = Color.Red, Font = new Font(this.Font, FontStyle.Bold) };
            gp.Controls.Add(_lblTotal); gp.Controls.Add(_lblOk); gp.Controls.Add(_lblNg);
            this.Controls.Add(gp); y += 120;

            Button btnReset = new Button { Text = "カウンターリセット", Location = new Point(x, y), Size = new Size(width, 30) };
            btnReset.Click += (s, e) => { _totalCount = _okCount = _ngCount = 0; UpdateCounterDisplay(); };
            this.Controls.Add(btnReset); y += 45;

            Button btnAdminSettings = new Button
            {
                Text = "⚙ 管理・設定 (Admin / Settings)",
                Location = new Point(x, y),
                Size = new Size(width, 50),
                BackColor = Color.Orange,
                ForeColor = Color.White,
                Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold)
            };
            btnAdminSettings.Click += BtnAdminSettings_Click;
            this.Controls.Add(btnAdminSettings); y += 60;

            if (_txtLog != null) {
                _txtLog.Location = new Point(x, y);
                _txtLog.Size = new Size(width, 420);
                this.Controls.Add(_txtLog);
            }
        }



        internal void LoadTestImageFiles()
        {
            if (Directory.Exists(_testImagesPath))
            {
                var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp" };
                _testImageFiles = Directory.GetFiles(_testImagesPath)
                    .Where(file => extensions.Contains(Path.GetExtension(file).ToLower()))
                    .OrderBy(f => f)
                    .ToList();

                _currentTestImageIndex = 0;
            }
            else
            {
                _testImageFiles.Clear();
                _currentTestImageIndex = 0;
            }
        }

        internal void LoadAndInspectTestImage(int step)
        {
            if (!_isTestModeEnabled || _testImageFiles.Count == 0) return;

            _currentTestImageIndex += step;
            if (_currentTestImageIndex < 0) _currentTestImageIndex = _testImageFiles.Count - 1;
            if (_currentTestImageIndex >= _testImageFiles.Count) _currentTestImageIndex = 0;



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
                    if (!binImg.Empty() && _pictureBoxDebug != null && !_pictureBoxDebug.IsDisposed)
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

            if (_nudOuterTargetX != null) m.TargetOuterXOffsetMm = (double)_nudOuterTargetX.Value;
            if (_nudOuterOffsetX != null) m.OuterOffsetToleranceMm = (double)_nudOuterOffsetX.Value;
            if (_nudOuterTargetA != null) m.TargetOuterAngleDeg = (double)_nudOuterTargetA.Value;
            if (_nudOuterOffsetA != null) m.OuterAngleToleranceDeg = (double)_nudOuterOffsetA.Value;

            m.BtmMeasureRoi = new CvRect((int)_nudBtmRoiX.Value, (int)_nudBtmRoiY.Value, (int)_nudBtmRoiW.Value, (int)_nudBtmRoiH.Value);
            m.BtmInnerLeftRoi = new CvRect((int)_nudBtmInnerLX.Value, (int)_nudBtmInnerLY.Value, (int)_nudBtmInnerLW.Value, (int)_nudBtmInnerLH.Value);
            m.BtmInnerRightRoi = new CvRect((int)_nudBtmInnerRX.Value, (int)_nudBtmInnerRY.Value, (int)_nudBtmInnerRW.Value, (int)_nudBtmInnerRH.Value);

            m.HolesRoi = new CvRect((int)_nudHolesX.Value, (int)_nudHolesY.Value, (int)_nudHolesW.Value, (int)_nudHolesH.Value);
            m.MinHoleArea = (int)_nudMinHoleArea.Value; m.MaxHoleArea = (int)_nudMaxHoleArea.Value; m.MinCircularity = (double)_nudMinCircularity.Value;
            m.SplitBoundaryX = (int)_nudSplitX.Value; m.SplitBoundaryY = (int)_nudSplitY.Value;
            m.ThreshTopLeft = (int)_nudThreshTL.Value; m.ThreshTopRight = (int)_nudThreshTR.Value; m.ThreshBtmLeft = (int)_nudThreshBL.Value; m.ThreshBtmRight = (int)_nudThreshBR.Value;

            m.JigLeftRoi = new CvRect((int)_nudJigLX.Value, (int)_nudJigLY.Value, (int)_nudJigLW.Value, (int)_nudJigLH.Value);
            m.JigRightRoi = new CvRect((int)_nudJigRX.Value, (int)_nudJigRY.Value, (int)_nudJigRW.Value, (int)_nudJigRH.Value);

            if (_nudJigTarget != null) m.TargetJigDistanceMm = (double)_nudJigTarget.Value;
            if (_nudJigTolerance != null) m.JigToleranceMm = (double)_nudJigTolerance.Value;
            m.PixelToMmRatio = (double)_nudPixelToMm.Value;
            if (_nudTargetXOffset != null) m.TargetXOffsetMm = (double)_nudTargetXOffset.Value;
            if (_nudOffsetTolerance != null) m.OffsetToleranceMm = (double)_nudOffsetTolerance.Value;
            if (_nudTargetAngle != null) m.TargetAngleDeg = (double)_nudTargetAngle.Value;
            if (_nudAngleTolerance != null) m.AngleToleranceDeg = (double)_nudAngleTolerance.Value;
        }

        private void LoadSettingsToUI()
        {
            _isUpdatingUI = true;
            try
            {
                var m = _measurement;

                if (_chkEnableJigCheck != null) _chkEnableJigCheck.Checked = m.EnableJigCheck;
                if (_chkEnableOuterTiltCheck != null) _chkEnableOuterTiltCheck.Checked = m.EnableOuterTiltCheck;
                if (_chkEnableHoleCheck != null) _chkEnableHoleCheck.Checked = m.EnableHoleCheck;

                _nudTiltLX.Value = m.TiltLeftRoi.X; _nudTiltLY.Value = m.TiltLeftRoi.Y; _nudTiltLW.Value = m.TiltLeftRoi.Width; _nudTiltLH.Value = m.TiltLeftRoi.Height;
                _nudTiltRX.Value = m.TiltRightRoi.X; _nudTiltRY.Value = m.TiltRightRoi.Y; _nudTiltRW.Value = m.TiltRightRoi.Width; _nudTiltRH.Value = m.TiltRightRoi.Height;
                _nudThreshOuterL.Value = m.ThreshOuterL; _nudThreshOuterR.Value = m.ThreshOuterR;

                _nudThreshBtmInnerL.Value = m.ThreshBtmInnerL; _nudThreshBtmInnerR.Value = m.ThreshBtmInnerR;

                if (_nudOuterTargetX != null) _nudOuterTargetX.Value = (decimal)m.TargetOuterXOffsetMm;
                if (_nudOuterOffsetX != null) _nudOuterOffsetX.Value = (decimal)m.OuterOffsetToleranceMm;
                if (_nudOuterTargetA != null) _nudOuterTargetA.Value = (decimal)m.TargetOuterAngleDeg;
                if (_nudOuterOffsetA != null) _nudOuterOffsetA.Value = (decimal)m.OuterAngleToleranceDeg;

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

                if (_nudJigTarget != null) _nudJigTarget.Value = (decimal)m.TargetJigDistanceMm;
                if (_nudJigTolerance != null) _nudJigTolerance.Value = (decimal)m.JigToleranceMm;
                if (_nudPixelToMm != null) _nudPixelToMm.Value = (decimal)m.PixelToMmRatio;
                if (_nudTargetXOffset != null) _nudTargetXOffset.Value = (decimal)m.TargetXOffsetMm;
                if (_nudOffsetTolerance != null) _nudOffsetTolerance.Value = (decimal)m.OffsetToleranceMm;
                if (_nudTargetAngle != null) _nudTargetAngle.Value = (decimal)m.TargetAngleDeg;
                if (_nudAngleTolerance != null) _nudAngleTolerance.Value = (decimal)m.AngleToleranceDeg;
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

                if (_cmbTriggerMode != null) _cmbTriggerMode.SelectedIndex = _triggerOnBright ? 0 : 1;
                if (_cmbSaveMode != null) _cmbSaveMode.SelectedIndex = _saveMode;
                if (_nudTriggerThreshold != null) _nudTriggerThreshold.Value = (decimal)_triggerThreshold;
                if (_nudStabilityDuration != null) _nudStabilityDuration.Value = _stabilityDurationMs;
                if (_nudPlcDelayMs != null) _nudPlcDelayMs.Value = _plcDelayMs;
                if (_nudRetryCount != null) _nudRetryCount.Value = _maxRetryCount;
                if (_nudRetryDelayMs != null) _nudRetryDelayMs.Value = _retryDelayMs;
                if (_nudAutoStartCount != null) _nudAutoStartCount.Value = _autoStartCount;
                if (_nudResetThreshold != null) _nudResetThreshold.Value = (decimal)_resetThreshold;
                if (_nudRoiX != null) _nudRoiX.Value = _roi.X;
                if (_nudRoiY != null) _nudRoiY.Value = _roi.Y;
                if (_nudRoiW != null) _nudRoiW.Value = _roi.Width;
                if (_nudRoiH != null) _nudRoiH.Value = _roi.Height;
                if (_nudSaveRoiX != null) _nudSaveRoiX.Value = _saveRoi.X;
                if (_nudSaveRoiY != null) _nudSaveRoiY.Value = _saveRoi.Y;
                if (_nudSaveRoiW != null) _nudSaveRoiW.Value = _saveRoi.Width;
                if (_nudSaveRoiH != null) _nudSaveRoiH.Value = _saveRoi.Height;
                if (_nudLogKeepDays != null) _nudLogKeepDays.Value = _logKeepDays;

                LoadSettingsToUI();
            }
            catch { }
            finally { _isLoadingConfig = false; }
        }

        internal void SaveConfig()
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