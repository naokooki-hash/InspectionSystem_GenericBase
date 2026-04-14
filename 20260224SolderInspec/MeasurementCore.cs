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
        public double TargetXOffsetMm { get; set; } = 0.0;
        public double OffsetToleranceMm { get; set; } = 1.0;
        public double TargetAngleDeg { get; set; } = 0.0;
        public double AngleToleranceDeg { get; set; } = 2.0;

        public CvRect BtmMeasureRoi { get; set; } = new CvRect(250, 350, 150, 80);
        public CvRect HolesRoi { get; set; } = new CvRect(250, 350, 600, 200);
        public int MinHoleArea { get; set; } = 300;
        public int MaxHoleArea { get; set; } = 3000;

        public int EdgeThreshold { get; set; } = 12;
        public int HoleThreshold { get; set; } = 51;
        public int SplitBoundaryY { get; set; } = 570;

        public double MinCircularity { get; set; } = 0.7;

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

        private CvPoint _topCenter, _btmCenter;
        private List<CvPoint> _detectedHoles = new List<CvPoint>();

        // ★修正: Point2f から CvPoint (int) に変更して型の衝突を回避
        private CvPoint _btmAxisPt1, _btmAxisPt2;
        private CvPoint _projectedBtm;

        private readonly object _imageLock = new object();
        private Mat _lastBinaryHole = new Mat();

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

        public void UpdateDebugImageRealtime(Mat frame, CvRect debugRoi)
        {
            using (Mat gray = new Mat())
            {
                if (frame.Channels() == 3) Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                else frame.CopyTo(gray);

                CvRect safeDebugRoi = debugRoi & new CvRect(0, 0, gray.Width, gray.Height);
                if (safeDebugRoi.Width > 0 && safeDebugRoi.Height > 0)
                {
                    using (Mat debugMat = new Mat(gray, safeDebugRoi))
                    using (Mat blurred = new Mat())
                    {
                        Cv2.GaussianBlur(debugMat, blurred, new CvSize(5, 5), 0);

                        using (Mat bin = new Mat(blurred.Size(), MatType.CV_8UC1, new Scalar(0)))
                        {
                            int splitY = SplitBoundaryY - safeDebugRoi.Y;
                            if (splitY < 0) splitY = 0;
                            if (splitY > blurred.Height) splitY = blurred.Height;

                            if (splitY > 0)
                            {
                                CvRect topRoi = new CvRect(0, 0, blurred.Width, splitY);
                                using (Mat topMat = new Mat(blurred, topRoi))
                                using (Mat topBin = new Mat(bin, topRoi))
                                {
                                    Cv2.Threshold(topMat, topBin, EdgeThreshold, 255, ThresholdTypes.BinaryInv);
                                }
                            }

                            if (splitY < blurred.Height)
                            {
                                CvRect btmRoi = new CvRect(0, splitY, blurred.Width, blurred.Height - splitY);
                                using (Mat btmMat = new Mat(blurred, btmRoi))
                                using (Mat btmBin = new Mat(bin, btmRoi))
                                {
                                    Cv2.Threshold(btmMat, btmBin, HoleThreshold, 255, ThresholdTypes.BinaryInv);
                                }
                            }

                            if (splitY > 0 && splitY < blurred.Height)
                            {
                                Cv2.Line(bin, new CvPoint(0, splitY), new CvPoint(bin.Width, splitY), Scalar.Gray, 2);
                            }

                            lock (_imageLock) { bin.CopyTo(_lastBinaryHole); }
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

        private bool DetectJigEdge(Mat gray, CvRect roi, bool isLeft, out int edgeX)
        {
            edgeX = 0;
            CvRect safeRoi = roi & new CvRect(0, 0, gray.Width, gray.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return false;

            using (Mat roiMat = new Mat(gray, safeRoi))
            using (Mat blurred = new Mat())
            using (Mat bin = new Mat())
            {
                Cv2.GaussianBlur(roiMat, blurred, new CvSize(3, 3), 0);
                Cv2.Threshold(blurred, bin, EdgeThreshold, 255, ThresholdTypes.Binary);

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

        private double _lastXOffsetMm, _lastAngle;
        private int _lastResult = 0;
        private bool _isOffsetOk = false, _isAngleOk = false, _hasValidData = false;

        public int Inspect(Mat frame, CvRect debugRoi, bool isDebugMode)
        {
            _hasValidData = false;
            try
            {
                using (Mat gray = new Mat())
                {
                    if (frame.Channels() == 3) Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                    else frame.CopyTo(gray);

                    // 1. 上部エッジ距離チェック
                    _jigEdgeDetected = false;
                    IsJigOk = false;

                    if (DetectJigEdge(gray, JigLeftRoi, true, out _jigLeftEdgeX) &&
                        DetectJigEdge(gray, JigRightRoi, false, out _jigRightEdgeX))
                    {
                        _jigEdgeDetected = true;
                        double distancePx = Math.Abs(_jigRightEdgeX - _jigLeftEdgeX);
                        LastJigDistanceMm = distancePx * PixelToMmRatio;
                        if (TargetJigDistanceMm <= 0) TargetJigDistanceMm = LastJigDistanceMm;
                        IsJigOk = (Math.Abs(LastJigDistanceMm - TargetJigDistanceMm) <= JigToleranceMm);
                    }

                    // 2. 穴検出 (基準点3の導出)
                    _detectedHoles.Clear();
                    CvRect safeHolesRoi = HolesRoi & new CvRect(0, 0, gray.Width, gray.Height);
                    if (safeHolesRoi.Width > 0 && safeHolesRoi.Height > 0)
                    {
                        using (Mat holeRoiMat = new Mat(gray, safeHolesRoi))
                        using (Mat blurred = new Mat())
                        using (Mat bin = new Mat())
                        {
                            Cv2.GaussianBlur(holeRoiMat, blurred, new CvSize(5, 5), 0);
                            Cv2.Threshold(blurred, bin, HoleThreshold, 255, ThresholdTypes.BinaryInv);

                            Cv2.FindContours(bin, out CvPoint[][] hContours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
                            foreach (var c in hContours)
                            {
                                double area = Cv2.ContourArea(c);
                                if (area < MinHoleArea || area > MaxHoleArea) continue;
                                double perimeter = Cv2.ArcLength(c, true);
                                if (perimeter == 0) continue;
                                if ((4 * Math.PI * area) / (perimeter * perimeter) < MinCircularity) continue;

                                if (c.Length >= 5)
                                {
                                    var ellipse = Cv2.FitEllipse(c);
                                    _detectedHoles.Add(new CvPoint((int)ellipse.Center.X + safeHolesRoi.X, (int)ellipse.Center.Y + safeHolesRoi.Y));
                                }
                                else
                                {
                                    var m = Cv2.Moments(c);
                                    if (m.M00 > 0) _detectedHoles.Add(new CvPoint((int)(m.M10 / m.M00) + safeHolesRoi.X, (int)(m.M01 / m.M00) + safeHolesRoi.Y));
                                }
                            }
                        }
                    }

                    if (!_jigEdgeDetected || !IsJigOk) { _hasValidData = true; return 2; }
                    if (_detectedHoles.Count < 2) return 3;

                    var sorted = _detectedHoles.OrderBy(p => p.X).ToList();
                    var leftHole = sorted.First(); var rightHole = sorted.Last();

                    _topCenter = new CvPoint((leftHole.X + rightHole.X) / 2, (leftHole.Y + rightHole.Y) / 2);

                    // 上部(穴)の水平に対する角度
                    double top_tilt_rad = Math.Atan2(rightHole.Y - leftHole.Y, rightHole.X - leftHole.X);
                    LastHoleDistancePx = Math.Sqrt(Math.Pow(rightHole.X - leftHole.X, 2) + Math.Pow(rightHole.Y - leftHole.Y, 2));

                    // 3. 下部検出 (図解の「1と2の中点の軸」を導出)
                    CvRect safeBtmRoi = BtmMeasureRoi & new CvRect(0, 0, gray.Width, gray.Height);
                    if (safeBtmRoi.Width > 0 && safeBtmRoi.Height > 0)
                    {
                        using (Mat btmRoiMat = new Mat(gray, safeBtmRoi))
                        using (Mat bin = new Mat())
                        {
                            Cv2.Threshold(btmRoiMat, bin, HoleThreshold, 255, ThresholdTypes.Binary);

                            List<Point2f> centerPoints = new List<Point2f>();

                            for (int y = 0; y < bin.Rows; y++)
                            {
                                int leftX = -1;
                                for (int x = 0; x < bin.Cols; x++)
                                {
                                    if (bin.At<byte>(y, x) == 255) { leftX = x; break; }
                                }
                                int rightX = -1;
                                for (int x = bin.Cols - 1; x >= 0; x--)
                                {
                                    if (bin.At<byte>(y, x) == 255) { rightX = x; break; }
                                }

                                if (leftX != -1 && rightX != -1 && rightX > leftX)
                                {
                                    float centerX = (leftX + rightX) / 2.0f;
                                    centerPoints.Add(new Point2f(centerX + safeBtmRoi.X, y + safeBtmRoi.Y));
                                }
                            }

                            if (centerPoints.Count < 5) return 3;

                            Line2D btmLine = Cv2.FitLine(centerPoints, DistanceTypes.L2, 0, 0.01, 0.01);

                            // ★修正: float ではなく double で受ける
                            double vx = btmLine.Vx;
                            double vy = btmLine.Vy;
                            double x0 = btmLine.X1;
                            double y0 = btmLine.Y1;

                            // 描画用の軸線ベクトル (下向きに統一し、線を長く引く)
                            if (vy < 0) { vx = -vx; vy = -vy; }

                            // ★修正: 計算を完全に double で行い、最後に int キャストする
                            _btmAxisPt1 = new CvPoint((int)(x0 - 500.0 * vx), (int)(y0 - 500.0 * vy));
                            _btmAxisPt2 = new CvPoint((int)(x0 + 500.0 * vx), (int)(y0 + 500.0 * vy));

                            // ROIのY方向中心における、下部部品の中心X座標
                            double targetY = safeBtmRoi.Y + safeBtmRoi.Height / 2.0;
                            double t = (targetY - y0) / vy;
                            double btmCenterX = x0 + t * vx;

                            _btmCenter = new CvPoint((int)btmCenterX, (int)targetY);

                            double btm_tilt_rad = Math.Atan2(vx, vy);
                            _lastAngle = (top_tilt_rad - btm_tilt_rad) * 180.0 / Math.PI;
                        }
                    }
                    else return 3;

                    // 4. 垂線投影とズレ計算
                    double dy = _btmCenter.Y - _topCenter.Y;
                    double expected_btm_X = _topCenter.X - dy * Math.Tan(top_tilt_rad);
                    _projectedBtm = new CvPoint((int)expected_btm_X, _btmCenter.Y);

                    _lastXOffsetMm = (_btmCenter.X - expected_btm_X) * PixelToMmRatio;

                    _isOffsetOk = Math.Abs(_lastXOffsetMm - TargetXOffsetMm) <= OffsetToleranceMm;
                    _isAngleOk = Math.Abs(_lastAngle - TargetAngleDeg) <= AngleToleranceDeg;
                    _lastResult = (_isOffsetOk && _isAngleOk) ? 1 : 2;
                    _hasValidData = true;
                    return _lastResult;
                }
            }
            catch { return 3; }
        }

        public void DrawOverlay(Mat dispMat)
        {
            if (!_hasValidData) return;

            if (_detectedHoles.Count >= 2)
            {
                var sorted = _detectedHoles.OrderBy(p => p.X).ToList();
                foreach (var h in _detectedHoles) Cv2.Circle(dispMat, h, 8, Scalar.Orange, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, sorted.First(), sorted.Last(), Scalar.Yellow, 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(dispMat, _topCenter, Scalar.Red, MarkerTypes.Cross, 20, 2);
            }

            if (IsJigOk && _detectedHoles.Count >= 2)
            {
                // ★修正: Point2f ではなく CvPoint をそのまま渡す
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

            if (_jigEdgeDetected)
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