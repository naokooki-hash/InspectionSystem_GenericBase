using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CvRect = OpenCvSharp.Rect;

namespace InspectionSystem_GenericBase
{
    public partial class FormSettings : Form
    {
        private Form1 _mainForm;
        private AppSettings _appSettings;
        private MeasurementCore _measurement;

        private TabControl _tabSettings;

        // --- TAB 1: テストモード (Test Mode) ---
        private TabPage tabTestMode;
        private CheckBox chkTestModeEnable;
        private Button btnSelectTestFolder;
        private Label lblTestImageInfo;
        private Button btnPrevImage;
        private Button btnNextImage;
        private CheckBox chkAutoPlay;
        private System.Windows.Forms.Timer autoPlayTimer;
        private PictureBox pbDebugView;
        private TextBox txtTestImageSaveFolder;
        private Button btnBrowseSaveFolder;
        private TabPage tabImageCapture;
        private Button btnCaptureTestImage;
        private NumericUpDown nudAutoCaptureCount;
        private Button btnStartAutoCapture;
        private Label lblAutoCaptureStatus;
        private System.Windows.Forms.Timer autoCapturePollTimer;
        // --- TAB 2: 検査パラメータ (Inspection Parameters) ---
        private TabPage tabInspection;
        // Jig settings
        private CheckBox chkEnableJigCheck;
        private NumericUpDown nudJigTarget;
        private NumericUpDown nudJigTolerance;
        // Outer edge settings
        private CheckBox chkEnableOuterTiltCheck;
        private NumericUpDown nudOuterOffsetX;
        private NumericUpDown nudOuterOffsetA;
        // Hole settings
        private CheckBox chkEnableHoleCheck;
        private NumericUpDown nudOffsetTolerance;
        private NumericUpDown nudAngleTolerance;
        // Coordinates & Thresholds
        private NumericUpDown nudTiltLX, nudTiltLY, nudTiltLW, nudTiltLH;
        private NumericUpDown nudTiltRX, nudTiltRY, nudTiltRW, nudTiltRH;
        private NumericUpDown nudThreshOuterL, nudThreshOuterR;
        private NumericUpDown nudHolesX, nudHolesY, nudHolesW, nudHolesH;
        private NumericUpDown nudMinHoleArea, nudMaxHoleArea, nudMinCircularity;
        private NumericUpDown nudJigLX, nudJigLY, nudJigLW, nudJigLH;
        private NumericUpDown nudJigRX, nudJigRY, nudJigRW, nudJigRH;
        private NumericUpDown nudBtmRoiX, nudBtmRoiY, nudBtmRoiW, nudBtmRoiH;
        private NumericUpDown nudBtmInnerLX, nudBtmInnerLY, nudBtmInnerLW, nudBtmInnerLH;
        private NumericUpDown nudBtmInnerRX, nudBtmInnerRY, nudBtmInnerRW, nudBtmInnerRH;
        private NumericUpDown nudThreshBtmInnerL, nudThreshBtmInnerR;
        private NumericUpDown nudSplitX, nudSplitY;
        private NumericUpDown nudThreshTL, nudThreshTR, nudThreshBL, nudThreshBR;
        // Calibration
        private NumericUpDown nudPixelToMm, nudActualWidthMm;
        private Label lblCurrentHoleDistPx;
        private Button btnCalcRatio;

        // --- TAB 3: カメラ・システム (Camera & System) ---
        private TabPage tabCameraSystem;
        private NumericUpDown numExposureTime;
        private ComboBox cmbSaveMode;
        private TextBox txtAdminPassword;
        // Visual trigger parameters
        private ComboBox cmbTriggerMode;
        private NumericUpDown nudTriggerThreshold;
        private NumericUpDown nudStabilityDuration;
        private NumericUpDown nudResetThreshold;

        // --- TAB 4: PLC・ログ (PLC & Logs) ---
        private TabPage tabPlcLogs;
        private TextBox txtPlcIp;
        private NumericUpDown numPlcPort;
        private ComboBox cmbPlcVendor;
        private ComboBox cmbPlcDataType;
        private NumericUpDown numPlcHeartbeat;
        private NumericUpDown numPlcRead;
        private NumericUpDown numPlcOk;
        private NumericUpDown numPlcNg;
        private NumericUpDown nudPlcDelayMs;
        private NumericUpDown nudRetryCount;
        private NumericUpDown nudRetryDelayMs;
        private NumericUpDown nudAutoStartCount;
        private NumericUpDown nudLogKeepDays;
        private Button btnOpenDashboard;

        public FormSettings(Form1 mainForm)
        {
            _mainForm = mainForm;
            _appSettings = mainForm._appSettings;
            _measurement = mainForm._measurement;

            InitializeComponent();
            LoadCurrentSettings();

            // Link Form1's debug picturebox reference to settings tab debug picturebox
            _mainForm._pictureBoxDebug = pbDebugView;

            // Setup auto capture status polling timer
            autoCapturePollTimer = new System.Windows.Forms.Timer { Interval = 200 };
            autoCapturePollTimer.Tick += (s, e) => { UpdateAutoCaptureStatusLabel(); };
            autoCapturePollTimer.Start();

            // Form closing cleanup
            this.FormClosing += (s, e) => {
                autoPlayTimer.Stop();
                autoCapturePollTimer.Stop();
                _mainForm._pictureBoxDebug = null;
            };
        }

