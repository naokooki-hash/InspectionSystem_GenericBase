using System;
using System.Diagnostics;
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
using CvSize = OpenCvSharp.Size;
using CvRect = OpenCvSharp.Rect;

namespace _20260224SolderInspec
{
    public class Form1 : Form
    {
        // 状態定数
        private const int STATE_WAITING = 0;
        private const int STATE_STABILIZING = 1;
        private const int STATE_INSPECTED = 2;

        // コンポーネント
        private TeliCamera _camera;
        private MeasurementCore _measurement;
        private PlcCommunicator _plc;
        private PictureBox _pictureBox;
        private Label _lblStatus;
        private Label _lblBrightness;
        private NumericUpDown _nudTriggerThreshold;
        private NumericUpDown _nudStabilityDuration; // ms単位
        private NumericUpDown _nudResetThreshold;
        private NumericUpDown _nudRoiX, _nudRoiY, _nudRoiW, _nudRoiH;
        private Button _btnSaveConfig;

        // 状態変数
        private int _currentState = STATE_WAITING;
        private DateTime _stabilityStartTime;
        private CvRect _roi = new CvRect(300, 200, 100, 100);

        // 設定値
        private double _triggerThreshold = 100.0;
        private int _stabilityDurationMs = 300; // 0.3秒 = 300ms
        private double _resetThreshold = 50.0;

        public Form1()
        {
            InitializeCustomUI();

            // コンポーネント初期化
            _camera = new TeliCamera();
            _measurement = new MeasurementCore();
            _plc = new PlcCommunicator();

            // イベント購読
            _camera.OnFrameCaptured += Camera_OnFrameCaptured;
            _camera.OnError += Camera_OnError;

            // 設定読み込み
            LoadConfig();

            // フォームイベント
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
        }

        private void InitializeCustomUI()
        {
            this.Text = "Auto Trigger Inspection System";
            this.Size = new Size(1000, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            // PictureBox
            _pictureBox = new PictureBox();
            _pictureBox.Location = new Point(10, 10);
            _pictureBox.Size = new Size(640, 480);
            _pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            _pictureBox.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(_pictureBox);

            // コントロールパネル (右側)
            int panelX = 660;
            int y = 10;
            int labelWidth = 120;
            int controlWidth = 100;
            int lineHeight = 30;

            // ステータス表示
            _lblStatus = new Label { Text = "Status: WAITING", Location = new Point(panelX, y), AutoSize = true, Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold) };
            this.Controls.Add(_lblStatus);
            y += 40;

            _lblBrightness = new Label { Text = "Brightness: 0.0", Location = new Point(panelX, y), AutoSize = true };
            this.Controls.Add(_lblBrightness);
            y += 40;

            // 設定コントロール生成ヘルパー
            void AddSettingControl(string labelText, ref NumericUpDown nud, decimal min, decimal max, decimal initial, int decimalPlaces = 0)
            {
                Label lbl = new Label { Text = labelText, Location = new Point(panelX, y), Size = new Size(labelWidth, 20) };
                this.Controls.Add(lbl);

                nud = new NumericUpDown();
                nud.Location = new Point(panelX + labelWidth, y);
                nud.Size = new Size(controlWidth, 20);
                nud.DecimalPlaces = decimalPlaces;
                // クラッシュ回避: Value設定前にMaximumを設定
                nud.Maximum = max;
                nud.Minimum = min;
                nud.Value = initial;
                this.Controls.Add(nud);
                y += lineHeight;
            }

            AddSettingControl("Trigger Thresh:", ref _nudTriggerThreshold, 0, 255, (decimal)_triggerThreshold, 1);
            AddSettingControl("Stability (ms):", ref _nudStabilityDuration, 0, 5000, _stabilityDurationMs);
            AddSettingControl("Reset Thresh:", ref _nudResetThreshold, 0, 255, (decimal)_resetThreshold, 1);

            y += 10;
            Label lblRoi = new Label { Text = "ROI Settings (X, Y, W, H):", Location = new Point(panelX, y), AutoSize = true };
            this.Controls.Add(lblRoi);
            y += 25;

            AddSettingControl("ROI X:", ref _nudRoiX, 0, 2000, _roi.X);
            AddSettingControl("ROI Y:", ref _nudRoiY, 0, 2000, _roi.Y);
            AddSettingControl("ROI W:", ref _nudRoiW, 1, 2000, _roi.Width);
            AddSettingControl("ROI H:", ref _nudRoiH, 1, 2000, _roi.Height);

            // 更新イベントハンドラ
            EventHandler updateSettings = (s, e) => { UpdateSettingsFromUI(); };
            _nudTriggerThreshold.ValueChanged += updateSettings;
            _nudStabilityDuration.ValueChanged += updateSettings;
            _nudResetThreshold.ValueChanged += updateSettings;
            _nudRoiX.ValueChanged += updateSettings;
            _nudRoiY.ValueChanged += updateSettings;
            _nudRoiW.ValueChanged += updateSettings;
            _nudRoiH.ValueChanged += updateSettings;

            // 保存ボタン
            _btnSaveConfig = new Button { Text = "Save Config", Location = new Point(panelX, y + 10), Size = new Size(controlWidth * 2, 30) };
            _btnSaveConfig.Click += (s, e) => { SaveConfig(); MessageBox.Show("Config Saved."); };
            this.Controls.Add(_btnSaveConfig);
        }

