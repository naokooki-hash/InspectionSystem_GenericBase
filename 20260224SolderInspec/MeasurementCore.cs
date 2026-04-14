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
        public int HoleThreshold { get; set; } = 100;
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

        private RotatedRect _btmRect;
        private CvPoint _topCenter, _btmCenter;
        private List<CvPoint> _detectedHoles = new List<CvPoint>();

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
                    using (Mat bin = new Mat())
                    {
                        Cv2.GaussianBlur(debugMat, blurred, new CvSize(5, 5), 0);
                        Cv2.Threshold(blurred, bin, HoleThreshold, 255, ThresholdTypes.BinaryInv);
                        lock (_imageLock) { bin.CopyTo(_lastBinaryHole); }
                    }
                }
            }
        }

        private double _lastXOffsetMm, _lastAngle;
        private int _lastResult = 0;
        private bool _isOffsetOk = false, _isAngleOk = false, _hasValidData = false;

        public double CalculateBrightness(Mat frame, CvRect roi)
        {
            if (frame == null || frame.Empty()) return 0;
            CvRect safeRoi = roi & new CvRect(0, 0, frame.Width, frame.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return 0;
            using (Mat roiMat = new Mat(frame, safeRoi)) { return Cv2.Mean(roiMat).Val0; }
        }

        // ★青矢印対応：重心を廃止し、元の「一番外側のピクセル(BoundingRectの端点)」を測るロジックに復元
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
                Cv2.Threshold(blurred, bin, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);

                Cv2.FindContours(bin, out CvPoint[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                if (contours.Length == 0) return false;

                var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();

                // 一番外側の四角形（BoundingRect）を取得
                var rect = Cv2.BoundingRect(largestContour);

                if (rect.Width < 5 || rect.Height < 5) return false;

                // 左側のROIなら塊の右端、右側のROIなら塊の左端を取得
                if (isLeft) edgeX = safeRoi.X + rect.Right;
                else edgeX = safeRoi.X + rect.Left;

                return true;
            }
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

                    // 1. エッジ距離チェック (元の直線ロジック)
                    _jigEdgeDetected = false;
                    IsJigOk = false;

                    // isLeft のフラグを渡して呼び出す
                    if (DetectJigEdge(gray, JigLeftRoi, true, out _jigLeftEdgeX) &&
                        DetectJigEdge(gray, JigRightRoi, false, out _jigRightEdgeX))
                    {
                        _jigEdgeDetected = true;

                        double distancePx = Math.Abs(_jigRightEdgeX - _jigLeftEdgeX);
                        LastJigDistanceMm = distancePx * PixelToMmRatio;

                        if (TargetJigDistanceMm <= 0) TargetJigDistanceMm = LastJigDistanceMm;

                        IsJigOk = (Math.Abs(LastJigDistanceMm - TargetJigDistanceMm) <= JigToleranceMm);
                    }

                    // 2. 穴検出
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

                                // ★赤矢印対応：重心(Moments)をやめ、楕円フィッティング(FitEllipse)で円の中心を求める
                                if (c.Length >= 5) // FitEllipseは計算に5点以上必要
                                {
                                    var ellipse = Cv2.FitEllipse(c);
                                    _detectedHoles.Add(new CvPoint((int)ellipse.Center.X + safeHolesRoi.X, (int)ellipse.Center.Y + safeHolesRoi.Y));
                                }
                                else
                                {
                                    // 万が一輪郭が小さすぎる場合の保険
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
                    _lastAngle = Math.Atan2(rightHole.Y - leftHole.Y, rightHole.X - leftHole.X) * 180.0 / Math.PI;
                    LastHoleDistancePx = Math.Sqrt(Math.Pow(rightHole.X - leftHole.X, 2) + Math.Pow(rightHole.Y - leftHole.Y, 2));

                    // 3. 下部検出
                    CvRect safeBtmRoi = BtmMeasureRoi & new CvRect(0, 0, gray.Width, gray.Height);
                    if (safeBtmRoi.Width > 0 && safeBtmRoi.Height > 0)
                    {
                        using (Mat btmRoiMat = new Mat(gray, safeBtmRoi))
                        using (Mat bin = new Mat())
                        {
                            Cv2.Threshold(btmRoiMat, bin, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.BinaryInv);
                            Cv2.FindContours(bin, out CvPoint[][] bContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                            if (bContours.Length == 0) return 3;
                            var mainC = bContours.OrderByDescending(c => Cv2.ContourArea(c)).First();
                            var m = Cv2.Moments(mainC);
                            _btmCenter = new CvPoint((int)(m.M10 / m.M00) + safeBtmRoi.X, (int)(m.M01 / m.M00) + safeBtmRoi.Y);
                            _btmRect = Cv2.MinAreaRect(mainC);
                            _btmRect.Center = new Point2f(_btmCenter.X, _btmCenter.Y);
                        }
                    }
                    else return 3;

                    _lastXOffsetMm = (_btmCenter.X - _topCenter.X) * PixelToMmRatio;
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
                Cv2.Line(dispMat, _topCenter, _btmCenter, Scalar.Magenta, 2, LineTypes.AntiAlias);
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