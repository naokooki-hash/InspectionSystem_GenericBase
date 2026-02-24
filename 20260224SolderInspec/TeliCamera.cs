using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms; // for MessageBox (optional, but good for error reporting)
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Teli.TeliCamSDK;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using CvPoint = OpenCvSharp.Point;

namespace _20260224SolderInspec
{
    public class TeliCamera : IDisposable
    {
        private Teli.TeliCamSDK.TeliCamSystem _sys;
        private Teli.TeliCamSDK.CamDevice _cam;
        private bool _isStreaming;
        private object _lockObj = new object();

        // Push型通信のためのイベント
        public event EventHandler<Mat> OnFrameCaptured;
        public event EventHandler<string> OnError;

        public bool IsConnected => _cam != null && _cam.IsOpen;
        public bool IsStreaming => _isStreaming;

        public TeliCamera()
        {
            try
            {
                _sys = new Teli.TeliCamSDK.TeliCamSystem(Teli.TeliCamSDK.CameraType.TypeGigE | Teli.TeliCamSDK.CameraType.TypeU3v);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize TeliCamSystem: {ex.Message}");
                OnError?.Invoke(this, $"Failed to initialize TeliCamSystem: {ex.Message}");
            }
        }

        public void Connect()
        {
            if (_sys == null) return;

            try
            {
                int numCameras = _sys.GetNumOfCameras();
                if (numCameras == 0)
                {
                    OnError?.Invoke(this, "No cameras found.");
                    return;
                }

                // 最初のカメラを開く
                _cam = _sys.GetCamera(0);
                if (_cam != null)
                {
                    _cam.Open();

                    // ストリームイベントの登録
                    _cam.CamStreamEvent += _cam_CamStreamEvent;
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Failed to connect camera: {ex.Message}");
            }
        }

        public void StartStream()
        {
            if (_cam != null && _cam.IsOpen && !_isStreaming)
            {
                try
                {
                    _cam.StartStream();
                    _isStreaming = true;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"Failed to start stream: {ex.Message}");
                }
            }
        }

        public void StopStream()
        {
            if (_cam != null && _cam.IsOpen && _isStreaming)
            {
                try
                {
                    _cam.StopStream();
                    _isStreaming = false;
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"Failed to stop stream: {ex.Message}");
                }
            }
        }

        public void Disconnect()
        {
            StopStream();
            if (_cam != null)
            {
                if (_cam.IsOpen)
                {
                    _cam.CamStreamEvent -= _cam_CamStreamEvent;
                    _cam.Close();
                }
                _cam = null; // Dispose is handled by the system or GC usually, but setting null helps
            }
        }

        private void _cam_CamStreamEvent(object sender, Teli.TeliCamSDK.CamStreamEventArgs e)
        {
            // Push型通信の実装
            // 画像取得と変換（重要：画像4重化回避）

            if (e.Error != Teli.TeliCamSDK.CamApiError.OK)
            {
                // エラーハンドリング
                return;
            }

            try
            {
                // 生画像の取得
                Teli.TeliCamSDK.CamImage rawImage = e.GetCamImage();
                if (rawImage == null) return;

                // 重要ルール2: 画像4重化（ストライドパディング）の回避
                // CameraUtility.ConvertImage を使用し、DstPixelFormat.BGR24 に変換する

                // 変換後のバッファサイズ計算
                int width = (int)rawImage.Width;
                int height = (int)rawImage.Height;
                // BGR24は1ピクセル3バイト
                int convertedSize = width * height * 3;
                byte[] convertedBuffer = new byte[convertedSize];

                GCHandle handle = GCHandle.Alloc(convertedBuffer, GCHandleType.Pinned);
                IntPtr convertedPtr = handle.AddrOfPinnedObject();

                try
                {
                    // ConvertImageの使用
                    Teli.TeliCamSDK.CameraUtility.ConvertImage(
                        rawImage,
                        convertedPtr,
                        (uint)convertedSize,
                        Teli.TeliCamSDK.DstPixelFormat.BGR24
                    );

                    // OpenCV Matへの変換
                    using (Mat matBgr = new Mat(height, width, MatType.CV_8UC3, convertedPtr))
                    {
                        // グレースケールに変換（処理用）
                        using (Mat matGray = new Mat())
                        {
                            Cv2.CvtColor(matBgr, matGray, ColorConversionCodes.BGR2GRAY);

                            // イベント発火 (同期的に処理されることを想定)
                            OnFrameCaptured?.Invoke(this, matGray);
                        }
                    }
                }
                finally
                {
                    if (handle.IsAllocated)
                        handle.Free();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing frame: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Disconnect();
            if (_sys != null)
            {
                _sys.Terminate();
                _sys = null;
            }
        }
    }
}
