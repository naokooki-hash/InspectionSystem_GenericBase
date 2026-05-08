using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace _20260224SolderInspec
{
    public class MeasurementCore
    {
        public bool EnableJigCheck { get; set; } = true;

        // ★追加：穴ではなく外形エッジで傾斜を計算するモード
        public bool UseOuterEdgeForTilt { get; set; } = false;

        public double TargetXOffsetMm { get; set; } = 0.0;
        public double OffsetToleranceMm { get; set; } = 1.0;
        public double TargetAngleDeg { get; set; } = 0.0;
        public double AngleToleranceDeg { get; set; } = 2.0;

        public CvRect BtmMeasureRoi { get; set; } = new CvRect(250, 350, 150, 80);
        public CvRect HolesRoi { get; set; } = new CvRect(250, 350, 600, 200);

        // ★追加：外形エッジ（青枠）用のROI
        public CvRect TiltLeftRoi { get; set; } = new CvRect(100, 250, 80, 200);
        public CvRect TiltRightRoi { get; set; } = new CvRect(900, 250, 80, 200);

        public int MinHoleArea { get; set; } = 300;
        public int MaxHoleArea { get; set; } = 3000;

        public int SplitBoundaryX { get; set; } = 320;
        public int SplitBoundaryY { get; set; } = 570;
        public int ThreshTopLeft { get; set; } = 12;
        public int ThreshTopRight { get; set; } = 12;
        public int ThreshBtmLeft { get; set; } = 51;
        public int ThreshBtmRight { get; set; } = 51;

        public double MinCircularity { get; set; } = 0.4;

        public double PixelToMmRatio { get; set; } = 0.05;
        public double LastHoleDistancePx { get; private set; } = 0.0;

        public CvRect JigLeftRoi { get; set; } = new CvRect(280, 315, 200, 200);
        public CvRect JigRightRoi { get; set; } = new CvRect(820, 315, 200, 200);

        public double TargetJigDistanceMm { get; set; } = 40.0;
        public double JigToleranceMm { get; set; } = 1.5;

        public bool IsJigOk { get; private set; } = false;
        public double LastJigDistanceMm { get; private set; } = 0.0;

        private int _jigLeftEdgeX = 0, _jigRightEdgeX = 0;
        private bool _jigEdgeDetected = false;

        private CvPoint _topCenter = new CvPoint(0, 0);
        private CvPoint _btmCenter = new CvPoint(0, 0);
        private List<CvPoint> _detectedHoles = new List<CvPoint>();

        // ★追加：外形エッジ描画用の変数
        private CvPoint _tiltLeftPt1, _tiltLeftPt2;
        private CvPoint _tiltRightPt1, _tiltRightPt2;
        private bool _tiltEdgesFound = false;

        private CvPoint _btmAxisPt1, _btmAxisPt2;
        private CvPoint _projectedBtm;

        private readonly object _imageLock = new object();
        private Mat _lastBinaryHole = new Mat();

        private double _lastXOffsetMm, _lastAngle;
        private int _lastResult = 0;
        private bool _isOffsetOk = false, _isAngleOk = false, _hasValidData = false;

        public void GetDebugImage(Mat dst)
        {
            lock (_imageLock)
            {
                if (_lastBinaryHole != null && !_lastBinaryHole.Empty())
                {
                    _lastBinaryHole.CopyTo(dst);
                }
            }
        }

        private Mat CreateFourSplitBinary(Mat gray, bool invert)
        {
            Mat bin = new Mat(gray.Size(), MatType.CV_8UC1, new Scalar(0));
            int splitX = Math.Max(0, Math.Min(SplitBoundaryX, gray.Width));
            int splitY = Math.Max(0, Math.Min(SplitBoundaryY, gray.Height));
            var type = invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;

            if (splitX > 0 && splitY > 0)
            {
                using (Mat subGray = new Mat(gray, new CvRect(0, 0, splitX, splitY)))
                using (Mat subBin = new Mat(bin, new CvRect(0, 0, splitX, splitY)))
                    Cv2.Threshold(subGray, subBin, ThreshTopLeft, 255, type);
            }
            if (splitX < gray.Width && splitY > 0)
            {
                using (Mat subGray = new Mat(gray, new CvRect(splitX, 0, gray.Width - splitX, splitY)))
                using (Mat subBin = new Mat(bin, new CvRect(splitX, 0, gray.Width - splitX, splitY)))
                    Cv2.Threshold(subGray, subBin, ThreshTopRight, 255, type);
            }
            if (splitX > 0 && splitY < gray.Height)
            {
                using (Mat subGray = new Mat(gray, new CvRect(0, splitY, splitX, gray.Height - splitY)))
                using (Mat subBin = new Mat(bin, new CvRect(0, splitY, splitX, gray.Height - splitY)))
                    Cv2.Threshold(subGray, subBin, ThreshBtmLeft, 255, type);
            }
            if (splitX < gray.Width && splitY < gray.Height)
            {
                using (Mat subGray = new Mat(gray, new CvRect(splitX, splitY, gray.Width - splitX, gray.Height - splitY)))
                using (Mat subBin = new Mat(bin, new CvRect(splitX, splitY, gray.Width - splitX, gray.Height - splitY)))
                    Cv2.Threshold(subGray, subBin, ThreshBtmRight, 255, type);
            }
            return bin;
        }

        public void UpdateDebugImageRealtime(Mat frame, CvRect debugRoi)
        {
            using (Mat gray = new Mat())
            {
                if (frame.Channels() == 3) Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                else frame.CopyTo(gray);

                using (Mat blurred = new Mat())
                {
                    Cv2.GaussianBlur(gray, blurred, new CvSize(5, 5), 0);

                    using (Mat binFull = CreateFourSplitBinary(blurred, true))
                    {
                        Cv2.Line(binFull, new CvPoint(0, SplitBoundaryY), new CvPoint(binFull.Width, SplitBoundaryY), Scalar.Gray, 2);
                        Cv2.Line(binFull, new CvPoint(SplitBoundaryX, 0), new CvPoint(SplitBoundaryX, binFull.Height), Scalar.Gray, 2);

                        CvRect safeDebugRoi = debugRoi & new CvRect(0, 0, binFull.Width, binFull.Height);
                        if (safeDebugRoi.Width > 0 && safeDebugRoi.Height > 0)
                        {
                            lock (_imageLock)
                            {
                                using (Mat cropped = new Mat(binFull, safeDebugRoi))
                                {
                                    cropped.CopyTo(_lastBinaryHole);
                                }
                            }
                        }
                    }
                }
            }
        }

        public double CalculateBrightness(Mat frame, CvRect roi)
        {
            if (frame == null || frame.Empty()) return 0;
            CvRect safeRoi = roi & new CvRect(0, 0, frame.Width, frame.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return 0;
            using (Mat roiMat = new Mat(frame, safeRoi)) { return Cv2.Mean(roiMat).Val0; }
        }

        private bool DetectJigEdge(Mat binFull, CvRect roi, bool isLeft, out int edgeX)
        {
            edgeX = 0;
            CvRect safeRoi = roi & new CvRect(0, 0, binFull.Width, binFull.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return false;

            using (Mat bin = new Mat(binFull, safeRoi))
            {
                Cv2.FindContours(bin, out CvPoint[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                if (contours.Length == 0) return false;

                var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
                var rect = Cv2.BoundingRect(largestContour);

                if (rect.Width < 5 || rect.Height < 5) return false;

                if (isLeft) edgeX = safeRoi.X + rect.Right;
                else edgeX = safeRoi.X + rect.Left;

                return true;
            }
        }

        // ★追加：外形エッジの全ピクセルから直線を近似するメソッド
        private bool FindVerticalEdgeLine(Mat binFull, CvRect roi, bool isLeftEdge, out double x0, out double y0, out double vx, out double vy)
        {
            x0 = y0 = vx = vy = 0;
            CvRect safeRoi = roi & new CvRect(0, 0, binFull.Width, binFull.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(binFull, safeRoi))
            {
                List<Point2f> edgePoints = new List<Point2f>();
                for (int y = 0; y < roiMat.Rows; y++)
                {
                    if (isLeftEdge)
                    {
                        for (int x = 0; x < roiMat.Cols; x++)
                        {
                            if (roiMat.At<byte>(y, x) == 255) { edgePoints.Add(new Point2f(x + safeRoi.X, y + safeRoi.Y)); break; }
                        }
                    }
                    else
                    {
                        for (int x = roiMat.Cols - 1; x >= 0; x--)
                        {
                            if (roiMat.At<byte>(y, x) == 255) { edgePoints.Add(new Point2f(x + safeRoi.X, y + safeRoi.Y)); break; }
                        }
                    }
                }

                if (edgePoints.Count >= 20) // ロバスト化：最低20点のピクセルが必要
                {
                    // L2（最小二乗法）で直線近似
                    Line2D line = Cv2.FitLine(edgePoints, DistanceTypes.L2, 0, 0.01, 0.01);
                    vx = line.Vx; vy = line.Vy; x0 = line.X1; y0 = line.Y1;
                    if (vy < 0) { vx = -vx; vy = -vy; } // ベクトルを必ず下向きに統一
                    return true;
                }
            }
            return false;
        }

        public int Inspect(Mat frame, CvRect debugRoi, bool isDebugMode)
        {
            _hasValidData = false;
            try
            {
                using (Mat gray = new Mat())
                {
                    if (frame.Channels() == 3) Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                    else frame.CopyTo(gray);

                    using (Mat blurred = new Mat())
                    {
                        Cv2.GaussianBlur(gray, blurred, new CvSize(5, 5), 0);

                        using (Mat binNormal = CreateFourSplitBinary(blurred, false))
                        using (Mat binInv = CreateFourSplitBinary(blurred, true))
                        {
                            // ==========================================
                            // 1. 下部・上部エッジ距離チェック (Jig)
                            // ==========================================
                            _jigEdgeDetected = false;
                            IsJigOk = false;

                            if (EnableJigCheck)
                            {
                                if (DetectJigEdge(binNormal, JigLeftRoi, true, out _jigLeftEdgeX) &&
                                    DetectJigEdge(binNormal, JigRightRoi, false, out _jigRightEdgeX))
                                {
                                    _jigEdgeDetected = true;
                                    double distancePx = Math.Abs(_jigRightEdgeX - _jigLeftEdgeX);
                                    LastJigDistanceMm = distancePx * PixelToMmRatio;
                                    if (TargetJigDistanceMm <= 0) TargetJigDistanceMm = LastJigDistanceMm;
                                    IsJigOk = (Math.Abs(LastJigDistanceMm - TargetJigDistanceMm) <= JigToleranceMm);
                                }
                            }

                            // ==========================================
                            // 2. 製品上部の傾斜・中心座標の取得 (モード分岐)
                            // ==========================================
                            double top_tilt_rad = 0;
                            _tiltEdgesFound = false;
                            _detectedHoles.Clear();

                            if (UseOuterEdgeForTilt)
                            {
                                // ★ モードA：外形エッジの直線近似から計算
                                bool lFound = FindVerticalEdgeLine(binNormal, TiltLeftRoi, true, out double lx0, out double ly0, out double lvx, out double lvy);
                                bool rFound = FindVerticalEdgeLine(binNormal, TiltRightRoi, false, out double rx0, out double ry0, out double rvx, out double rvy);

                                if (lFound && rFound)
                                {
                                    _tiltEdgesFound = true;

                                    // Y軸からの角度を計算
                                    double lAngle = Math.Atan2(lvx, lvy);
                                    double rAngle = Math.Atan2(rvx, rvy);

                                    // 左右の角度を足して2で割る（ご提案通りの最強メソッド）
                                    top_tilt_rad = (lAngle + rAngle) / 2.0;

                                    // 描画用の線分を生成
                                    _tiltLeftPt1 = new CvPoint((int)(lx0 - 500 * lvx), (int)(ly0 - 500 * lvy));
                                    _tiltLeftPt2 = new CvPoint((int)(lx0 + 500 * lvx), (int)(ly0 + 500 * lvy));
                                    _tiltRightPt1 = new CvPoint((int)(rx0 - 500 * rvx), (int)(ry0 - 500 * rvy));
                                    _tiltRightPt2 = new CvPoint((int)(rx0 + 500 * rvx), (int)(ry0 + 500 * rvy));

                                    // 中心点（_topCenter）の導出（ROIの真ん中のY座標における左右のXの平均値）
                                    double refY = (TiltLeftRoi.Y + TiltLeftRoi.Height / 2.0 + TiltRightRoi.Y + TiltRightRoi.Height / 2.0) / 2.0;
                                    double lX = lx0 + ((refY - ly0) / lvy) * lvx;
                                    double rX = rx0 + ((refY - ry0) / rvy) * rvx;
                                    _topCenter = new CvPoint((int)((lX + rX) / 2.0), (int)refY);

                                    // ピクセル比率計算用に、外形間の距離を記録
                                    LastHoleDistancePx = Math.Abs(rX - lX);
                                }
                            }
                            else
                            {
                                // ★ モードB：従来の穴検出から計算
                                CvRect safeHolesRoi = HolesRoi & new CvRect(0, 0, binInv.Width, binInv.Height);
                                if (safeHolesRoi.Width > 0 && safeHolesRoi.Height > 0)
                                {
                                    using (Mat holeBin = new Mat(binInv, safeHolesRoi))
                                    {
                                        using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new CvSize(5, 5)))
                                        {
                                            Cv2.MorphologyEx(holeBin, holeBin, MorphTypes.Close, kernel);
                                        }

                                        Cv2.FindContours(holeBin, out CvPoint[][] hContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                                        foreach (var c in hContours)
                                        {
                                            double area = Cv2.ContourArea(c);
                                            if (area < MinHoleArea || area > MaxHoleArea) continue;

                                            double perimeter = Cv2.ArcLength(c, true);
                                            if (perimeter > 0)
                                            {
                                                double circularity = (4 * Math.PI * area) / (perimeter * perimeter);
                                                if (circularity < MinCircularity) continue;
                                            }

                                            var m = Cv2.Moments(c);
                                            if (m.M00 > 0)
                                            {
                                                _detectedHoles.Add(new CvPoint((int)(m.M10 / m.M00) + safeHolesRoi.X, (int)(m.M01 / m.M00) + safeHolesRoi.Y));
                                            }
                                        }
                                    }
                                }

                                if (_detectedHoles.Count >= 2)
                                {
                                    var sorted = _detectedHoles.OrderBy(p => p.X).ToList();
                                    var leftHole = sorted.First(); var rightHole = sorted.Last();

                                    _topCenter = new CvPoint((leftHole.X + rightHole.X) / 2, (leftHole.Y + rightHole.Y) / 2);
                                    top_tilt_rad = Math.Atan2(rightHole.Y - leftHole.Y, rightHole.X - leftHole.X);
                                    LastHoleDistancePx = Math.Sqrt(Math.Pow(rightHole.X - leftHole.X, 2) + Math.Pow(rightHole.Y - leftHole.Y, 2));
                                }
                            }

                            // ==========================================
                            // 3. 下部検出 (BTM仮想中心軸の導出)
                            // ==========================================
                            bool isBtmDetected = false;
                            double btm_tilt_rad = 0;
                            CvRect safeBtmRoi = BtmMeasureRoi & new CvRect(0, 0, binNormal.Width, binNormal.Height);
                            if (safeBtmRoi.Width > 0 && safeBtmRoi.Height > 0)
                            {
                                using (Mat btmBin = new Mat(binNormal, safeBtmRoi))
                                {
                                    List<Point2f> centerPoints = new List<Point2f>();

                                    for (int y = 0; y < btmBin.Rows; y++)
                                    {
                                        int leftX = -1;
                                        for (int x = 0; x < btmBin.Cols; x++)
                                        {
                                            if (btmBin.At<byte>(y, x) == 255) { leftX = x; break; }
                                        }
                                        int rightX = -1;
                                        for (int x = btmBin.Cols - 1; x >= 0; x--)
                                        {
                                            if (btmBin.At<byte>(y, x) == 255) { rightX = x; break; }
                                        }

                                        if (leftX != -1 && rightX != -1 && rightX > leftX)
                                        {
                                            float centerX = (leftX + rightX) / 2.0f;
                                            centerPoints.Add(new Point2f(centerX + safeBtmRoi.X, y + safeBtmRoi.Y));
                                        }
                                    }

                                    if (centerPoints.Count >= 5)
                                    {
                                        Line2D btmLine = Cv2.FitLine(centerPoints, DistanceTypes.L2, 0, 0.01, 0.01);
                                        double vx = btmLine.Vx;
                                        double vy = btmLine.Vy;
                                        double x0 = btmLine.X1;
                                        double y0 = btmLine.Y1;

                                        if (vy < 0) { vx = -vx; vy = -vy; }

                                        _btmAxisPt1 = new CvPoint((int)(x0 - 500.0 * vx), (int)(y0 - 500.0 * vy));
                                        _btmAxisPt2 = new CvPoint((int)(x0 + 500.0 * vx), (int)(y0 + 500.0 * vy));

                                        double targetY = safeBtmRoi.Y + safeBtmRoi.Height / 2.0;
                                        double t = (targetY - y0) / vy;
                                        double btmCenterX = x0 + t * vx;

                                        _btmCenter = new CvPoint((int)btmCenterX, (int)targetY);
                                        btm_tilt_rad = Math.Atan2(vx, vy);
                                        isBtmDetected = true;
                                    }
                                }
                            }

                            _hasValidData = true;

                            // ==========================================
                            // 4. 垂線投影・ズレ計算 ＆ 総合判定
                            // ==========================================
                            // 穴モードまたは外形エッジモードのどちらかが成功していれば計算へ
                            bool isTopDetected = UseOuterEdgeForTilt ? _tiltEdgesFound : (_detectedHoles.Count >= 2);

                            if (isTopDetected && isBtmDetected)
                            {
                                _lastAngle = (top_tilt_rad - btm_tilt_rad) * 180.0 / Math.PI;

                                double dy = _btmCenter.Y - _topCenter.Y;
                                double expected_btm_X = _topCenter.X - dy * Math.Tan(top_tilt_rad);
                                _projectedBtm = new CvPoint((int)expected_btm_X, _btmCenter.Y);

                                _lastXOffsetMm = (_btmCenter.X - expected_btm_X) * PixelToMmRatio;

                                _isOffsetOk = Math.Abs(_lastXOffsetMm - TargetXOffsetMm) <= OffsetToleranceMm;
                                _isAngleOk = Math.Abs(_lastAngle - TargetAngleDeg) <= AngleToleranceDeg;
                            }
                            else
                            {
                                _isOffsetOk = false;
                                _isAngleOk = false;
                            }

                            // 判定優先順位
                            if (!isTopDetected) return 3; // 上部（穴またはエッジ）が見つからない
                            if (!isBtmDetected) return 3; // 下部線が見つからない
                            if (EnableJigCheck && (!_jigEdgeDetected || !IsJigOk)) return 2; // エッジチェックNG

                            _lastResult = (_isOffsetOk && _isAngleOk) ? 1 : 2;
                            return _lastResult;
                        }
                    }
                }
            }
            catch { return 3; }
        }

        public void DrawOverlay(Mat dispMat)
        {
            if (!_hasValidData) return;

            // モードに応じた上部の描画
            if (UseOuterEdgeForTilt && _tiltEdgesFound)
            {
                // 外形エッジの描画（シアン色の直線）
                Cv2.Line(dispMat, _tiltLeftPt1, _tiltLeftPt2, Scalar.Cyan, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, _tiltRightPt1, _tiltRightPt2, Scalar.Cyan, 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(dispMat, _topCenter, Scalar.Red, MarkerTypes.Cross, 20, 2);
            }
            else if (!UseOuterEdgeForTilt && _detectedHoles.Count >= 2)
            {
                // 従来の穴描画
                var sorted = _detectedHoles.OrderBy(p => p.X).ToList();
                foreach (var h in _detectedHoles) Cv2.Circle(dispMat, h, 8, Scalar.Orange, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, sorted.First(), sorted.Last(), Scalar.Yellow, 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(dispMat, _topCenter, Scalar.Red, MarkerTypes.Cross, 20, 2);
            }

            // 下部軸と結果の描画
            bool isTopDetected = UseOuterEdgeForTilt ? _tiltEdgesFound : (_detectedHoles.Count >= 2);
            if (isTopDetected && _btmCenter.X != 0 && _btmCenter.Y != 0)
            {
                Cv2.Line(dispMat, _btmAxisPt1, _btmAxisPt2, new Scalar(255, 255, 0), 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, _topCenter, _projectedBtm, Scalar.Magenta, 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(dispMat, _btmCenter, Scalar.Blue, MarkerTypes.Cross, 20, 2);

                int tx = Math.Max(_topCenter.X, _btmCenter.X) + 40, ty = (_topCenter.Y + _btmCenter.Y) / 2;
                string offStr = "X Offset: " + _lastXOffsetMm.ToString("+0.00;-0.00") + "mm";
                string angStr = "Angle: " + _lastAngle.ToString("+0.00;-0.00") + "deg";
                Cv2.PutText(dispMat, offStr, new CvPoint(tx, ty - 15), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
                Cv2.PutText(dispMat, offStr, new CvPoint(tx, ty - 15), HersheyFonts.HersheySimplex, 0.7, _isOffsetOk ? Scalar.LimeGreen : Scalar.Red, 1);
                Cv2.PutText(dispMat, angStr, new CvPoint(tx, ty + 20), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
                Cv2.PutText(dispMat, angStr, new CvPoint(tx, ty + 20), HersheyFonts.HersheySimplex, 0.7, _isAngleOk ? Scalar.LimeGreen : Scalar.Red, 1);
            }

            // エッジ間距離描画
            if (EnableJigCheck && _jigEdgeDetected)
            {
                Scalar edgeCol = IsJigOk ? Scalar.LimeGreen : Scalar.Red;

                int lY1 = JigLeftRoi.Y, lY2 = JigLeftRoi.Y + JigLeftRoi.Height;
                Cv2.Line(dispMat, new CvPoint(_jigLeftEdgeX, lY1), new CvPoint(_jigLeftEdgeX, lY2), edgeCol, 2, LineTypes.AntiAlias);

                int rY1 = JigRightRoi.Y, rY2 = JigRightRoi.Y + JigRightRoi.Height;
                Cv2.Line(dispMat, new CvPoint(_jigRightEdgeX, rY1), new CvPoint(_jigRightEdgeX, rY2), edgeCol, 2, LineTypes.AntiAlias);

                int midY = (JigLeftRoi.Y + JigLeftRoi.Height / 2 + JigRightRoi.Y + JigRightRoi.Height / 2) / 2;
                Cv2.Line(dispMat, new CvPoint(_jigLeftEdgeX, midY), new CvPoint(_jigRightEdgeX, midY), edgeCol, 1, LineTypes.AntiAlias);

                string edgeText = IsJigOk ? $"EDGE OK ({LastJigDistanceMm:F1}mm)" : $"EDGE ERROR ({LastJigDistanceMm:F1}mm)";
                Cv2.PutText(dispMat, edgeText, new CvPoint(_jigLeftEdgeX, JigLeftRoi.Y - 10), HersheyFonts.HersheySimplex, 0.8, edgeCol, 2);
            }
        }
    }
}