        private void UpdateSettingsFromUI()
        {
            _triggerThreshold = (double)_nudTriggerThreshold.Value;
            _stabilityDurationMs = (int)_nudStabilityDuration.Value;
            _resetThreshold = (double)_nudResetThreshold.Value;
            _roi = new CvRect((int)_nudRoiX.Value, (int)_nudRoiY.Value, (int)_nudRoiW.Value, (int)_nudRoiH.Value);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _camera.Connect();
            _camera.StartStream();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _camera.Disconnect();
            _camera.Dispose();
            _plc.Dispose();
            SaveConfig();
        }

        private void Camera_OnError(object sender, string message)
        {
            this.Invoke((MethodInvoker)(() =>
            {
                MessageBox.Show($"Camera Error: {message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }

        // Push型通信: カメラからのフレーム取得イベント
        private void Camera_OnFrameCaptured(object sender, Mat frame)
        {
            // UIスレッドで処理を実行
            // Invokeは同期的であり、frameがDisposeされる前に処理を完了する
            try
            {
                this.Invoke((MethodInvoker)(() =>
                {
                    ProcessFrame(frame);
                }));
            }
            catch (ObjectDisposedException)
            {
                // フォームが閉じられている場合などは無視
            }
        }

        private void ProcessFrame(Mat frame)
        {
            if (frame.Empty()) return;

            // ROI領域の輝度計算
            double brightness = _measurement.CalculateBrightness(frame, _roi);

            // ステートマシン更新
            UpdateStateMachine(frame, brightness);

            // 描画用画像の作成 (Mat -> Bitmap)
            // 描画はBitmap上で行うか、Mat上で行ってから変換するか。
            // Mat上で描画したほうが簡単かつ高速。ただしframeはReadonly的な扱いが望ましいが、ここでは直接描画してしまう。
            // (TeliCamera側で都度newしているので影響なし)

            Scalar color = Scalar.White;
            string stateText = "";

            switch (_currentState)
            {
                case STATE_WAITING:
                    color = Scalar.Gray;
                    stateText = "WAITING";
                    break;
                case STATE_STABILIZING:
                    color = Scalar.Yellow;
                    stateText = "STABILIZING";
                    break;
                case STATE_INSPECTED:
                    color = Scalar.Lime; // Green
                    stateText = "INSPECTED";
                    break;
            }

            // ROI描画
            Cv2.Rectangle(frame, _roi, color, 2);

            // テキスト描画 (OpenCVのPutTextは日本語不可だが、今回は英語)
            Cv2.PutText(frame, $"State: {stateText}", new CvPoint(20, 50), HersheyFonts.HersheySimplex, 1.0, color, 2);
            Cv2.PutText(frame, $"Brightness: {brightness:F1}", new CvPoint(20, 90), HersheyFonts.HersheySimplex, 0.7, Scalar.White, 1);

            // Bitmap変換して表示
            Bitmap bmp = BitmapConverter.ToBitmap(frame);

            // 古いBitmapを破棄
            Image old = _pictureBox.Image;
            _pictureBox.Image = bmp;
            if (old != null) old.Dispose();

            // ラベル更新
            _lblStatus.Text = $"Status: {stateText}";
            _lblStatus.ForeColor = Color.FromArgb((int)color.Val2, (int)color.Val1, (int)color.Val0); // BGR to RGB? No, Color is ARGB. Scalar is BGR usually.
            // Scalar is B, G, R. Color.FromArgb(r, g, b).
            // Actually Scalar is double[4]. OpenCV uses BGR.
            // So Val0=B, Val1=G, Val2=R.

            // Simple Color mapping
            if (_currentState == STATE_WAITING) _lblStatus.ForeColor = Color.Gray;
            else if (_currentState == STATE_STABILIZING) _lblStatus.ForeColor = Color.Gold; // Yellowish
            else if (_currentState == STATE_INSPECTED) _lblStatus.ForeColor = Color.Lime;

            _lblBrightness.Text = $"Brightness: {brightness:F1}";
        }

        private void UpdateStateMachine(Mat frame, double brightness)
        {
            switch (_currentState)
            {
                case STATE_WAITING:
                    if (brightness > _triggerThreshold)
                    {
                        _currentState = STATE_STABILIZING;
                        _stabilityStartTime = DateTime.Now;
                        Debug.WriteLine("Transition: WAITING -> STABILIZING");
                    }
                    break;

                case STATE_STABILIZING:
                    // 輝度が下がったらリセット
                    if (brightness < _resetThreshold) // Wait, logic says "Wait until brightness > Trigger". If it drops, it means part left or noise.
                    {
                        // 輝度が下がったらWaitingに戻る (仕様: "途中で輝度が下がったら WAITING に戻る")
                        // 下がる基準は triggerThreshold なのか resetThreshold なのか？
                        // Pythonコード: if brightness < RESET_THRESHOLD: current_state = STATE_WAITING
                        // 仕様文: "途中で輝度が下がったら WAITING に戻る" -> おそらくTriggerを下回るか、Resetを下回るか。
                        // Pythonコードに従うなら RESET_THRESHOLD。
                        if (brightness < _resetThreshold)
                        {
                            _currentState = STATE_WAITING;
                            Debug.WriteLine("Transition: STABILIZING -> WAITING (Brightness drop)");
                        }
                    }
                    else
                    {
                        // 時間経過チェック
                        TimeSpan elapsed = DateTime.Now - _stabilityStartTime;
                        if (elapsed.TotalMilliseconds > _stabilityDurationMs)
                        {
                            // 検査実行
                            int result = _measurement.Inspect(frame);
                            _plc.SendResult(result); // 結果送信

                            _currentState = STATE_INSPECTED;
                            Debug.WriteLine("Transition: STABILIZING -> INSPECTED");
                        }
                    }
                    break;

                case STATE_INSPECTED:
                    // 部品がなくなるまで待機
                    if (brightness < _resetThreshold)
                    {
                        _currentState = STATE_WAITING;
                        Debug.WriteLine("Transition: INSPECTED -> WAITING");
                    }
                    break;
            }
        }

        private void LoadConfig()
        {
            string path = "config.txt";
            if (!File.Exists(path)) return;

            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    if (key == "TriggerThreshold") _triggerThreshold = double.Parse(val);
                    else if (key == "StabilityDurationMs") _stabilityDurationMs = int.Parse(val);
                    else if (key == "ResetThreshold") _resetThreshold = double.Parse(val);
                    else if (key == "RoiX") _roi.X = int.Parse(val);
                    else if (key == "RoiY") _roi.Y = int.Parse(val);
                    else if (key == "RoiW") _roi.Width = int.Parse(val);
                    else if (key == "RoiH") _roi.Height = int.Parse(val);
                }

                // UIに反映
                _nudTriggerThreshold.Value = (decimal)_triggerThreshold;
                _nudStabilityDuration.Value = _stabilityDurationMs;
                _nudResetThreshold.Value = (decimal)_resetThreshold;
                _nudRoiX.Value = _roi.X;
                _nudRoiY.Value = _roi.Y;
                _nudRoiW.Value = _roi.Width;
                _nudRoiH.Value = _roi.Height;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Config load error: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            string path = "config.txt";
            try
            {
                using (StreamWriter sw = new StreamWriter(path))
                {
                    sw.WriteLine($"TriggerThreshold={_triggerThreshold}");
                    sw.WriteLine($"StabilityDurationMs={_stabilityDurationMs}");
                    sw.WriteLine($"ResetThreshold={_resetThreshold}");
                    sw.WriteLine($"RoiX={_roi.X}");
                    sw.WriteLine($"RoiY={_roi.Y}");
                    sw.WriteLine($"RoiW={_roi.Width}");
                    sw.WriteLine($"RoiH={_roi.Height}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Config save error: {ex.Message}");
            }
        }
    }
}
