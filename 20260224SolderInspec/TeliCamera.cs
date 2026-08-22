using System;
using System.Threading;
using OpenCvSharp;
using Teli.TeliCamAPI.NET;
using Teli.TeliCamAPI.NET.Utility;

namespace _20260224SolderInspec
{
    public class TeliCamera : IDisposable
    {
        private CameraSystem? camSystem;
        private CameraDevice? camDevice;
        private AutoResetEvent imageReceivedEvent = new AutoResetEvent(false);
        private int maxPayloadSize = 0;
        private volatile bool keepCapturing = false;
        private Thread? captureThread;

        // 画像処理側へ渡すイベント
        public event EventHandler<Mat>? OnFrameCaptured;

        public bool IsConnected => camDevice != null;

        public bool Initialize(int cameraIndex = 0)
        {
            try
            {
                camSystem = new CameraSystem();
                // BU160MCF（U3V）等に対応
                if (camSystem.Initialize(CameraType.TypeU3v | CameraType.TypeGev) != CamApiStatus.Success) return false;

                int camNum;
                camSystem.GetNumOfCameras(out camNum);
                if (camNum == 0 || cameraIndex >= camNum) return false;

                camSystem.CreateDeviceObject(cameraIndex, ref camDevice);
                if (camDevice.Open() != CamApiStatus.Success) return false;

                // =================================================================
                // ★カメラ本体の15FPS強制ロック（192FPSのデータ洪水を根本から防ぐ）
                // =================================================================
                try
                {
                    // 1. オート露出を「手動（Manual）」に固定する（ロック解除）
                    camDevice.camControl.SetExposureTimeControl(CameraExposureTimeCtrl.Manual);

                    // 2. フレームレートの制御を「手動（Manual）」に有効化する
                    if (camDevice.IsSupportIIDC2)
                    {
                        camDevice.camControl.SetAcquisitionFrameRateControl(CameraAcqFrameRateCtrl.Manual);
                    }

                    // 3. フレームレートを「15.0」に強制固定！
                    camDevice.camControl.SetAcquisitionFrameRate(15.0);

                    System.Diagnostics.Debug.WriteLine("FPSを15.0に強制固定しました。");
                }
                catch (Exception ex)
                {
                    // 万が一カメラ側が対応していなくても、エラーで落ちずに検査へ進む
                    System.Diagnostics.Debug.WriteLine($"FPS強制設定エラー: {ex.Message}");
                }
                // =================================================================

                // 16バッファでストリーム開始準備
                if (camDevice.camStream.Open(imageReceivedEvent, 16, 0, out maxPayloadSize) != CamApiStatus.Success) return false;
                if (camDevice.camStream.Start() != CamApiStatus.Success) return false;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void StartCapture()
        {
            if (keepCapturing) return;
            keepCapturing = true;
            captureThread = new Thread(CaptureLoop);
            captureThread.IsBackground = true; // アプリ終了時にスレッドも道連れにして終了させる
            captureThread.Start();
        }

        public void StopCapture()
        {
            keepCapturing = false;
            if (captureThread != null && captureThread.IsAlive)
            {
                captureThread.Join(500);
            }
        }

        private void CaptureLoop()
        {
            CameraImageInfo? imageInfo = null;
            int bufferIndex;

            while (keepCapturing && camDevice != null)
            {
                // 画像が来るまで待機（1秒タイムアウト）
                if (imageReceivedEvent.WaitOne(1000))
                {
                    if (camDevice != null && camDevice.camStream.GetCurrentBufferIndex(out bufferIndex) == CamApiStatus.Success)
                    {
                        camDevice.camStream.LockBuffer(bufferIndex, ref imageInfo);

                        if (imageInfo != null && imageInfo.BufferPointer != IntPtr.Zero)
                        {
                            try
                            {
                                int rows = (int)imageInfo.SizeY;
                                int cols = (int)imageInfo.SizeX;

                                // 受け皿となるMatを生成（BGR24）
                                using (Mat colorMat = new Mat(rows, cols, MatType.CV_8UC3))
                                {
                                    // 東芝テリの公式Utilityで安全に画像フォーマットを変換
                                    CameraUtility.ConvertImage(
                                        DstPixelFormat.BGR24,
                                        imageInfo.PixelFormat,
                                        true,
                                        colorMat.Data,
                                        imageInfo.BufferPointer,
                                        imageInfo.SizeX,
                                        imageInfo.SizeY
                                    );

                                    // 検査処理用のグレースケール変換
                                    Mat grayMat = new Mat();
                                    Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

                                    // Form1へ画像を渡す
                                    OnFrameCaptured?.Invoke(this, grayMat);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Capture Error: {ex.Message}");
                            }
                        }
                        camDevice.camStream.UnlockBuffer(bufferIndex);
                    }
                }
            }
        }

        public void Terminate()
        {
            StopCapture();
            if (camDevice != null)
            {
                camDevice.camStream.Stop();
                camDevice.camStream.Close();
                camDevice.Close();
            }
            if (camSystem != null)
            {
                camSystem.Terminate();
            }
        }

        public void Dispose()
        {
            Terminate();
        }
    }
}