using System;
using System.Threading;
using OpenCvSharp;
// ご提示の動作実績のある名前空間を使用
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

        public bool Initialize()
        {
            try
            {
                camSystem = new CameraSystem();
                // BU160MCF（U3V）に対応
                if (camSystem.Initialize(CameraType.TypeU3v | CameraType.TypeGev) != CamApiStatus.Success) return false;

                int camNum;
                camSystem.GetNumOfCameras(out camNum);
                if (camNum == 0) return false;

                camSystem.CreateDeviceObject(0, ref camDevice);
                if (camDevice!.Open() != CamApiStatus.Success) return false;

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
            captureThread.IsBackground = true; // アプリ終了時にスレッドも終了させる
            captureThread.Start();
        }

        public void StopCapture()
        {
            keepCapturing = false;
            captureThread?.Join(500);
        }

        private void CaptureLoop()
        {
            CameraImageInfo? imageInfo = null;
            int bufferIndex;

            while (keepCapturing)
            {
                // 画像が来るまで待機（1秒タイムアウト）
                if (imageReceivedEvent.WaitOne(1000))
                {
                    if (camDevice!.camStream.GetCurrentBufferIndex(out bufferIndex) == CamApiStatus.Success)
                    {
                        camDevice.camStream.LockBuffer(bufferIndex, ref imageInfo);

                        if (imageInfo != null && imageInfo.BufferPointer != IntPtr.Zero)
                        {
                            try
                            {
                                int rows = (int)imageInfo.SizeY;
                                int cols = (int)imageInfo.SizeX;

                                // 指示通りの Mat.FromPixelData を使用（歪み回避のためBGR24へ変換）
                                // まず変換後の受け皿となるMatを生成
                                using (Mat colorMat = new Mat(rows, cols, MatType.CV_8UC3))
                                {
                                    // ご提示のコードと同じシグネチャで変換を実行
                                    CameraUtility.ConvertImage(
                                        DstPixelFormat.BGR24,
                                        imageInfo.PixelFormat,
                                        true,
                                        colorMat.Data, // IntPtr
                                        imageInfo.BufferPointer,
                                        imageInfo.SizeX,
                                        imageInfo.SizeY
                                    );

                                    // 検査処理用のグレースケール変換
                                    Mat grayMat = new Mat();
                                    Cv2.CvtColor(colorMat, grayMat, ColorConversionCodes.BGR2GRAY);

                                    // イベント発火
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
            camSystem?.Terminate();
        }

        public void Dispose() => Terminate();
    }
}