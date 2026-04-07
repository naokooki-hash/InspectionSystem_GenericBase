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
        // --- 状態遷移定数 ---
        private const int STATE_WAITING = 0;
        private const int STATE_STABILIZING = 1;
        private const int STATE_COOLING = 2; // PLCモード等での冷却(連続トリガー防止)期間

        // --- コアコンポーネント ---
        private TeliCamera _camera;
        private MeasurementCore _measurement;
        private PlcCommunicator _plc;
        private AppSettings _appSettings; // 新規追加: JSON設定データ

        // --- UIコントロール ---
        private PictureBox _pictureBox, _pictureBoxDebug;
        private TabControl _tabControl;
        private Label _lblStatus, _lblBrightness, _lblBigResult, _lblTotal, _lblOk, _lblNg, _lblCurrentHoleDistPx;
        private CheckBox _chkShowOverlay;
        private ComboBox _cmbTriggerMode, _cmbSaveMode;

        // 数値入力コントロール群
        private NumericUpDown _nudTriggerThreshold, _nudStabilityDuration, _nudResetThreshold;
        private NumericUpDown _nudRoiX, _nudRoiY, _nudRoiW, _nudRoiH;
        private NumericUpDown _nudSaveRoiX, _nudSaveRoiY, _nudSaveRoiW, _nudSaveRoiH;
        private NumericUpDown _nudBtmRoiX, _nudBtmRoiY, _nudBtmRoiW, _nudBtmRoiH;
        private NumericUpDown _nudHolesX, _nudHolesY, _nudHolesW, _nudHolesH;
        private NumericUpDown _nudMinHoleArea, _nudMaxHoleArea, _nudHoleThresh, _nudMinCircularity;
        private NumericUpDown _nudJigLX, _nudJigLY, _nudJigLW, _nudJigLH;
        private NumericUpDown _nudJigRX, _nudJigRY, _nudJigRW, _nudJigRH;
        private NumericUpDown _nudJigTarget, _nudJigTolerance, _nudPixelToMm;
        private NumericUpDown _nudTargetXOffset, _nudOffsetTolerance, _nudTargetAngle, _nudAngleTolerance, _nudActualWidthMm;

        private Button _btnCalcRatio;

        // --- 状態管理用変数 ---
        private int _currentState = STATE_WAITING;
        private DateTime _stabilityStartTime;
        private DateTime _cooldownStartTime;
        private int _cooldownDurationMs = 500; // 冷却期間(ms)

        private CvRect _roi = new CvRect(300, 200, 100, 100);
        private CvRect _saveRoi = new CvRect(100, 50, 440, 380);

        private bool _requestManualTest = false;
        private bool _isProcessing = false;
        private bool _isLoadingConfig = false;
        private bool _isUiLoaded = false;

        // --- PLCトリガー監視用 ---
        private bool _isMonitoring = false;
        private bool _plcTriggerReceived = false;

        private int _totalCount = 0, _okCount = 0, _ngCount = 0, _saveMode = 0;
        private int _stabilityDurationMs = 300, _pendingSaveResult = -1;
        private bool _triggerOnBright = true;
        private double _triggerThreshold = 100.0, _resetThreshold = 50.0;

        private string _logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public Form1()
        {
            // 1. JSON設定の読み込み
            _appSettings = AppSettings.Load();

            // 2. コアコンポーネントの初期化
            _camera = new TeliCamera();
            _measurement = new MeasurementCore();
            _plc = new PlcCommunicator(_appSettings); // 設定を渡す

            // 3. UIとイベントの設定
            InitializeCustomUI();
            _camera.OnFrameCaptured += Camera_OnFrameCaptured;

            // 4. 古い設定ファイル(ROI等)の読み込み
            LoadConfig();

            // 5. フォームイベントのフック
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
        }

        // =========================================================
        // UI初期化処理 (可読性向上のため改行・整理)
        // =========================================================
        private void InitializeCustomUI()
        {
            this.Text = "Punching Metal Auto Inspection System (Bulletproof)";
            this.Size = new Size(1100, 850);
            this.StartPosition = FormStartPosition.CenterScreen;

            // メインカメラ画像表示領域
            _pictureBox = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(640, 480),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(_pictureBox);

            // ステータス表示
            int px = 660;
            _lblStatus = new Label
            {
                Text = "Status: WAITING",
                Location = new Point(px, 10),
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold)
            };
            _lblBrightness = new Label
            {
                Text = "Brightness: 0.0",
                Location = new Point(px, 40),
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 12)
            };
            this.Controls.Add(_lblStatus);
            this.Controls.Add(_lblBrightness);

            // タブコントロール
            _tabControl = new TabControl
            {
                Location = new Point(px, 80),
                Size = new Size(410, 710),
                Font = new Font(this.Font.FontFamily, 10)
            };
            this.Controls.Add(_tabControl);

            TabPage t1 = new TabPage("運用 (Main)");
            InitializeMainTab(t1);
            _tabControl.TabPages.Add(t1);

            TabPage t2 = new TabPage("設定 (Settings)");
            t2.AutoScroll = true;
            InitializeSettingsTab(t2);
            _tabControl.TabPages.Add(t2);

            TabPage t3 = new TabPage("検査設定 (Inspection)");
            t3.AutoScroll = true;
            InitializeInspectionTab(t3);
            _tabControl.TabPages.Add(t3);

            TabPage t4 = new TabPage("画像確認 (Debug)");
            InitializeDebugTab(t4);
            _tabControl.TabPages.Add(t4);
        }

        private void InitializeMainTab(TabPage tab)
        {
            int y = 10;
            _chkShowOverlay = new CheckBox { Text = "計測パラメータを表示する", Location = new Point(10, y), AutoSize = true, Checked = true };
            tab.Controls.Add(_chkShowOverlay);
            y += 40;

            _lblBigResult = new Label
            {
                Text = "READY",
                Location = new Point(10, y),
                Size = new Size(370, 80),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(this.Font.FontFamily, 36, FontStyle.Bold),
                BackColor = Color.LightGray
            };
            tab.Controls.Add(_lblBigResult);
            y += 100;

            // カウンターグループ
            GroupBox gp = new GroupBox { Text = "生産カウンター", Location = new Point(10, y), Size = new Size(370, 120) };
            _lblTotal = new Label { Text = "総検査数 : 0", Location = new Point(20, 30), AutoSize = true };
            _lblOk = new Label { Text = "良品 (OK): 0", Location = new Point(20, 60), AutoSize = true, ForeColor = Color.Green, Font = new Font(this.Font, FontStyle.Bold) };
            _lblNg = new Label { Text = "不良 (NG): 0", Location = new Point(20, 90), AutoSize = true, ForeColor = Color.Red, Font = new Font(this.Font, FontStyle.Bold) };
            gp.Controls.Add(_lblTotal); gp.Controls.Add(_lblOk); gp.Controls.Add(_lblNg);
            tab.Controls.Add(gp);
            y += 140;

            Button btnReset = new Button { Text = "カウンターリセット", Location = new Point(10, y), Size = new Size(370, 30) };
            btnReset.Click += (s, e) => { _totalCount = _okCount = _ngCount = 0; UpdateCounterDisplay(); };
            tab.Controls.Add(btnReset);
            y += 60;

            Button btnTest = new Button { Text = "手動検査テスト", Location = new Point(10, y), Size = new Size(370, 50), BackColor = Color.LightSkyBlue, Font = new Font(this.Font.FontFamily, 12, FontStyle.Bold) };
            btnTest.Click += (s, e) => _requestManualTest = true;
            tab.Controls.Add(btnTest);
        }

        private void InitializeSettingsTab(TabPage tab)
        {
            int y = 10, lw = 150, cw = 100, lh = 28;

            // ヘルパー関数: 数値入力欄の追加
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI();
                tab.Controls.Add(n);
                y += lh;
            }

            // --- トリガー設定 ---
            tab.Controls.Add(new Label { Text = "--- トリガー設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;

            _cmbTriggerMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbTriggerMode.Items.AddRange(new object[] { "明転 (>)", "暗転 (<)" });
            _cmbTriggerMode.SelectedIndex = _triggerOnBright ? 0 : 1;
            _cmbTriggerMode.SelectedIndexChanged += (s, e) => { if (!_isLoadingConfig) _triggerOnBright = _cmbTriggerMode.SelectedIndex == 0; };

            tab.Controls.Add(new Label { Text = "Visual Trigger:", Location = new Point(10, y + 2), Size = new Size(lw, 20) });
            tab.Controls.Add(_cmbTriggerMode); y += lh;

            AddN("Trigger Thresh:", ref _nudTriggerThreshold, 0, 255, (decimal)_triggerThreshold, 1);
            AddN("Stability (ms):", ref _nudStabilityDuration, 0, 5000, _stabilityDurationMs);
            AddN("Reset Thresh:", ref _nudResetThreshold, 0, 255, (decimal)_resetThreshold, 1);
            y += 10;

            // --- ROI 設定 ---
            tab.Controls.Add(new Label { Text = "--- 輝度監視 ROI 設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddN("ROI X:", ref _nudRoiX, 0, 3000, _roi.X); AddN("ROI Y:", ref _nudRoiY, 0, 3000, _roi.Y);
            AddN("ROI W:", ref _nudRoiW, 1, 3000, _roi.Width); AddN("ROI H:", ref _nudRoiH, 1, 3000, _roi.Height);
            y += 10;

            // --- 画像保存設定 ---
            tab.Controls.Add(new Label { Text = "--- 画像保存設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            _cmbSaveMode = new ComboBox { Location = new Point(10 + lw, y), Size = new Size(cw + 50, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSaveMode.Items.AddRange(new object[] { "0: 保存しない", "1: NGのみ保存", "2: 全て保存" });
            _cmbSaveMode.SelectedIndex = _saveMode;
            _cmbSaveMode.SelectedIndexChanged += (s, e) => { if (!_isLoadingConfig) _saveMode = _cmbSaveMode.SelectedIndex; };

            tab.Controls.Add(new Label { Text = "保存モード:", Location = new Point(10, y + 2), Size = new Size(lw, 20) });
            tab.Controls.Add(_cmbSaveMode); y += lh;

            AddN("Save ROI X:", ref _nudSaveRoiX, 0, 3000, _saveRoi.X); AddN("Save ROI Y:", ref _nudSaveRoiY, 0, 3000, _saveRoi.Y);
            AddN("Save ROI W:", ref _nudSaveRoiW, 1, 3000, _saveRoi.Width); AddN("Save ROI H:", ref _nudSaveRoiH, 1, 3000, _saveRoi.Height);
            y += 20;

            Button btnSave = new Button { Text = "設定を保存する (Save)", Location = new Point(10, y), Size = new Size(360, 40), BackColor = Color.LightGreen };
            btnSave.Click += (s, e) => { SaveConfig(); MessageBox.Show("保存しました。"); };
            tab.Controls.Add(btnSave);
        }

        private void InitializeInspectionTab(TabPage tab)
        {
            int y = 10, lw = 160, cw = 100, lh = 28;

            // ヘルパー関数: 単一の数値入力
            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp };
                n.ValueChanged += (s, e) => UpdateSettingsFromUI(); tab.Controls.Add(n); y += lh;
            }

            // ヘルパー関数: 矩形(Rect)入力
            void AddRect(string txt, ref NumericUpDown nx, ref NumericUpDown ny, ref NumericUpDown nw, ref NumericUpDown nh, CvRect r)
            {
                tab.Controls.Add(new Label { Text = txt, Location = new Point(10, y + 2), Size = new Size(110, 20) });
                int sx = 120, step = 65, boxW = 60;
                nx = new NumericUpDown { Location = new Point(sx, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.X };
                ny = new NumericUpDown { Location = new Point(sx + step, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.Y };
                nw = new NumericUpDown { Location = new Point(sx + step * 2, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Width };
                nh = new NumericUpDown { Location = new Point(sx + step * 3, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Height };

                nx.ValueChanged += (s, e) => UpdateSettingsFromUI();
                ny.ValueChanged += (s, e) => UpdateSettingsFromUI();
                nw.ValueChanged += (s, e) => UpdateSettingsFromUI();
                nh.ValueChanged += (s, e) => UpdateSettingsFromUI();

                tab.Controls.Add(nx); tab.Controls.Add(ny); tab.Controls.Add(nw); tab.Controls.Add(nh); y += lh;
            }

            tab.Controls.Add(new Label { Text = "--- 傾き検出基準穴 設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddRect("基準穴 ROI:", ref _nudHolesX, ref _nudHolesY, ref _nudHolesW, ref _nudHolesH, _measurement.HolesRoi);
            AddN("穴 最小面積:", ref _nudMinHoleArea, 0, 10000, _measurement.MinHoleArea);
            AddN("穴 最大面積:", ref _nudMaxHoleArea, 0, 100000, _measurement.MaxHoleArea);
            AddN("真円度しきい値:", ref _nudMinCircularity, 0, 1, (decimal)_measurement.MinCircularity, 2); y += 10;

            tab.Controls.Add(new Label { Text = "--- エッジ間距離 測定設定 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddRect("左エッジ ROI:", ref _nudJigLX, ref _nudJigLY, ref _nudJigLW, ref _nudJigLH, _measurement.JigLeftRoi);
            AddRect("右エッジ ROI:", ref _nudJigRX, ref _nudJigRY, ref _nudJigRW, ref _nudJigRH, _measurement.JigRightRoi);
            AddN("エッジ目標距離(px):", ref _nudJigTarget, 0, 3000, (decimal)_measurement.TargetJigDistancePx, 1);
            AddN("エッジ許容誤差(px):", ref _nudJigTolerance, 0, 500, (decimal)_measurement.JigTolerancePx, 1); y += 10;

            tab.Controls.Add(new Label { Text = "--- 下部測定 ROI ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkGoldenrod }); y += 22;
            AddRect("Btm 線 ROI:", ref _nudBtmRoiX, ref _nudBtmRoiY, ref _nudBtmRoiW, ref _nudBtmRoiH, _measurement.BtmMeasureRoi); y += 10;

            tab.Controls.Add(new Label { Text = "--- キャリブレーション (穴間基準) ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.DarkOrange }); y += 22;
            _lblCurrentHoleDistPx = new Label { Text = "現在の穴間距離: 0.0 px", Location = new Point(10, y), Size = new Size(300, 20), ForeColor = Color.DarkOrange, Font = new Font(this.Font, FontStyle.Bold) };
            tab.Controls.Add(_lblCurrentHoleDistPx); y += lh;
            _nudActualWidthMm = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0.1M, Maximum = 500, Value = 50, DecimalPlaces = 2 };
            tab.Controls.Add(new Label { Text = "実測の穴間距離(mm):", Location = new Point(10, y + 2), Size = new Size(lw, 20) }); tab.Controls.Add(_nudActualWidthMm); y += lh;

            _btnCalcRatio = new Button { Text = "比率を自動計算", Location = new Point(10, y), Size = new Size(360, 30), BackColor = Color.LightYellow };
            _btnCalcRatio.Click += (s, e) => {
                if (_measurement.LastHoleDistancePx <= 0) { MessageBox.Show("先にテスト実行して穴を検出させてください。"); return; }
                _nudPixelToMm.Value = _nudActualWidthMm.Value / (decimal)_measurement.LastHoleDistancePx;
                MessageBox.Show("更新しました。");
            };
            tab.Controls.Add(_btnCalcRatio); y += 45;

            tab.Controls.Add(new Label { Text = "--- 検査パラメータ ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            AddN("Pixel->mm比率:", ref _nudPixelToMm, 0.0001M, 1, (decimal)_measurement.PixelToMmRatio, 5);
            AddN("目標 Xずれ(mm):", ref _nudTargetXOffset, -100, 100, (decimal)_measurement.TargetXOffsetMm, 2);
            AddN("Xずれ許容(mm):", ref _nudOffsetTolerance, 0, 50, (decimal)_measurement.OffsetToleranceMm, 2);
            AddN("目標 Θ(deg):", ref _nudTargetAngle, -180, 180, (decimal)_measurement.TargetAngleDeg, 2);
            AddN("Θ許容(deg):", ref _nudAngleTolerance, 0, 90, (decimal)_measurement.AngleToleranceDeg, 2);
        }

        private void InitializeDebugTab(TabPage tab)
        {
            int y = 10, lw = 150, cw = 100, lh = 28;
            _pictureBoxDebug = new PictureBox { Location = new Point(10, y), Size = new Size(380, 280), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(_pictureBoxDebug); y += 300;

            tab.Controls.Add(new Label { Text = "--- 二値化 閾値調整 ---", Location = new Point(10, y), AutoSize = true, ForeColor = Color.Blue }); y += 22;
            tab.Controls.Add(new Label { Text = "Hole Binary Thresh:", Location = new Point(10, y + 2), Size = new Size(lw, 20) });
            _nudHoleThresh = new NumericUpDown { Location = new Point(10 + lw, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 255, Value = _measurement.HoleThreshold };
            _nudHoleThresh.ValueChanged += (s, e) => UpdateSettingsFromUI();
            tab.Controls.Add(_nudHoleThresh); y += lh;

            tab.Controls.Add(new Label { Text = "※このタブでは「画像保存設定(Save ROI)」の範囲を\n二値化してリアルタイム表示します。", Location = new Point(10, y + 20), AutoSize = true, ForeColor = Color.DarkGreen });
        }

        // =========================================================
        // 起動・終了処理
        // =========================================================
        private void Form1_Load(object sender, EventArgs e)
        {
            //_plc.Connect(); // PLCへの接続試行
            _isUiLoaded = true;
            if (_camera.Initialize()) _camera.StartCapture();

            // PLCトリガーのバックグラウンド監視を開始
            _ = MonitorPlcTriggerAsync();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _isMonitoring = false; // 監視ループ停止
            _isUiLoaded = false;

            _camera.StopCapture();
            _camera.Dispose();
            _plc.Disconnect(); // Dispose() から Disconnect() に修正

            SaveConfig();
        }

        // =========================================================
        // PLCトリガー監視ループ
        // =========================================================
        private async Task MonitorPlcTriggerAsync()
        {
            _isMonitoring = true;
            while (_isMonitoring && !this.IsDisposed)
            {
                if (_appSettings.TriggerMode == "Plc")
                {
                    // ★追加: 繋がっていなければ、裏側でこっそり接続を試みる
                    if (!_plc.IsConnected)
                    {
                        await Task.Run(() => _plc.Connect());
                    }

                    // 繋がっていれば、トリガーを読み取る
                    if (_plc.IsConnected)
                    {
                        // UIを固めないよう別スレッドで読出し
                        int triggerValue = await Task.Run(() => _plc.ReadDevice(_appSettings.ReadDeviceAddress));

                        if (triggerValue == 1 && _currentState == STATE_WAITING)
                        {
                            _plcTriggerReceived = true;
                        }
                    }
                }
                await Task.Delay(_appSettings.InspectionIntervalMs);
            }
        }

        // =========================================================
        // スレッドセーフなUI操作ヘルパー
        // =========================================================
        private void SafeInvoke(Action action)
        {
            if (!_isUiLoaded || this.IsDisposed || !this.IsHandleCreated || this.Disposing) return;
            try { this.Invoke(new MethodInvoker(action)); } catch { }
        }

        private void SafeBeginInvoke(Action action)
        {
            if (!_isUiLoaded || this.IsDisposed || !this.IsHandleCreated || this.Disposing) return;
            try { this.BeginInvoke(new MethodInvoker(action)); } catch { }
        }

        // =========================================================
        // カメラ画像取得とメイン処理フロー
        // =========================================================
        private void Camera_OnFrameCaptured(object sender, Mat frame)
        {
            if (!_isUiLoaded || this.IsDisposed || frame == null || frame.Empty()) return;
            if (_isProcessing) { frame.Dispose(); return; }
            _isProcessing = true;

            Task.Run(() => {
                try
                {
                    bool isDebug = false;
                    SafeInvoke(() => { isDebug = (_tabControl.SelectedIndex == 3); });

                    // 輝度は常に計算(UI表示用)
                    double b = _measurement.CalculateBrightness(frame, _roi);

                    // 手動テスト要求があれば即座に検査
                    if (_requestManualTest)
                    {
                        _requestManualTest = false;
                        int manualResult = _measurement.Inspect(frame, _saveRoi, isDebug);
                        SafeInvoke(() => UpdateResultDisplay(manualResult, true));
                        _pendingSaveResult = manualResult;
                    }

                    // 状態遷移と自動検査のトリガー判定
                    UpdateStateMachine(frame, b, isDebug);

                    SafeBeginInvoke(() => {
                        UpdateUIDisplay(frame, b, isDebug);
                        frame.Dispose();
                        _isProcessing = false;
                    });
                }
                catch
                {
                    if (frame != null && !frame.IsDisposed) frame.Dispose();
                    _isProcessing = false;
                }
            });
        }

        // =========================================================
        // 状態遷移と検査実行（PLCモード / Visualモード 共存）
        // =========================================================
        private void UpdateStateMachine(Mat frame, double b, bool isDebug)
        {
            bool isTriggered = false;
            bool isReset = false;

            // モードに応じたトリガーの評価
            if (_appSettings.TriggerMode == "Plc")
            {
                isTriggered = _plcTriggerReceived;
                isReset = false; // PLCは一瞬の信号なので「外れる」概念はない
            }
            else // Visualモード(輝度変化)
            {
                isTriggered = _triggerOnBright ? (b > _triggerThreshold) : (b < _triggerThreshold);
                isReset = _triggerOnBright ? (b < _resetThreshold) : (b > _resetThreshold);
            }

            switch (_currentState)
            {
                case STATE_WAITING:
                    if (isTriggered)
                    {
                        if (_appSettings.TriggerMode == "Plc")
                        {
                            // PLCモード: 安定待ちをスキップして即検査！
                            _plcTriggerReceived = false;
                            ExecuteInspection(frame, isDebug);

                            _currentState = STATE_COOLING;
                            _cooldownStartTime = DateTime.Now;
                            SafeInvoke(() => lblStateUpdate("TESTING (PLC)", Color.Yellow));
                        }
                        else
                        {
                            // Visualモード: 安定待ちへ移行
                            _currentState = STATE_STABILIZING;
                            _stabilityStartTime = DateTime.Now;
                            SafeInvoke(() => lblStateUpdate("TESTING", Color.Yellow));
                        }
                    }
                    break;

                case STATE_STABILIZING: // Visualモード専用の安定待ち
                    if (isReset)
                    {
                        _currentState = STATE_WAITING;
                        SafeInvoke(() => lblStateUpdate("READY", Color.LightGray));
                    }
                    else if ((DateTime.Now - _stabilityStartTime).TotalMilliseconds > _stabilityDurationMs)
                    {
                        ExecuteInspection(frame, isDebug);

                        _currentState = STATE_COOLING;
                        _cooldownStartTime = DateTime.Now;
                    }
                    break;

                case STATE_COOLING: // 検査直後のクールダウン(連続検知防止)
                    if ((DateTime.Now - _cooldownStartTime).TotalMilliseconds > _cooldownDurationMs)
                    {
                        if (_appSettings.TriggerMode == "Plc" || isReset)
                        {
                            _currentState = STATE_WAITING;
                            SafeInvoke(() => lblStateUpdate("READY", Color.LightGray));
                        }
                    }
                    break;
            }
        }

        // ラベル更新用の小さなヘルパー
        private void lblStateUpdate(string text, Color color)
        {
            if (_lblBigResult != null && !_lblBigResult.IsDisposed)
            {
                _lblBigResult.Text = text;
                _lblBigResult.BackColor = color;
            }
        }

        // =========================================================
        // 検査の実行とハンドシェイク
        // =========================================================
        private void ExecuteInspection(Mat frame, bool isDebug)
        {
            // 検査ロジックの実行
            int inspectResult = _measurement.Inspect(frame, _saveRoi, isDebug);

            // 判定結果をD100に書き込む (true=OK, false=NG)
            _plc.SendResult(inspectResult == 1);

            // 【ハンドシェイク】PLCモードの場合、トリガー(D101)を0に戻す
            if (_appSettings.TriggerMode == "Plc")
            {
                Task.Run(() => _plc.WriteDevice(_appSettings.ReadDeviceAddress, 0));
            }

            SafeInvoke(() => UpdateResultDisplay(inspectResult, false));
            _pendingSaveResult = inspectResult; // 画像保存用に結果を保持
        }

        // =========================================================
        // UI描画・結果表示系
        // =========================================================
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
                    Cv2.Rectangle(disp, _measurement.HolesRoi, Scalar.Orange, 2);
                    Cv2.Rectangle(disp, _measurement.JigLeftRoi, Scalar.Yellow, 2);
                    Cv2.Rectangle(disp, _measurement.JigRightRoi, Scalar.Yellow, 2);
                    Cv2.Rectangle(disp, _saveRoi, Scalar.LightSkyBlue, 1);
                    _measurement.DrawOverlay(disp);
                }

                // 画像保存要求の処理
                if (_pendingSaveResult != -1)
                {
                    if (_saveMode == 2 || (_saveMode == 1 && _pendingSaveResult != 1))
                        SaveInspectionImage(disp, _pendingSaveResult);
                    _pendingSaveResult = -1;
                }

                Bitmap bmp = BitmapConverter.ToBitmap(disp);
                Image old = _pictureBox.Image;
                _pictureBox.Image = bmp;
                old?.Dispose();
            }

            if (isDebug)
            {
                using (Mat binImg = new Mat())
                {
                    _measurement.GetDebugImage(binImg);
                    if (!binImg.Empty())
                    {
                        Bitmap bmpD = BitmapConverter.ToBitmap(binImg);
                        Image oldD = _pictureBoxDebug.Image;
                        _pictureBoxDebug.Image = bmpD;
                        oldD?.Dispose();
                    }
                }
            }

            if (_lblCurrentHoleDistPx != null && !_lblCurrentHoleDistPx.IsDisposed && _measurement.LastHoleDistancePx > 0)
                _lblCurrentHoleDistPx.Text = "現在の穴間距離: " + _measurement.LastHoleDistancePx.ToString("F1") + " px";

            if (_lblStatus != null && !_lblStatus.IsDisposed)
            {
                _lblStatus.Text = "Status: " + (_currentState == 0 ? "WAITING" : (_currentState == 1 ? "STABILIZING" : "COOLING"));
                _lblStatus.ForeColor = _currentState == 0 ? Color.Gray : (_currentState == 1 ? Color.Goldenrod : Color.LimeGreen);
            }
            if (_lblBrightness != null && !_lblBrightness.IsDisposed)
                _lblBrightness.Text = "Brightness: " + b.ToString("F1");
        }

        private void SaveInspectionImage(Mat img, int res)
        {
            try
            {
                string dir = Path.Combine(_logDirPath, DateTime.Now.ToString("yyyyMMdd"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string resStr = (res == 1) ? "OK" : "NG";
                string fileName = string.Format("{0:HHmmss_fff}_{1}.jpg", DateTime.Now, resStr);
                string path = Path.Combine(dir, fileName);

                CvRect crop = _saveRoi & new CvRect(0, 0, img.Width, img.Height);
                using (Mat cropped = new Mat(img, crop))
                using (Mat resized = new Mat())
                {
                    Cv2.Resize(cropped, resized, new CvSize(cropped.Width / 2, cropped.Height / 2));
                    Cv2.ImWrite(path, resized);
                }
            }
            catch { }
        }

        private void UpdateResultDisplay(int res, bool manual)
        {
            if (_lblBigResult == null || _lblBigResult.IsDisposed) return;

            _lblBigResult.Text = res == 1 ? "OK" : "NG";
            _lblBigResult.BackColor = res == 1 ? Color.LimeGreen : Color.Red;

            if (!manual)
            {
                _totalCount++;
                if (res == 1) _okCount++; else _ngCount++;
                UpdateCounterDisplay();
            }
        }

        private void UpdateCounterDisplay()
        {
            if (_lblTotal == null || _lblTotal.IsDisposed) return;
            _lblTotal.Text = "総検査数 : " + _totalCount;
            _lblOk.Text = "良品 (OK): " + _okCount;
            _lblNg.Text = "不良 (NG): " + _ngCount;
        }

        // =========================================================
        // 古い設定ファイルの反映と保存 (可読性向上のため改行)
        // =========================================================
        private void UpdateSettingsFromUI()
        {
            if (_isLoadingConfig) return;
            _triggerThreshold = (double)_nudTriggerThreshold.Value;
            _stabilityDurationMs = (int)_nudStabilityDuration.Value;
            _resetThreshold = (double)_nudResetThreshold.Value;

            _roi = new CvRect((int)_nudRoiX.Value, (int)_nudRoiY.Value, (int)_nudRoiW.Value, (int)_nudRoiH.Value);
            _saveRoi = new CvRect((int)_nudSaveRoiX.Value, (int)_nudSaveRoiY.Value, (int)_nudSaveRoiW.Value, (int)_nudSaveRoiH.Value);

            _measurement.BtmMeasureRoi = new CvRect((int)_nudBtmRoiX.Value, (int)_nudBtmRoiY.Value, (int)_nudBtmRoiW.Value, (int)_nudBtmRoiH.Value);
            _measurement.HolesRoi = new CvRect((int)_nudHolesX.Value, (int)_nudHolesY.Value, (int)_nudHolesW.Value, (int)_nudHolesH.Value);
            _measurement.MinHoleArea = (int)_nudMinHoleArea.Value;
            _measurement.MaxHoleArea = (int)_nudMaxHoleArea.Value;
            _measurement.MinCircularity = (double)_nudMinCircularity.Value;
            _measurement.HoleThreshold = (int)_nudHoleThresh.Value;

            _measurement.JigLeftRoi = new CvRect((int)_nudJigLX.Value, (int)_nudJigLY.Value, (int)_nudJigLW.Value, (int)_nudJigLH.Value);
            _measurement.JigRightRoi = new CvRect((int)_nudJigRX.Value, (int)_nudJigRY.Value, (int)_nudJigRW.Value, (int)_nudJigRH.Value);

            _measurement.TargetJigDistancePx = (double)_nudJigTarget.Value;
            _measurement.JigTolerancePx = (double)_nudJigTolerance.Value;
            _measurement.PixelToMmRatio = (double)_nudPixelToMm.Value;
            _measurement.TargetXOffsetMm = (double)_nudTargetXOffset.Value;
            _measurement.OffsetToleranceMm = (double)_nudOffsetTolerance.Value;
            _measurement.TargetAngleDeg = (double)_nudTargetAngle.Value;
            _measurement.AngleToleranceDeg = (double)_nudAngleTolerance.Value;
        }

        private void LoadConfig()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            if (!File.Exists(path)) return;

            _isLoadingConfig = true;
            try
            {
                var d = File.ReadAllLines(path)
                            .Select(l => l.Split('='))
                            .Where(p => p.Length == 2)
                            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

                int GetI(string k, int def) => d.TryGetValue(k, out var v) && int.TryParse(v, out int i) ? i : def;
                double GetD(string k, double def) => d.TryGetValue(k, out var v) && double.TryParse(v, out double num) ? num : def;

                _triggerOnBright = d.TryGetValue("TriggerOnBright", out var tb) ? bool.Parse(tb) : true;
                _triggerThreshold = GetD("TriggerThreshold", _triggerThreshold);
                _stabilityDurationMs = GetI("StabilityDurationMs", _stabilityDurationMs);
                _resetThreshold = GetD("ResetThreshold", _resetThreshold);
                _saveMode = GetI("SaveMode", _saveMode);

                _roi = new CvRect(GetI("RoiX", _roi.X), GetI("RoiY", _roi.Y), GetI("RoiW", _roi.Width), GetI("RoiH", _roi.Height));
                _saveRoi = new CvRect(GetI("SaveRoiX", _saveRoi.X), GetI("SaveRoiY", _saveRoi.Y), GetI("SaveRoiW", _saveRoi.Width), GetI("SaveRoiH", _saveRoi.Height));

                _measurement.HolesRoi = new CvRect(GetI("HolesX", _measurement.HolesRoi.X), GetI("HolesY", _measurement.HolesRoi.Y), GetI("HolesW", _measurement.HolesRoi.Width), GetI("HolesH", _measurement.HolesRoi.Height));
                _measurement.MinHoleArea = GetI("MinHoleArea", _measurement.MinHoleArea);
                _measurement.MaxHoleArea = GetI("MaxHoleArea", _measurement.MaxHoleArea);
                _measurement.MinCircularity = GetD("MinCirc", _measurement.MinCircularity);
                _measurement.HoleThreshold = GetI("HoleThresh", _measurement.HoleThreshold);
                _measurement.BtmMeasureRoi = new CvRect(GetI("BtmRoiX", _measurement.BtmMeasureRoi.X), GetI("BtmRoiY", _measurement.BtmMeasureRoi.Y), GetI("BtmRoiW", _measurement.BtmMeasureRoi.Width), GetI("BtmRoiH", _measurement.BtmMeasureRoi.Height));

                _measurement.JigLeftRoi = new CvRect(GetI("JigLX", _measurement.JigLeftRoi.X), GetI("JigLY", _measurement.JigLeftRoi.Y), GetI("JigLW", _measurement.JigLeftRoi.Width), GetI("JigLH", _measurement.JigLeftRoi.Height));
                _measurement.JigRightRoi = new CvRect(GetI("JigRX", _measurement.JigRightRoi.X), GetI("JigRY", _measurement.JigRightRoi.Y), GetI("JigRW", _measurement.JigRightRoi.Width), GetI("JigRH", _measurement.JigRightRoi.Height));

                _measurement.TargetJigDistancePx = GetD("JigTarget", _measurement.TargetJigDistancePx);
                _measurement.JigTolerancePx = GetD("JigTol", _measurement.JigTolerancePx);
                _measurement.PixelToMmRatio = GetD("PixelToMmRatio", _measurement.PixelToMmRatio);
                _measurement.TargetXOffsetMm = GetD("TargetXOffsetMm", _measurement.TargetXOffsetMm);
                _measurement.OffsetToleranceMm = GetD("OffsetToleranceMm", _measurement.OffsetToleranceMm);
                _measurement.TargetAngleDeg = GetD("TargetAngleDeg", _measurement.TargetAngleDeg);
                _measurement.AngleToleranceDeg = GetD("AngleToleranceDeg", _measurement.AngleToleranceDeg);

                // UIコントロールへの反映
                _cmbTriggerMode.SelectedIndex = _triggerOnBright ? 0 : 1;
                _cmbSaveMode.SelectedIndex = _saveMode;
                _nudTriggerThreshold.Value = (decimal)_triggerThreshold;
                _nudStabilityDuration.Value = _stabilityDurationMs;
                _nudResetThreshold.Value = (decimal)_resetThreshold;

                _nudRoiX.Value = _roi.X; _nudRoiY.Value = _roi.Y; _nudRoiW.Value = _roi.Width; _nudRoiH.Value = _roi.Height;
                _nudSaveRoiX.Value = _saveRoi.X; _nudSaveRoiY.Value = _saveRoi.Y; _nudSaveRoiW.Value = _saveRoi.Width; _nudSaveRoiH.Value = _saveRoi.Height;

                _nudHolesX.Value = _measurement.HolesRoi.X; _nudHolesY.Value = _measurement.HolesRoi.Y; _nudHolesW.Value = _measurement.HolesRoi.Width; _nudHolesH.Value = _measurement.HolesRoi.Height;
                _nudMinHoleArea.Value = _measurement.MinHoleArea; _nudMaxHoleArea.Value = _measurement.MaxHoleArea;
                _nudMinCircularity.Value = (decimal)_measurement.MinCircularity; _nudHoleThresh.Value = _measurement.HoleThreshold;

                _nudBtmRoiX.Value = _measurement.BtmMeasureRoi.X; _nudBtmRoiY.Value = _measurement.BtmMeasureRoi.Y; _nudBtmRoiW.Value = _measurement.BtmMeasureRoi.Width; _nudBtmRoiH.Value = _measurement.BtmMeasureRoi.Height;
                _nudJigLX.Value = _measurement.JigLeftRoi.X; _nudJigLY.Value = _measurement.JigLeftRoi.Y; _nudJigLW.Value = _measurement.JigLeftRoi.Width; _nudJigLH.Value = _measurement.JigLeftRoi.Height;
                _nudJigRX.Value = _measurement.JigRightRoi.X; _nudJigRY.Value = _measurement.JigRightRoi.Y; _nudJigRW.Value = _measurement.JigRightRoi.Width; _nudJigRH.Value = _measurement.JigRightRoi.Height;

                _nudJigTarget.Value = (decimal)_measurement.TargetJigDistancePx;
                _nudJigTolerance.Value = (decimal)_measurement.JigTolerancePx;
                _nudPixelToMm.Value = (decimal)_measurement.PixelToMmRatio;
                _nudTargetXOffset.Value = (decimal)_measurement.TargetXOffsetMm;
                _nudOffsetTolerance.Value = (decimal)_measurement.OffsetToleranceMm;
                _nudTargetAngle.Value = (decimal)_measurement.TargetAngleDeg;
                _nudAngleTolerance.Value = (decimal)_measurement.AngleToleranceDeg;
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
                    sw.WriteLine("TriggerOnBright=" + _triggerOnBright);
                    sw.WriteLine("TriggerThreshold=" + _triggerThreshold);
                    sw.WriteLine("StabilityDurationMs=" + _stabilityDurationMs);
                    sw.WriteLine("ResetThreshold=" + _resetThreshold);
                    sw.WriteLine("SaveMode=" + _saveMode);

                    sw.WriteLine("RoiX=" + _roi.X); sw.WriteLine("RoiY=" + _roi.Y); sw.WriteLine("RoiW=" + _roi.Width); sw.WriteLine("RoiH=" + _roi.Height);
                    sw.WriteLine("SaveRoiX=" + _saveRoi.X); sw.WriteLine("SaveRoiY=" + _saveRoi.Y); sw.WriteLine("SaveRoiW=" + _saveRoi.Width); sw.WriteLine("SaveRoiH=" + _saveRoi.Height);

                    sw.WriteLine("HolesX=" + _measurement.HolesRoi.X); sw.WriteLine("HolesY=" + _measurement.HolesRoi.Y); sw.WriteLine("HolesW=" + _measurement.HolesRoi.Width); sw.WriteLine("HolesH=" + _measurement.HolesRoi.Height);
                    sw.WriteLine("MinHoleArea=" + _measurement.MinHoleArea); sw.WriteLine("MaxHoleArea=" + _measurement.MaxHoleArea);
                    sw.WriteLine("MinCirc=" + _measurement.MinCircularity); sw.WriteLine("HoleThresh=" + _measurement.HoleThreshold);

                    sw.WriteLine("BtmRoiX=" + _measurement.BtmMeasureRoi.X); sw.WriteLine("BtmRoiY=" + _measurement.BtmMeasureRoi.Y); sw.WriteLine("BtmRoiW=" + _measurement.BtmMeasureRoi.Width); sw.WriteLine("BtmRoiH=" + _measurement.BtmMeasureRoi.Height);
                    sw.WriteLine("JigLX=" + _measurement.JigLeftRoi.X); sw.WriteLine("JigLY=" + _measurement.JigLeftRoi.Y); sw.WriteLine("JigLW=" + _measurement.JigLeftRoi.Width); sw.WriteLine("JigLH=" + _measurement.JigLeftRoi.Height);
                    sw.WriteLine("JigRX=" + _measurement.JigRightRoi.X); sw.WriteLine("JigRY=" + _measurement.JigRightRoi.Y); sw.WriteLine("JigRW=" + _measurement.JigRightRoi.Width); sw.WriteLine("JigRH=" + _measurement.JigRightRoi.Height);

                    sw.WriteLine("JigTarget=" + _measurement.TargetJigDistancePx); sw.WriteLine("JigTol=" + _measurement.JigTolerancePx);
                    sw.WriteLine("PixelToMmRatio=" + _measurement.PixelToMmRatio);
                    sw.WriteLine("TargetXOffsetMm=" + _measurement.TargetXOffsetMm); sw.WriteLine("OffsetToleranceMm=" + _measurement.OffsetToleranceMm);
                    sw.WriteLine("TargetAngleDeg=" + _measurement.TargetAngleDeg); sw.WriteLine("AngleToleranceDeg=" + _measurement.AngleToleranceDeg);
                }
            }
            catch { }
        }
    }
}