        private void InitializeComponent()
        {
            this.Width = 680;
            this.Height = 980;
            this.Text = "管理者用設定画面 (System Settings & Maintenance)";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;

            _tabSettings = new TabControl { Location = new Point(10, 10), Size = new Size(640, 860), Font = new Font(this.Font.FontFamily, 10) };
            this.Controls.Add(_tabSettings);

            // Tab Pages creation
            tabTestMode = new TabPage("テストモード (Test)") { AutoScroll = true };
            tabInspection = new TabPage("検査パラメータ (Inspection)") { AutoScroll = true };
            tabCameraSystem = new TabPage("カメラ・システム (System)") { AutoScroll = true };
            tabPlcLogs = new TabPage("PLC・ログ (PLC & Comms)") { AutoScroll = true };
            tabImageCapture = new TabPage("画像収集 (Capture)") { AutoScroll = true };

            _tabSettings.TabPages.Add(tabTestMode);
            _tabSettings.TabPages.Add(tabInspection);
            _tabSettings.TabPages.Add(tabCameraSystem);
            _tabSettings.TabPages.Add(tabPlcLogs);
            _tabSettings.TabPages.Add(tabImageCapture);

            BuildTestModeTab();
            BuildInspectionTab();
            BuildCameraSystemTab();
            BuildPlcLogsTab();
            BuildImageCaptureTab();

            // Bottom Dialog Buttons
            Button btnOk = new Button { Text = "OK (適用・保存)", Location = new Point(430, 885), Size = new Size(110, 35), BackColor = Color.LightGreen, DialogResult = DialogResult.OK };
            btnOk.Click += BtnOk_Click;
            this.Controls.Add(btnOk);

            Button btnCancel = new Button { Text = "キャンセル", Location = new Point(550, 885), Size = new Size(100, 35), DialogResult = DialogResult.Cancel };
            btnCancel.Click += (s, e) => { this.Close(); };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void BuildTestModeTab()
        {
            int y = 20;

            GroupBox gpCtrl = new GroupBox { Text = "オフライン検査テスト (Offline Simulation)", Location = new Point(15, y), Size = new Size(590, 120) };
            tabTestMode.Controls.Add(gpCtrl);

            chkTestModeEnable = new CheckBox { Text = "テストモード有効化", Location = new Point(15, 25), AutoSize = true };
            chkTestModeEnable.CheckedChanged += ChkTestModeEnable_CheckedChanged;
            gpCtrl.Controls.Add(chkTestModeEnable);

            btnSelectTestFolder = new Button { Text = "フォルダ選択", Location = new Point(180, 20), Size = new Size(100, 30) };
            btnSelectTestFolder.Click += BtnSelectTestFolder_Click;
            gpCtrl.Controls.Add(btnSelectTestFolder);

            lblTestImageInfo = new Label { Text = "画像: 0 / 0", Location = new Point(290, 25), AutoSize = true };
            gpCtrl.Controls.Add(lblTestImageInfo);

            btnPrevImage = new Button { Text = "◀ 前の画像", Location = new Point(15, 65), Size = new Size(110, 35) };
            btnPrevImage.Click += (s, e) => { _mainForm.LoadAndInspectTestImage(-1); UpdateTestImageInfoLabel(); };
            gpCtrl.Controls.Add(btnPrevImage);

            btnNextImage = new Button { Text = "次の画像 ▶", Location = new Point(140, 65), Size = new Size(110, 35) };
            btnNextImage.Click += (s, e) => { _mainForm.LoadAndInspectTestImage(1); UpdateTestImageInfoLabel(); };
            gpCtrl.Controls.Add(btnNextImage);

            chkAutoPlay = new CheckBox { Text = "自動送り (1.5秒間隔)", Location = new Point(270, 72), AutoSize = true };
            chkAutoPlay.CheckedChanged += ChkAutoPlay_CheckedChanged;
            gpCtrl.Controls.Add(chkAutoPlay);

            autoPlayTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            autoPlayTimer.Tick += (s, e) => { _mainForm.LoadAndInspectTestImage(1); UpdateTestImageInfoLabel(); };

            y += 140;

            GroupBox gpView = new GroupBox { Text = "二値化プレビュー (Debug Frame Preview)", Location = new Point(15, y), Size = new Size(590, 500) };
            tabTestMode.Controls.Add(gpView);

            pbDebugView = new PictureBox { Location = new Point(15, 25), Size = new Size(560, 455), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
            gpView.Controls.Add(pbDebugView);
        }

        private void BuildImageCaptureTab()
        {
            int y = 20;
            int lw = 150;

            GroupBox gpCapture = new GroupBox { Text = "テスト画像手動収集トリガー (Manual Image Collector)", Location = new Point(15, y), Size = new Size(590, 200) };
            tabImageCapture.Controls.Add(gpCapture);

            y = 30;
            btnCaptureTestImage = new Button
            {
                Text = "📷 テスト画像を撮影・保存 (生画像)",
                Location = new Point(15, y),
                Size = new Size(560, 50),
                BackColor = Color.DodgerBlue,
                ForeColor = Color.White,
                Font = new Font(this.Font.FontFamily, 12, FontStyle.Bold)
            };
            btnCaptureTestImage.Click += (s, e) => {
                _mainForm.CaptureTestImage();
            };
            gpCapture.Controls.Add(btnCaptureTestImage);
            y += 70;

            gpCapture.Controls.Add(new Label { Text = "撮像画像保存先フォルダ:", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            txtTestImageSaveFolder = new TextBox { Location = new Point(15 + lw, y), Size = new Size(290, 25) };
            gpCapture.Controls.Add(txtTestImageSaveFolder);

            btnBrowseSaveFolder = new Button { Text = "参照...", Location = new Point(15 + lw + 300, y - 2), Size = new Size(90, 28) };
            btnBrowseSaveFolder.Click += (s, e) => {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = txtTestImageSaveFolder.Text;
                    fbd.Description = "テスト画像の撮影・保存先フォルダを選択してください";
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        txtTestImageSaveFolder.Text = fbd.SelectedPath;
                    }
                }
            };
            gpCapture.Controls.Add(btnBrowseSaveFolder);

            // Triggered Auto-Capture GroupBox
            GroupBox gpAuto = new GroupBox { Text = "検査トリガー連動・連続自動収集 (Trigger Sync Auto Capture)", Location = new Point(15, 235), Size = new Size(590, 150) };
            tabImageCapture.Controls.Add(gpAuto);

            gpAuto.Controls.Add(new Label { Text = "自動収集枚数:", Location = new Point(15, 32), Size = new Size(100, 20) });
            nudAutoCaptureCount = new NumericUpDown { Location = new Point(120, 30), Size = new Size(90, 25), Minimum = 1, Maximum = 9999, Value = 100 };
            gpAuto.Controls.Add(nudAutoCaptureCount);

            lblAutoCaptureStatus = new Label { Text = "自動収集: 停止中 (待機)", Location = new Point(230, 32), Size = new Size(330, 20), ForeColor = Color.DarkGray, Font = new Font(this.Font, FontStyle.Bold) };
            gpAuto.Controls.Add(lblAutoCaptureStatus);

            btnStartAutoCapture = new Button
            {
                Text = "▶ 自動収集を開始 (START)",
                Location = new Point(15, 75),
                Size = new Size(560, 50),
                BackColor = Color.LightSkyBlue,
                Font = new Font(this.Font.FontFamily, 12, FontStyle.Bold)
            };
            btnStartAutoCapture.Click += BtnStartAutoCapture_Click;
            gpAuto.Controls.Add(btnStartAutoCapture);
        }

        private void BtnStartAutoCapture_Click(object? sender, EventArgs e)
        {
            if (_mainForm._autoCaptureRemainingCount > 0)
            {
                _mainForm._autoCaptureRemainingCount = 0;
                _mainForm.AppendLog("[自動収集] 画像収集シーケンスを停止しました。");
            }
            else
            {
                int count = (int)nudAutoCaptureCount.Value;
                _mainForm._autoCaptureRemainingCount = count;
                _mainForm.AppendLog($"[自動収集] 画像収集シーケンスを開始します。目標枚数: {count} 枚");
            }
            UpdateAutoCaptureStatusLabel();
        }

        private void UpdateAutoCaptureStatusLabel()
        {
            int remaining = _mainForm._autoCaptureRemainingCount;
            if (remaining > 0)
            {
                lblAutoCaptureStatus.Text = $"自動収集実行中: 残り {remaining} 枚";
                lblAutoCaptureStatus.ForeColor = Color.Green;
                btnStartAutoCapture.Text = "■ 自動収集を停止 (STOP)";
                btnStartAutoCapture.BackColor = Color.Salmon;
                nudAutoCaptureCount.Enabled = false;
            }
            else
            {
                lblAutoCaptureStatus.Text = "自動収集: 停止中 (待機)";
                lblAutoCaptureStatus.ForeColor = Color.DarkGray;
                btnStartAutoCapture.Text = "▶ 自動収集を開始 (START)";
                btnStartAutoCapture.BackColor = Color.LightSkyBlue;
                nudAutoCaptureCount.Enabled = true;
            }
        }

        private void BuildInspectionTab()
        {
            int y = 15;
            int lw = 170, cw = 80, rw = 320, lh = 30;

            void AddN(string txt, ref NumericUpDown n, decimal min, decimal max, decimal val, int dp = 0, decimal step = 1M, Control? parent = null)
            {
                if (parent == null) parent = tabInspection;
                parent.Controls.Add(new Label { Text = txt, Location = new Point(15, y + 2), Size = new Size(lw, 20) });
                n = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = min, Maximum = max, Value = val, DecimalPlaces = dp, Increment = step };
                parent.Controls.Add(n); y += lh;
            }

            void AddRect(string txt, ref NumericUpDown nx, ref NumericUpDown ny, ref NumericUpDown nw, ref NumericUpDown nh, CvRect r, Control? parent = null)
            {
                if (parent == null) parent = tabInspection;
                parent.Controls.Add(new Label { Text = txt, Location = new Point(15, y + 2), Size = new Size(110, 20) });
                int sx = 130, step = 55, boxW = 50;
                nx = new NumericUpDown { Location = new Point(sx, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.X };
                ny = new NumericUpDown { Location = new Point(sx + step, y), Size = new Size(boxW, 20), Minimum = 0, Maximum = 3000, Value = r.Y };
                nw = new NumericUpDown { Location = new Point(sx + step * 2, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Width };
                nh = new NumericUpDown { Location = new Point(sx + step * 3, y), Size = new Size(boxW, 20), Minimum = 1, Maximum = 3000, Value = r.Height };
                parent.Controls.Add(nx); parent.Controls.Add(ny); parent.Controls.Add(nw); parent.Controls.Add(nh); y += lh;
            }

            // 1. Jig Check Settings
            GroupBox gpJig = new GroupBox { Text = "エッジ間距離測定設定 (Jig Check)", Location = new Point(15, y), Size = new Size(590, 190) };
            tabInspection.Controls.Add(gpJig);
            int origY = y;
            y = 20;

            chkEnableJigCheck = new CheckBox { Text = "エッジ間距離測定を有効にする", Location = new Point(15, y), AutoSize = true };
            gpJig.Controls.Add(chkEnableJigCheck);
            y += 25;

            AddRect("左エッジ ROI:", ref nudJigLX, ref nudJigLY, ref nudJigLW, ref nudJigLH, _measurement.JigLeftRoi, gpJig);
            AddRect("右エッジ ROI:", ref nudJigRX, ref nudJigRY, ref nudJigRW, ref nudJigRH, _measurement.JigRightRoi, gpJig);
            AddN("エッジ目標距離(mm):", ref nudJigTarget, 0, 500, (decimal)_measurement.TargetJigDistanceMm, 2, 0.1M, gpJig);
            AddN("エッジ許容誤差(mm):", ref nudJigTolerance, 0, 50, (decimal)_measurement.JigToleranceMm, 2, 0.1M, gpJig);

            y = origY + 205;

            // 2. Alignment Settings
            GroupBox gpAlign = new GroupBox { Text = "製品アライメント・傾き検査設定 (Alignment)", Location = new Point(15, y), Size = new Size(590, 480) };
            tabInspection.Controls.Add(gpAlign);
            origY = y;
            y = 20;

            chkEnableOuterTiltCheck = new CheckBox { Text = "【モードA】 外形エッジで製品の傾き・ズレを検査する", Location = new Point(15, y), AutoSize = true, ForeColor = Color.Teal };
            gpAlign.Controls.Add(chkEnableOuterTiltCheck);
            y += 25;

            AddRect("左外形エッジ ROI:", ref nudTiltLX, ref nudTiltLY, ref nudTiltLW, ref nudTiltLH, _measurement.TiltLeftRoi, gpAlign);
            AddRect("右外形エッジ ROI:", ref nudTiltRX, ref nudTiltRY, ref nudTiltRW, ref nudTiltRH, _measurement.TiltRightRoi, gpAlign);
            AddN("外形目標Xずれ(mm):", ref nudOuterOffsetX, 0, 50, (decimal)_measurement.OuterOffsetToleranceMm, 2, 0.1M, gpAlign);
            AddN("外形目標傾き(deg):", ref nudOuterOffsetA, 0, 90, (decimal)_measurement.OuterAngleToleranceDeg, 2, 0.1M, gpAlign);
            AddN("左エッジ(青枠) 閾値:", ref nudThreshOuterL, 0, 255, _measurement.ThreshOuterL, 0, 1M, gpAlign);
            AddN("右エッジ(青枠) 閾値:", ref nudThreshOuterR, 0, 255, _measurement.ThreshOuterR, 0, 1M, gpAlign);
            y += 10;

            chkEnableHoleCheck = new CheckBox { Text = "【モードB】 穴で製品の傾き・ズレを検査する", Location = new Point(15, y), AutoSize = true, ForeColor = Color.Blue };
            gpAlign.Controls.Add(chkEnableHoleCheck);
            y += 25;

            AddRect("基準穴 ROI:", ref nudHolesX, ref nudHolesY, ref nudHolesW, ref nudHolesH, _measurement.HolesRoi, gpAlign);
            AddN("穴 最小面積:", ref nudMinHoleArea, 0, 100000, _measurement.MinHoleArea, 0, 1M, gpAlign);
            AddN("穴 最大面積:", ref nudMaxHoleArea, 0, 1000000, _measurement.MaxHoleArea, 0, 1M, gpAlign);
            AddN("真円度しきい値:", ref nudMinCircularity, 0, 1, (decimal)_measurement.MinCircularity, 2, 0.05M, gpAlign);
            AddN("穴目標Xずれ(mm):", ref nudOffsetTolerance, 0, 50, (decimal)_measurement.OffsetToleranceMm, 2, 0.1M, gpAlign);
            AddN("穴目標傾き(deg):", ref nudAngleTolerance, 0, 90, (decimal)_measurement.AngleToleranceDeg, 2, 0.1M, gpAlign);

            y = origY + 495;

            // 3. Calibration Group
            GroupBox gpCalib = new GroupBox { Text = "キャリブレーション (Pixel/mm比率計算)", Location = new Point(15, y), Size = new Size(590, 150) };
            tabInspection.Controls.Add(gpCalib);
            origY = y;
            y = 20;

            lblCurrentHoleDistPx = new Label { Text = "現在の穴/エッジ間距離: 0.0 px", Location = new Point(15, y), Size = new Size(300, 20), ForeColor = Color.DarkOrange, Font = new Font(this.Font, FontStyle.Bold) };
            gpCalib.Controls.Add(lblCurrentHoleDistPx);
            y += 25;

            gpCalib.Controls.Add(new Label { Text = "実測の距離(mm):", Location = new Point(15, y + 2), Size = new Size(120, 20) });
            nudActualWidthMm = new NumericUpDown { Location = new Point(150, y), Size = new Size(80, 20), Minimum = 0.1M, Maximum = 500, Value = 50, DecimalPlaces = 2, Increment = 0.1M };
            gpCalib.Controls.Add(nudActualWidthMm);

            btnCalcRatio = new Button { Text = "比率を自動計算", Location = new Point(250, y - 2), Size = new Size(130, 26), BackColor = Color.LightYellow };
            btnCalcRatio.Click += BtnCalcRatio_Click;
            gpCalib.Controls.Add(btnCalcRatio);
            y += 30;

            AddN("Pixel->mm比率:", ref nudPixelToMm, 0.0001M, 1, (decimal)_measurement.PixelToMmRatio, 5, 0.001M, gpCalib);

            y = origY + 165;

            // 4. Detailed ROIs and Binary parameters
            GroupBox gpMore = new GroupBox { Text = "詳細測定領域設定 (Detailed ROIs & Binary Parameters)", Location = new Point(15, y), Size = new Size(590, 520) };
            tabInspection.Controls.Add(gpMore);
            origY = y;
            y = 20;

            AddRect("Btm線 ROI(黄):", ref nudBtmRoiX, ref nudBtmRoiY, ref nudBtmRoiW, ref nudBtmRoiH, _measurement.BtmMeasureRoi, gpMore);
            AddRect("Btm内側左 ROI(赤):", ref nudBtmInnerLX, ref nudBtmInnerLY, ref nudBtmInnerLW, ref nudBtmInnerLH, _measurement.BtmInnerLeftRoi, gpMore);
            AddRect("Btm内側右 ROI(赤):", ref nudBtmInnerRX, ref nudBtmInnerRY, ref nudBtmInnerRW, ref nudBtmInnerRH, _measurement.BtmInnerRightRoi, gpMore);
            AddN("左内側(赤枠) 閾値:", ref nudThreshBtmInnerL, 0, 255, _measurement.ThreshBtmInnerL, 0, 1M, gpMore);
            AddN("右内側(赤枠) 閾値:", ref nudThreshBtmInnerR, 0, 255, _measurement.ThreshBtmInnerR, 0, 1M, gpMore);
            y += 10;

            gpMore.Controls.Add(new Label { Text = "--- 4分割二値化 境界線設定 ---", Location = new Point(15, y), AutoSize = true, ForeColor = Color.Blue });
            y += 25;
            AddN("左右分割 X境界線:", ref nudSplitX, 0, 3000, _measurement.SplitBoundaryX, 0, 1M, gpMore);
            AddN("上下分割 Y境界線:", ref nudSplitY, 0, 3000, _measurement.SplitBoundaryY, 0, 1M, gpMore);
            AddN("左上 (TL) 二値閾値:", ref nudThreshTL, 0, 255, _measurement.ThreshTopLeft, 0, 1M, gpMore);
            AddN("右上 (TR) 二値閾値:", ref nudThreshTR, 0, 255, _measurement.ThreshTopRight, 0, 1M, gpMore);
            AddN("左下 (BL) 二値閾値:", ref nudThreshBL, 0, 255, _measurement.ThreshBtmLeft, 0, 1M, gpMore);
            AddN("右下 (BR) 二値閾値:", ref nudThreshBR, 0, 255, _measurement.ThreshBtmRight, 0, 1M, gpMore);

            y = origY + 540;
        }

        private void BuildCameraSystemTab()
        {
            int y = 15;
            int lw = 180, cw = 260, lh = 35;

            GroupBox gpCam = new GroupBox { Text = "カメラ露光・画像保存設定 (Camera Config)", Location = new Point(15, y), Size = new Size(590, 160) };
            tabCameraSystem.Controls.Add(gpCam);
            int origY = y;
            y = 25;

            gpCam.Controls.Add(new Label { Text = "カメラ露光時間 (us):", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            numExposureTime = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 100, Maximum = 100000, Increment = 100M };
            gpCam.Controls.Add(numExposureTime);
            y += lh;

            gpCam.Controls.Add(new Label { Text = "画像保存モード:", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            cmbSaveMode = new ComboBox { Location = new Point(200, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSaveMode.Items.AddRange(new object[] { "0: 保存しない", "1: NGのみ保存", "2: 全て保存" });
            gpCam.Controls.Add(cmbSaveMode);
            y += lh;

            gpCam.Controls.Add(new Label { Text = "管理者パスワード:", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            txtAdminPassword = new TextBox { Location = new Point(200, y), Size = new Size(160, 25), Text = "********", PasswordChar = '*', ReadOnly = true };
            gpCam.Controls.Add(txtAdminPassword);

            Button btnChangePassword = new Button { Text = "変更...", Location = new Point(370, y), Size = new Size(90, 25), BackColor = Color.LightGray };
            btnChangePassword.Click += BtnChangePassword_Click;
            gpCam.Controls.Add(btnChangePassword);

            y = origY + 180;

            GroupBox gpTrigger = new GroupBox { Text = "画像処理トリガー設定 (Visual Trigger)", Location = new Point(15, y), Size = new Size(590, 190) };
            tabCameraSystem.Controls.Add(gpTrigger);
            y = 25;

            gpTrigger.Controls.Add(new Label { Text = "画像トリガーモード:", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            cmbTriggerMode = new ComboBox { Location = new Point(200, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTriggerMode.Items.AddRange(new object[] { "明るさ変化 (Bright)", "暗さ変化 (Dark)" });
            gpTrigger.Controls.Add(cmbTriggerMode);
            y += lh;

            gpTrigger.Controls.Add(new Label { Text = "トリガー判定閾値:", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            nudTriggerThreshold = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 1, Maximum = 255 };
            gpTrigger.Controls.Add(nudTriggerThreshold);
            y += lh;

            gpTrigger.Controls.Add(new Label { Text = "トリガー安定判定時間 (ms):", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            nudStabilityDuration = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 50, Maximum = 5000, Increment = 50M };
            gpTrigger.Controls.Add(nudStabilityDuration);
            y += lh;

            gpTrigger.Controls.Add(new Label { Text = "復帰待ち輝度差閾値:", Location = new Point(15, y + 2), Size = new Size(lw, 20) });
            nudResetThreshold = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 1, Maximum = 255 };
            gpTrigger.Controls.Add(nudResetThreshold);
        }

        private void BuildPlcLogsTab()
        {
            int y = 15;
            int lw = 180, cw = 160, lh = 32;

            void AddL(string txt, Control parent) { parent.Controls.Add(new Label { Text = txt, Location = new Point(15, y + 2), Size = new Size(lw, 20) }); }

            // 1. PLC Connection Settings
            GroupBox gpPlc = new GroupBox { Text = "PLC 通信接続設定 (PLC Comms Settings)", Location = new Point(15, y), Size = new Size(590, 480) };
            tabPlcLogs.Controls.Add(gpPlc);
            int origY = y;
            y = 20;

            AddL("PLC IPアドレス:", gpPlc);
            txtPlcIp = new TextBox { Location = new Point(200, y), Size = new Size(cw, 25) };
            gpPlc.Controls.Add(txtPlcIp);
            y += lh;

            AddL("ポート番号:", gpPlc);
            numPlcPort = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 1, Maximum = 65535 };
            gpPlc.Controls.Add(numPlcPort);
            y += lh;

            AddL("ベンダー:", gpPlc);
            cmbPlcVendor = new ComboBox { Location = new Point(200, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPlcVendor.Items.AddRange(new object[] { "Mitsubishi", "Keyence" });
            gpPlc.Controls.Add(cmbPlcVendor);
            y += lh;

            AddL("データタイプ:", gpPlc);
            cmbPlcDataType = new ComboBox { Location = new Point(200, y), Size = new Size(cw, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPlcDataType.Items.AddRange(new object[] { "Bit", "Word" });
            gpPlc.Controls.Add(cmbPlcDataType);
            y += lh;

            y += 10;
            gpPlc.Controls.Add(new Label { Text = "--- PLC デバイスアドレス設定 ---", Location = new Point(15, y), AutoSize = true, ForeColor = Color.Blue });
            y += 22;

            AddL("Heartbeat アドレス:", gpPlc);
            numPlcHeartbeat = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999 };
            gpPlc.Controls.Add(numPlcHeartbeat);
            y += lh;

            AddL("読み取り (Trigger):", gpPlc);
            numPlcRead = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999 };
            gpPlc.Controls.Add(numPlcRead);
            y += lh;

            AddL("書き込み (OK):", gpPlc);
            numPlcOk = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999 };
            gpPlc.Controls.Add(numPlcOk);
            y += lh;

            AddL("書き込み (NG):", gpPlc);
            numPlcNg = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 99999 };
            gpPlc.Controls.Add(numPlcNg);
            y += lh;

            Button btnPlcTest = new Button { Text = "保存して再接続テスト (Save & Reconnect)", Location = new Point(200, y), Size = new Size(260, 28), BackColor = Color.LightGreen };
            btnPlcTest.Click += BtnPlcTest_Click;
            gpPlc.Controls.Add(btnPlcTest);

            y = origY + 495;

            // 2. Log & Dashboard Group
            GroupBox gpLogs = new GroupBox { Text = "ログ管理・稼働品質分析 (Logs & Dashboard)", Location = new Point(15, y), Size = new Size(590, 240) };
            tabPlcLogs.Controls.Add(gpLogs);
            y = 20;

            AddL("PLCトリガー遅延 (ms):", gpLogs);
            nudPlcDelayMs = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 10000 };
            gpLogs.Controls.Add(nudPlcDelayMs);
            y += lh;

            AddL("トリガー再試行回数:", gpLogs);
            nudRetryCount = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 10 };
            gpLogs.Controls.Add(nudRetryCount);
            y += lh;

            AddL("再試行遅延 (ms):", gpLogs);
            nudRetryDelayMs = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 10, Maximum = 5000 };
            gpLogs.Controls.Add(nudRetryDelayMs);
            y += lh;

            AddL("自動起動時カウント待機:", gpLogs);
            nudAutoStartCount = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 100 };
            gpLogs.Controls.Add(nudAutoStartCount);
            y += lh;

            AddL("ログ保存期間 (日):", gpLogs);
            nudLogKeepDays = new NumericUpDown { Location = new Point(200, y), Size = new Size(cw, 20), Minimum = 0, Maximum = 365 };
            gpLogs.Controls.Add(nudLogKeepDays);
            y += lh;

            btnOpenDashboard = new Button
            {
                Text = "📊 稼働・品質分析ダッシュボードを開く",
                Location = new Point(200, y - 5),
                Size = new Size(260, 32),
                BackColor = Color.LightSlateGray,
                ForeColor = Color.White,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold)
            };
            btnOpenDashboard.Click += BtnOpenDashboard_Click;
            gpLogs.Controls.Add(btnOpenDashboard);
        }

        private void LoadCurrentSettings()
        {
            txtTestImageSaveFolder.Text = _appSettings.TestImageSaveFolder;
            // Camera configs
            numExposureTime.Value = (decimal)_appSettings.Cam.ExposureTime;
            cmbSaveMode.SelectedIndex = _mainForm._saveMode;

            // Visual triggers
            cmbTriggerMode.SelectedIndex = _mainForm._triggerOnBright ? 0 : 1;
            nudTriggerThreshold.Value = (decimal)_mainForm._triggerThreshold;
            nudStabilityDuration.Value = _mainForm._stabilityDurationMs;
            nudResetThreshold.Value = (decimal)_mainForm._resetThreshold;

            // Calibration values
            nudPixelToMm.Value = (decimal)_measurement.PixelToMmRatio;
            nudActualWidthMm.Value = 50M; // default
            if (_measurement.LastHoleDistancePx > 0)
            {
                lblCurrentHoleDistPx.Text = $"現在の穴/エッジ間距離: {_measurement.LastHoleDistancePx:F1} px";
            }

            // Toggles
            chkEnableJigCheck.Checked = _measurement.EnableJigCheck;
            nudJigTarget.Value = (decimal)_measurement.TargetJigDistanceMm;
            nudJigTolerance.Value = (decimal)_measurement.JigToleranceMm;

            chkEnableOuterTiltCheck.Checked = _measurement.EnableOuterTiltCheck;
            nudOuterOffsetX.Value = (decimal)_measurement.OuterOffsetToleranceMm;
            nudOuterOffsetA.Value = (decimal)_measurement.OuterAngleToleranceDeg;

            chkEnableHoleCheck.Checked = _measurement.EnableHoleCheck;
            nudOffsetTolerance.Value = (decimal)_measurement.OffsetToleranceMm;
            nudAngleTolerance.Value = (decimal)_measurement.AngleToleranceDeg;

            // Coords parameters
            nudTiltLX.Value = _measurement.TiltLeftRoi.X; nudTiltLY.Value = _measurement.TiltLeftRoi.Y;
            nudTiltLW.Value = _measurement.TiltLeftRoi.Width; nudTiltLH.Value = _measurement.TiltLeftRoi.Height;
            nudTiltRX.Value = _measurement.TiltRightRoi.X; nudTiltRY.Value = _measurement.TiltRightRoi.Y;
            nudTiltRW.Value = _measurement.TiltRightRoi.Width; nudTiltRH.Value = _measurement.TiltRightRoi.Height;
            nudThreshOuterL.Value = _measurement.ThreshOuterL; nudThreshOuterR.Value = _measurement.ThreshOuterR;

            nudHolesX.Value = _measurement.HolesRoi.X; nudHolesY.Value = _measurement.HolesRoi.Y;
            nudHolesW.Value = _measurement.HolesRoi.Width; nudHolesH.Value = _measurement.HolesRoi.Height;
            nudMinHoleArea.Value = _measurement.MinHoleArea; nudMaxHoleArea.Value = _measurement.MaxHoleArea;
            nudMinCircularity.Value = (decimal)_measurement.MinCircularity;

            nudJigLX.Value = _measurement.JigLeftRoi.X; nudJigLY.Value = _measurement.JigLeftRoi.Y;
            nudJigLW.Value = _measurement.JigLeftRoi.Width; nudJigLH.Value = _measurement.JigLeftRoi.Height;
            nudJigRX.Value = _measurement.JigRightRoi.X; nudJigRY.Value = _measurement.JigRightRoi.Y;
            nudJigRW.Value = _measurement.JigRightRoi.Width; nudJigRH.Value = _measurement.JigRightRoi.Height;

            nudBtmRoiX.Value = _measurement.BtmMeasureRoi.X; nudBtmRoiY.Value = _measurement.BtmMeasureRoi.Y;
            nudBtmRoiW.Value = _measurement.BtmMeasureRoi.Width; nudBtmRoiH.Value = _measurement.BtmMeasureRoi.Height;

            nudBtmInnerLX.Value = _measurement.BtmInnerLeftRoi.X; nudBtmInnerLY.Value = _measurement.BtmInnerLeftRoi.Y;
            nudBtmInnerLW.Value = _measurement.BtmInnerLeftRoi.Width; nudBtmInnerLH.Value = _measurement.BtmInnerLeftRoi.Height;
            nudBtmInnerRX.Value = _measurement.BtmInnerRightRoi.X; nudBtmInnerRY.Value = _measurement.BtmInnerRightRoi.Y;
            nudBtmInnerRW.Value = _measurement.BtmInnerRightRoi.Width; nudBtmInnerRH.Value = _measurement.BtmInnerRightRoi.Height;
            nudThreshBtmInnerL.Value = _measurement.ThreshBtmInnerL; nudThreshBtmInnerR.Value = _measurement.ThreshBtmInnerR;

            nudSplitX.Value = _measurement.SplitBoundaryX; nudSplitY.Value = _measurement.SplitBoundaryY;
            nudThreshTL.Value = _measurement.ThreshTopLeft; nudThreshTR.Value = _measurement.ThreshTopRight;
            nudThreshBL.Value = _measurement.ThreshBtmLeft; nudThreshBR.Value = _measurement.ThreshBtmRight;

            // PLC connections
            txtPlcIp.Text = _appSettings.PlcIpAddress;
            numPlcPort.Value = _appSettings.PlcPort;
            cmbPlcVendor.SelectedItem = _appSettings.PlcVendor;
            cmbPlcDataType.SelectedItem = _appSettings.PlcDataType;

            numPlcHeartbeat.Value = _appSettings.HeartbeatAddress;
            numPlcRead.Value = _appSettings.Cam.ReadDeviceAddress;
            numPlcOk.Value = _appSettings.Cam.OkDeviceAddress;
            numPlcNg.Value = _appSettings.Cam.NgDeviceAddress;

            // Log keeping configs
            nudPlcDelayMs.Value = _mainForm._plcDelayMs;
            nudRetryCount.Value = _mainForm._maxRetryCount;
            nudRetryDelayMs.Value = _mainForm._retryDelayMs;
            nudAutoStartCount.Value = _mainForm._autoStartCount;
            nudLogKeepDays.Value = _mainForm._logKeepDays;

            chkTestModeEnable.Checked = _mainForm._isTestModeEnabled;
            UpdateTestImageInfoLabel();
        }

        private void BtnChangePassword_Click(object? sender, EventArgs e)
        {
            using (var prompt = new Form())
            {
                prompt.Width = 350;
                prompt.Height = 220;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = "管理者パスワードの変更";
                prompt.StartPosition = FormStartPosition.CenterParent;

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

        private void BtnPlcTest_Click(object? sender, EventArgs e)
        {
            _appSettings.PlcIpAddress = txtPlcIp.Text;
            _appSettings.PlcPort = (int)numPlcPort.Value;
            _appSettings.PlcVendor = cmbPlcVendor.SelectedItem?.ToString() ?? "Mitsubishi";
            _appSettings.PlcDataType = cmbPlcDataType.SelectedItem?.ToString() ?? "Bit";
            _appSettings.Save();

            _mainForm._plc.Disconnect();
            _mainForm._plc.Connect();
            MessageBox.Show("PLC接続情報を保存し、再接続を試みました。");
        }

        private void BtnCalcRatio_Click(object? sender, EventArgs e)
        {
            if (_measurement.LastHoleDistancePx <= 0)
            {
                MessageBox.Show("先にテスト実行して検出させてください。");
                return;
            }
            nudPixelToMm.Value = nudActualWidthMm.Value / (decimal)_measurement.LastHoleDistancePx;
            MessageBox.Show("比率を更新しました。各種目標(mm)を再設定してください。");
        }

        private void BtnOpenDashboard_Click(object? sender, EventArgs e)
        {
            FormDashboard dash = new FormDashboard(_mainForm._analyzer);
            dash.ShowDialog(this);
        }

        private void ChkTestModeEnable_CheckedChanged(object? sender, EventArgs e)
        {
            _mainForm._isTestModeEnabled = chkTestModeEnable.Checked;
            if (_mainForm._isTestModeEnabled)
            {
                if (!Directory.Exists(_mainForm._testImagesPath))
                {
                    Directory.CreateDirectory(_mainForm._testImagesPath);
                }
                _mainForm.LoadTestImageFiles();
            }
            else
            {
                chkAutoPlay.Checked = false;
            }
            UpdateTestImageInfoLabel();
        }

        private void BtnSelectTestFolder_Click(object? sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = _mainForm._testImagesPath;
                fbd.Description = "テスト用画像の入ったフォルダを選択してください";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _mainForm._testImagesPath = fbd.SelectedPath;
                    _mainForm.LoadTestImageFiles();
                    UpdateTestImageInfoLabel();
                }
            }
        }

        private void UpdateTestImageInfoLabel()
        {
            if (_mainForm._testImageFiles.Count == 0)
            {
                lblTestImageInfo.Text = "画像: 0 / 0";
            }
            else
            {
                string filename = Path.GetFileName(_mainForm._testImageFiles[_mainForm._currentTestImageIndex]);
                lblTestImageInfo.Text = $"画像: {_mainForm._currentTestImageIndex + 1} / {_mainForm._testImageFiles.Count}\n({filename})";
            }
        }

        private void ChkAutoPlay_CheckedChanged(object? sender, EventArgs e)
        {
            if (chkAutoPlay.Checked && _mainForm._isTestModeEnabled && _mainForm._testImageFiles.Count > 0)
            {
                autoPlayTimer.Start();
            }
            else
            {
                autoPlayTimer.Stop();
            }
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            // 1. AppSettings update & save
            _appSettings.Cam.ExposureTime = (double)numExposureTime.Value;
            _appSettings.PlcIpAddress = txtPlcIp.Text;
            _appSettings.PlcPort = (int)numPlcPort.Value;
            _appSettings.PlcVendor = cmbPlcVendor.SelectedItem?.ToString() ?? "Mitsubishi";
            _appSettings.PlcDataType = cmbPlcDataType.SelectedItem?.ToString() ?? "Bit";
            _appSettings.HeartbeatAddress = (int)numPlcHeartbeat.Value;
            _appSettings.Cam.ReadDeviceAddress = (int)numPlcRead.Value;
            _appSettings.Cam.OkDeviceAddress = (int)numPlcOk.Value;
            _appSettings.Cam.NgDeviceAddress = (int)numPlcNg.Value;
            _appSettings.Cam.WriteDeviceAddress = (int)numPlcOk.Value;
            _appSettings.TestImageSaveFolder = txtTestImageSaveFolder.Text;
            _appSettings.Save();

            // 2. MainForm parameters update
            _mainForm._saveMode = cmbSaveMode.SelectedIndex;
            _mainForm._triggerOnBright = cmbTriggerMode.SelectedIndex == 0;
            _mainForm._triggerThreshold = (double)nudTriggerThreshold.Value;
            _mainForm._stabilityDurationMs = (int)nudStabilityDuration.Value;
            _mainForm._resetThreshold = (double)nudResetThreshold.Value;
            _mainForm._plcDelayMs = (int)nudPlcDelayMs.Value;
            _mainForm._maxRetryCount = (int)nudRetryCount.Value;
            _mainForm._retryDelayMs = (int)nudRetryDelayMs.Value;
            _mainForm._autoStartCount = (int)nudAutoStartCount.Value;
            _mainForm._logKeepDays = (int)nudLogKeepDays.Value;

            // Apply camera exposure immediately
            _mainForm._camera.SetExposure(_appSettings.Cam.ExposureTime);

            // 3. MeasurementCore params update
            _measurement.EnableJigCheck = chkEnableJigCheck.Checked;
            _measurement.TargetJigDistanceMm = (double)nudJigTarget.Value;
            _measurement.JigToleranceMm = (double)nudJigTolerance.Value;

            _measurement.EnableOuterTiltCheck = chkEnableOuterTiltCheck.Checked;
            _measurement.OuterOffsetToleranceMm = (double)nudOuterOffsetX.Value;
            _measurement.OuterAngleToleranceDeg = (double)nudOuterOffsetA.Value;

            _measurement.EnableHoleCheck = chkEnableHoleCheck.Checked;
            _measurement.OffsetToleranceMm = (double)nudOffsetTolerance.Value;
            _measurement.AngleToleranceDeg = (double)nudAngleTolerance.Value;

            // Update main form local config properties
            _mainForm._configEnableOuterTilt = _measurement.EnableOuterTiltCheck;
            _mainForm._configEnableHole = _measurement.EnableHoleCheck;

            // Coords & thresholding params write back
            _measurement.TiltLeftRoi = new CvRect((int)nudTiltLX.Value, (int)nudTiltLY.Value, (int)nudTiltLW.Value, (int)nudTiltLH.Value);
            _measurement.TiltRightRoi = new CvRect((int)nudTiltRX.Value, (int)nudTiltRY.Value, (int)nudTiltRW.Value, (int)nudTiltRH.Value);
            _measurement.ThreshOuterL = (int)nudThreshOuterL.Value;
            _measurement.ThreshOuterR = (int)nudThreshOuterR.Value;

            _measurement.HolesRoi = new CvRect((int)nudHolesX.Value, (int)nudHolesY.Value, (int)nudHolesW.Value, (int)nudHolesH.Value);
            _measurement.MinHoleArea = (int)nudMinHoleArea.Value;
            _measurement.MaxHoleArea = (int)nudMaxHoleArea.Value;
            _measurement.MinCircularity = (double)nudMinCircularity.Value;

            _measurement.JigLeftRoi = new CvRect((int)nudJigLX.Value, (int)nudJigLY.Value, (int)nudJigLW.Value, (int)nudJigLH.Value);
            _measurement.JigRightRoi = new CvRect((int)nudJigRX.Value, (int)nudJigRY.Value, (int)nudJigRW.Value, (int)nudJigRH.Value);

            _measurement.BtmMeasureRoi = new CvRect((int)nudBtmRoiX.Value, (int)nudBtmRoiY.Value, (int)nudBtmRoiW.Value, (int)nudBtmRoiH.Value);
            _measurement.BtmInnerLeftRoi = new CvRect((int)nudBtmInnerLX.Value, (int)nudBtmInnerLY.Value, (int)nudBtmInnerLW.Value, (int)nudBtmInnerLH.Value);
            _measurement.BtmInnerRightRoi = new CvRect((int)nudBtmInnerRX.Value, (int)nudBtmInnerRY.Value, (int)nudBtmInnerRW.Value, (int)nudBtmInnerRH.Value);
            _measurement.ThreshBtmInnerL = (int)nudThreshBtmInnerL.Value;
            _measurement.ThreshBtmInnerR = (int)nudThreshBtmInnerR.Value;

            _measurement.SplitBoundaryX = (int)nudSplitX.Value;
            _measurement.SplitBoundaryY = (int)nudSplitY.Value;
            _measurement.ThreshTopLeft = (int)nudThreshTL.Value;
            _measurement.ThreshTopRight = (int)nudThreshTR.Value;
            _measurement.ThreshBtmLeft = (int)nudThreshBL.Value;
            _measurement.ThreshBtmRight = (int)nudThreshBR.Value;

            _measurement.PixelToMmRatio = (double)nudPixelToMm.Value;

            // 4. Save to config.txt
            _mainForm.SaveConfig();

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
