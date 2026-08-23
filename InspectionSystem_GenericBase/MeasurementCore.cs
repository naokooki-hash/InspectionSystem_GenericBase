using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace InspectionSystem_GenericBase
{
    public class MeasurementCore : IInspectionEngine
    {
        public string EngineName => "Punching Metal Inspection Engine";

        // 外部からトリガーされる際の領域指定およびデバッグフラグ
        public CvRect DebugRoi { get; set; } = new CvRect(100, 50, 440, 380);
        public bool IsDebugMode { get; set; } = false;
        // 検査モードの有効/無効
        public bool EnableJigCheck { get; set; } = true;
        public bool EnableOuterTiltCheck { get; set; } = true;
        public bool EnableHoleCheck { get; set; } = true;

        // 【モードA】外形エッジ専用パラメータ
        public int ThreshOuterL { get; set; } = 100;
        public int ThreshOuterR { get; set; } = 100;
        public CvRect TiltLeftRoi { get; set; } = new CvRect(100, 250, 80, 200);
        public CvRect TiltRightRoi { get; set; } = new CvRect(900, 250, 80, 200);
        public double TargetOuterXOffsetMm { get; set; } = 0.0;
        public double OuterOffsetToleranceMm { get; set; } = 1.0;
        public double TargetOuterAngleDeg { get; set; } = 0.0;
        public double OuterAngleToleranceDeg { get; set; } = 2.0;

        // 【モードB】穴・下部線専用パラメータ
        public int SplitBoundaryX { get; set; } = 320;
        public int SplitBoundaryY { get; set; } = 570;
        public int ThreshTopLeft { get; set; } = 12;
        public int ThreshTopRight { get; set; } = 12;
        public int ThreshBtmLeft { get; set; } = 51;
        public int ThreshBtmRight { get; set; } = 51;

        public CvRect BtmMeasureRoi { get; set; } = new CvRect(250, 350, 150, 80);

        // ★追加：BTM内側（赤枠）の専用閾値
        public int ThreshBtmInnerL { get; set; } = 51;
        public int ThreshBtmInnerR { get; set; } = 51;
        public CvRect BtmInnerLeftRoi { get; set; } = new CvRect(350, 420, 80, 120);
        public CvRect BtmInnerRightRoi { get; set; } = new CvRect(550, 420, 80, 120);

        public CvRect HolesRoi { get; set; } = new CvRect(250, 350, 600, 200);
        public int MinHoleArea { get; set; } = 300;
        public int MaxHoleArea { get; set; } = 3000;
        public double MinCircularity { get; set; } = 0.4;

        public double TargetXOffsetMm { get; set; } = 0.0;
        public double OffsetToleranceMm { get; set; } = 1.0;
        public double TargetAngleDeg { get; set; } = 0.0;
        public double AngleToleranceDeg { get; set; } = 2.0;

        // 共通・エッジ間距離パラメータ
        public double PixelToMmRatio { get; set; } = 0.05;
        public double LastHoleDistancePx { get; private set; } = 0.0;
        public CvRect JigLeftRoi { get; set; } = new CvRect(280, 315, 200, 200);
        public CvRect JigRightRoi { get; set; } = new CvRect(820, 315, 200, 200);
        public double TargetJigDistanceMm { get; set; } = 40.0;
        public double JigToleranceMm { get; set; } = 1.5;

        // 内部状態
        public bool IsJigOk { get; private set; } = false;
        public double LastJigDistanceMm { get; private set; } = 0.0;

        // ★追加：外部から実測角度を読み取るためのプロパティ
        public double LastOuterAngleDeg { get; private set; } = 0.0;
        public double LastHoleAngleDeg { get; private set; } = 0.0;

        private int _jigLeftEdgeX = 0, _jigRightEdgeX = 0;
        private bool _jigEdgeDetected = false;

        private CvPoint _holeTopCenter = new CvPoint(0, 0);
        private List<CvPoint> _detectedHoles = new List<CvPoint>();

        private CvPoint _outerTopCenter = new CvPoint(0, 0);
        private CvPoint _tiltLeftPt1, _tiltLeftPt2, _tiltRightPt1, _tiltRightPt2;
        private bool _tiltEdgesFound = false;

        private CvPoint _btmCenter = new CvPoint(0, 0);
        private CvPoint _btmAxisPt1, _btmAxisPt2;

        private bool _useInnerBtm = false;
        private bool _btmInnerEdgesFound = false;
        private CvPoint _btmInnerLeftPt1, _btmInnerLeftPt2, _btmInnerRightPt1, _btmInnerRightPt2;

        private CvPoint _projectedBtmHole, _projectedBtmOuter;

        private readonly object _imageLock = new object();
        private Mat _lastBinaryHole = new Mat();

        private double _lastHoleOffsetMm;
        private double _lastOuterOffsetMm;
        private bool _isHoleOffsetOk = false, _isHoleAngleOk = false;
        private bool _isOuterOffsetOk = false, _isOuterAngleOk = false;
        private bool _hasValidData = false;
        private int _lastResult = 0;

        public void GetDebugImage(Mat dst)
        {
            lock (_imageLock) { if (_lastBinaryHole != null && !_lastBinaryHole.Empty()) _lastBinaryHole.CopyTo(dst); }
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

                        // 上部外形エッジ(青枠)のデバッグ描画
                        CvRect safeLeft = TiltLeftRoi & new CvRect(0, 0, binFull.Width, binFull.Height);
                        if (safeLeft.Width > 0 && safeLeft.Height > 0)
                        {
                            using (Mat subGray = new Mat(blurred, safeLeft))
                            using (Mat subBin = new Mat(binFull, safeLeft))
                                Cv2.Threshold(subGray, subBin, ThreshOuterL, 255, ThresholdTypes.Binary);
                            Cv2.Rectangle(binFull, safeLeft, Scalar.Gray, 1);
                        }

                        CvRect safeRight = TiltRightRoi & new CvRect(0, 0, binFull.Width, binFull.Height);
                        if (safeRight.Width > 0 && safeRight.Height > 0)
                        {
                            using (Mat subGray = new Mat(blurred, safeRight))
                            using (Mat subBin = new Mat(binFull, safeRight))
                                Cv2.Threshold(subGray, subBin, ThreshOuterR, 255, ThresholdTypes.Binary);
                            Cv2.Rectangle(binFull, safeRight, Scalar.Gray, 1);
                        }

                        // ★追加：下部内側エッジ(赤枠)のデバッグ描画
                        CvRect safeInnerL = BtmInnerLeftRoi & new CvRect(0, 0, binFull.Width, binFull.Height);
                        if (safeInnerL.Width > 0 && safeInnerL.Height > 0)
                        {
                            using (Mat subGray = new Mat(blurred, safeInnerL))
                            using (Mat subBin = new Mat(binFull, safeInnerL))
                                Cv2.Threshold(subGray, subBin, ThreshBtmInnerL, 255, ThresholdTypes.Binary);
                            Cv2.Rectangle(binFull, safeInnerL, Scalar.Gray, 1);
                        }

                        CvRect safeInnerR = BtmInnerRightRoi & new CvRect(0, 0, binFull.Width, binFull.Height);
                        if (safeInnerR.Width > 0 && safeInnerR.Height > 0)
                        {
                            using (Mat subGray = new Mat(blurred, safeInnerR))
                            using (Mat subBin = new Mat(binFull, safeInnerR))
                                Cv2.Threshold(subGray, subBin, ThreshBtmInnerR, 255, ThresholdTypes.Binary);
                            Cv2.Rectangle(binFull, safeInnerR, Scalar.Gray, 1);
                        }

                        CvRect safeDebugRoi = debugRoi & new CvRect(0, 0, binFull.Width, binFull.Height);
                        if (safeDebugRoi.Width > 0 && safeDebugRoi.Height > 0)
                        {
                            lock (_imageLock)
                            {
                                using (Mat cropped = new Mat(binFull, safeDebugRoi)) { cropped.CopyTo(_lastBinaryHole); }
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
                if (isLeft) edgeX = safeRoi.X + rect.Right; else edgeX = safeRoi.X + rect.Left;
                return true;
            }
        }

        private bool FindVerticalEdgeLine(Mat gray, CvRect roi, bool isLeftEdge, int thresh, out double x0, out double y0, out double vx, out double vy)
        {
            x0 = y0 = vx = vy = 0;
            CvRect safeRoi = roi & new CvRect(0, 0, gray.Width, gray.Height);
            if (safeRoi.Width <= 0 || safeRoi.Height <= 0) return false;

            using (Mat roiGray = new Mat(gray, safeRoi))
            using (Mat blurred = new Mat())
            using (Mat bin = new Mat())
            {
                Cv2.GaussianBlur(roiGray, blurred, new CvSize(5, 5), 0);
                Cv2.Threshold(blurred, bin, thresh, 255, ThresholdTypes.Binary);

                List<Point2f> edgePoints = new List<Point2f>();
                for (int y = 0; y < bin.Rows; y++)
                {
                    if (isLeftEdge)
                    {
                        for (int x = 0; x < bin.Cols; x++)
                            if (bin.At<byte>(y, x) == 255) { edgePoints.Add(new Point2f(x + safeRoi.X, y + safeRoi.Y)); break; }
                    }
                    else
                    {
                        for (int x = bin.Cols - 1; x >= 0; x--)
                            if (bin.At<byte>(y, x) == 255) { edgePoints.Add(new Point2f(x + safeRoi.X, y + safeRoi.Y)); break; }
                    }
                }

                if (edgePoints.Count >= 20)
                {
                    Line2D line = Cv2.FitLine(edgePoints, DistanceTypes.L2, 0, 0.01, 0.01);
                    vx = line.Vx; vy = line.Vy; x0 = line.X1; y0 = line.Y1;
                    if (vy < 0) { vx = -vx; vy = -vy; }
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
                            // 1. Jigエッジ
                            _jigEdgeDetected = false; IsJigOk = false;
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

                            // 2A. 【モードA】外形エッジ検出
                            double outer_tilt_rad = 0; _tiltEdgesFound = false;
                            if (EnableOuterTiltCheck)
                            {
                                bool lFound = FindVerticalEdgeLine(gray, TiltLeftRoi, true, ThreshOuterL, out double lx0, out double ly0, out double lvx, out double lvy);
                                bool rFound = FindVerticalEdgeLine(gray, TiltRightRoi, false, ThreshOuterR, out double rx0, out double ry0, out double rvx, out double rvy);

                                if (lFound && rFound)
                                {
                                    _tiltEdgesFound = true;
                                    double lAngle = Math.Atan2(lvx, lvy);
                                    double rAngle = Math.Atan2(rvx, rvy);
                                    outer_tilt_rad = (lAngle + rAngle) / 2.0;

                                    _tiltLeftPt1 = new CvPoint((int)(lx0 - 500 * lvx), (int)(ly0 - 500 * lvy));
                                    _tiltLeftPt2 = new CvPoint((int)(lx0 + 500 * lvx), (int)(ly0 + 500 * lvy));
                                    _tiltRightPt1 = new CvPoint((int)(rx0 - 500 * rvx), (int)(ry0 - 500 * rvy));
                                    _tiltRightPt2 = new CvPoint((int)(rx0 + 500 * rvx), (int)(ry0 + 500 * rvy));

                                    double refY = (TiltLeftRoi.Y + TiltLeftRoi.Height / 2.0 + TiltRightRoi.Y + TiltRightRoi.Height / 2.0) / 2.0;
                                    double lX = lx0 + ((refY - ly0) / lvy) * lvx;
                                    double rX = rx0 + ((refY - ry0) / rvy) * rvx;
                                    _outerTopCenter = new CvPoint((int)((lX + rX) / 2.0), (int)refY);

                                    if (!EnableHoleCheck) LastHoleDistancePx = Math.Abs(rX - lX);
                                }
                            }

                            // 2B. 【モードB】穴検出
                            double hole_tilt_rad = 0; _detectedHoles.Clear();
                            if (EnableHoleCheck)
                            {
                                CvRect safeHolesRoi = HolesRoi & new CvRect(0, 0, binInv.Width, binInv.Height);
                                if (safeHolesRoi.Width > 0 && safeHolesRoi.Height > 0)
                                {
                                    using (Mat holeBin = new Mat(binInv, safeHolesRoi))
                                    using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new CvSize(5, 5)))
                                    {
                                        Cv2.MorphologyEx(holeBin, holeBin, MorphTypes.Close, kernel);
                                        Cv2.FindContours(holeBin, out CvPoint[][] hContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                                        foreach (var c in hContours)
                                        {
                                            double area = Cv2.ContourArea(c);
                                            if (area < MinHoleArea || area > MaxHoleArea) continue;
                                            double perimeter = Cv2.ArcLength(c, true);
                                            if (perimeter > 0 && ((4 * Math.PI * area) / (perimeter * perimeter)) < MinCircularity) continue;

                                            var m = Cv2.Moments(c);
                                            if (m.M00 > 0) _detectedHoles.Add(new CvPoint((int)(m.M10 / m.M00) + safeHolesRoi.X, (int)(m.M01 / m.M00) + safeHolesRoi.Y));
                                        }
                                    }
                                }

                                if (_detectedHoles.Count >= 2)
                                {
                                    var sorted = _detectedHoles.OrderBy(p => p.X).ToList();
                                    var leftHole = sorted.First(); var rightHole = sorted.Last();

                                    _holeTopCenter = new CvPoint((leftHole.X + rightHole.X) / 2, (leftHole.Y + rightHole.Y) / 2);
                                    hole_tilt_rad = Math.Atan2(rightHole.Y - leftHole.Y, rightHole.X - leftHole.X);
                                    LastHoleDistancePx = Math.Sqrt(Math.Pow(rightHole.X - leftHole.X, 2) + Math.Pow(rightHole.Y - leftHole.Y, 2));
                                }
                            }

                            // 3. 下部(BTM)基準線の検出
                            bool isBtmOuterDetected = false; double btm_tilt_rad_outer = 0;
                            CvPoint btmOuterCenter = new CvPoint(0, 0);

                            CvRect safeBtmRoi = BtmMeasureRoi & new CvRect(0, 0, binNormal.Width, binNormal.Height);
                            if (safeBtmRoi.Width > 0 && safeBtmRoi.Height > 0)
                            {
                                using (Mat btmBin = new Mat(binNormal, safeBtmRoi))
                                {
                                    List<Point2f> centerPoints = new List<Point2f>();
                                    for (int y = 0; y < btmBin.Rows; y++)
                                    {
                                        int leftX = -1; for (int x = 0; x < btmBin.Cols; x++) if (btmBin.At<byte>(y, x) == 255) { leftX = x; break; }
                                        int rightX = -1; for (int x = btmBin.Cols - 1; x >= 0; x--) if (btmBin.At<byte>(y, x) == 255) { rightX = x; break; }
                                        if (leftX != -1 && rightX != -1 && rightX > leftX) centerPoints.Add(new Point2f((leftX + rightX) / 2.0f + safeBtmRoi.X, y + safeBtmRoi.Y));
                                    }

                                    if (centerPoints.Count >= 5)
                                    {
                                        Line2D btmLine = Cv2.FitLine(centerPoints, DistanceTypes.L2, 0, 0.01, 0.01);
                                        double vx = btmLine.Vx, vy = btmLine.Vy, x0 = btmLine.X1, y0 = btmLine.Y1;
                                        if (vy < 0) { vx = -vx; vy = -vy; }

                                        double targetY = safeBtmRoi.Y + safeBtmRoi.Height / 2.0;
                                        double btmCenterX = x0 + ((targetY - y0) / vy) * vx;

                                        btmOuterCenter = new CvPoint((int)btmCenterX, (int)targetY);
                                        btm_tilt_rad_outer = Math.Atan2(vx, vy);
                                        isBtmOuterDetected = true;
                                    }
                                }
                            }

                            bool isBtmInnerDetected = false; double btm_tilt_rad_inner = 0;
                            CvPoint btmInnerCenter = new CvPoint(0, 0);
                            _btmInnerEdgesFound = false;

                            bool lInnerFound = FindVerticalEdgeLine(gray, BtmInnerLeftRoi, false, ThreshBtmInnerL, out double ilx0, out double ily0, out double ilvx, out double ilvy);
                            bool rInnerFound = FindVerticalEdgeLine(gray, BtmInnerRightRoi, true, ThreshBtmInnerR, out double irx0, out double iry0, out double irvx, out double irvy);

                            if (lInnerFound && rInnerFound)
                            {
                                _btmInnerEdgesFound = true;
                                double lAngle = Math.Atan2(ilvx, ilvy);
                                double rAngle = Math.Atan2(irvx, irvy);
                                btm_tilt_rad_inner = (lAngle + rAngle) / 2.0;

                                _btmInnerLeftPt1 = new CvPoint((int)(ilx0 - 500 * ilvx), (int)(ily0 - 500 * ilvy));
                                _btmInnerLeftPt2 = new CvPoint((int)(ilx0 + 500 * ilvx), (int)(ily0 + 500 * ilvy));
                                _btmInnerRightPt1 = new CvPoint((int)(irx0 - 500 * irvx), (int)(iry0 - 500 * irvy));
                                _btmInnerRightPt2 = new CvPoint((int)(irx0 + 500 * irvx), (int)(iry0 + 500 * irvy));

                                double refY = (BtmInnerLeftRoi.Y + BtmInnerLeftRoi.Height / 2.0 + BtmInnerRightRoi.Y + BtmInnerRightRoi.Height / 2.0) / 2.0;
                                double lX = ilx0 + ((refY - ily0) / ilvy) * ilvx;
                                double rX = irx0 + ((refY - iry0) / irvy) * irvx;
                                btmInnerCenter = new CvPoint((int)((lX + rX) / 2.0), (int)refY);
                                isBtmInnerDetected = true;
                            }

                            _useInnerBtm = false;
                            bool isBtmDetected = false;
                            double btm_tilt_rad = 0;

                            if (isBtmOuterDetected && isBtmInnerDetected)
                            {
                                if (Math.Abs(btm_tilt_rad_inner) < Math.Abs(btm_tilt_rad_outer)) _useInnerBtm = true;
                            }
                            else if (isBtmInnerDetected) { _useInnerBtm = true; }

                            if (isBtmOuterDetected || isBtmInnerDetected)
                            {
                                isBtmDetected = true;
                                if (_useInnerBtm) { _btmCenter = btmInnerCenter; btm_tilt_rad = btm_tilt_rad_inner; }
                                else { _btmCenter = btmOuterCenter; btm_tilt_rad = btm_tilt_rad_outer; }

                                double final_vx = Math.Sin(btm_tilt_rad);
                                double final_vy = Math.Cos(btm_tilt_rad);
                                _btmAxisPt1 = new CvPoint((int)(_btmCenter.X - 500 * final_vx), (int)(_btmCenter.Y - 500 * final_vy));
                                _btmAxisPt2 = new CvPoint((int)(_btmCenter.X + 500 * final_vx), (int)(_btmCenter.Y + 500 * final_vy));
                            }

                            _hasValidData = true;

                            // 4. 並列計算と総合判定
                            bool isHoleOk = false, isOuterOk = false, isJigOkFlag = true;

                            if (EnableJigCheck) { if (!_jigEdgeDetected || !IsJigOk) isJigOkFlag = false; }

                            if (EnableOuterTiltCheck && _tiltEdgesFound && isBtmDetected)
                            {
                                LastOuterAngleDeg = (outer_tilt_rad - btm_tilt_rad) * 180.0 / Math.PI;
                                double expected_btm_X = _outerTopCenter.X - (_btmCenter.Y - _outerTopCenter.Y) * Math.Tan(outer_tilt_rad);
                                _projectedBtmOuter = new CvPoint((int)expected_btm_X, _btmCenter.Y);
                                _lastOuterOffsetMm = (_btmCenter.X - expected_btm_X) * PixelToMmRatio;

                                _isOuterOffsetOk = Math.Abs(_lastOuterOffsetMm - TargetOuterXOffsetMm) <= OuterOffsetToleranceMm;
                                _isOuterAngleOk = Math.Abs(LastOuterAngleDeg - TargetOuterAngleDeg) <= OuterAngleToleranceDeg;
                                if (_isOuterOffsetOk && _isOuterAngleOk) isOuterOk = true;
                            }

                            if (EnableHoleCheck && _detectedHoles.Count >= 2 && isBtmDetected)
                            {
                                LastHoleAngleDeg = (hole_tilt_rad - btm_tilt_rad) * 180.0 / Math.PI;
                                double expected_btm_X = _holeTopCenter.X - (_btmCenter.Y - _holeTopCenter.Y) * Math.Tan(hole_tilt_rad);
                                _projectedBtmHole = new CvPoint((int)expected_btm_X, _btmCenter.Y);
                                _lastHoleOffsetMm = (_btmCenter.X - expected_btm_X) * PixelToMmRatio;

                                _isHoleOffsetOk = Math.Abs(_lastHoleOffsetMm - TargetXOffsetMm) <= OffsetToleranceMm;
                                _isHoleAngleOk = Math.Abs(LastHoleAngleDeg - TargetAngleDeg) <= AngleToleranceDeg;
                                if (_isHoleOffsetOk && _isHoleAngleOk) isHoleOk = true;
                            }

                            if (!isBtmDetected) return 3;
                            if (EnableOuterTiltCheck && !_tiltEdgesFound && !EnableHoleCheck) return 3;
                            if (EnableHoleCheck && _detectedHoles.Count < 2 && !EnableOuterTiltCheck) return 3;
                            if (EnableOuterTiltCheck && EnableHoleCheck && !_tiltEdgesFound && _detectedHoles.Count < 2) return 3;

                            if (!isJigOkFlag) return 2;

                            bool finalOk = false;
                            if (EnableOuterTiltCheck && EnableHoleCheck) { finalOk = isOuterOk || isHoleOk; }
                            else if (EnableOuterTiltCheck) { finalOk = isOuterOk; }
                            else if (EnableHoleCheck) { finalOk = isHoleOk; }
                            else { finalOk = true; }

                            _lastResult = finalOk ? 1 : 2;
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

            if (_btmInnerEdgesFound)
            {
                Cv2.Line(dispMat, _btmInnerLeftPt1, _btmInnerLeftPt2, Scalar.Red, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, _btmInnerRightPt1, _btmInnerRightPt2, Scalar.Red, 2, LineTypes.AntiAlias);
            }

            if (_btmCenter.X != 0)
            {
                Scalar axisCol = _useInnerBtm ? Scalar.Pink : new Scalar(0, 255, 255);
                Cv2.Line(dispMat, _btmAxisPt1, _btmAxisPt2, axisCol, 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(dispMat, _btmCenter, Scalar.Blue, MarkerTypes.Cross, 20, 2);

                string usedStr = _useInnerBtm ? "BTM: Inner (Red)" : "BTM: Outer (Yellow)";
                Cv2.PutText(dispMat, usedStr, new CvPoint(_btmCenter.X + 20, _btmCenter.Y + 20), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
                Cv2.PutText(dispMat, usedStr, new CvPoint(_btmCenter.X + 20, _btmCenter.Y + 20), HersheyFonts.HersheySimplex, 0.7, axisCol, 1);
            }

            if (EnableOuterTiltCheck && _tiltEdgesFound && _btmCenter.X != 0)
            {
                Cv2.Line(dispMat, _tiltLeftPt1, _tiltLeftPt2, Scalar.Cyan, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, _tiltRightPt1, _tiltRightPt2, Scalar.Cyan, 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(dispMat, _outerTopCenter, Scalar.Red, MarkerTypes.Cross, 20, 2);
                Cv2.Line(dispMat, _outerTopCenter, _projectedBtmOuter, Scalar.Cyan, 1, LineTypes.AntiAlias);

                int tx = Math.Max(_outerTopCenter.X, _btmCenter.X) + 40, ty = (_outerTopCenter.Y + _btmCenter.Y) / 2 - 30;
                string offStr = "[A]Outer Off: " + _lastOuterOffsetMm.ToString("+0.00;-0.00") + "mm";
                string angStr = "[A]Outer Ang: " + LastOuterAngleDeg.ToString("+0.00;-0.00") + "deg";
                Cv2.PutText(dispMat, offStr, new CvPoint(tx, ty - 15), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
                Cv2.PutText(dispMat, offStr, new CvPoint(tx, ty - 15), HersheyFonts.HersheySimplex, 0.7, _isOuterOffsetOk ? Scalar.LimeGreen : Scalar.Red, 1);
                Cv2.PutText(dispMat, angStr, new CvPoint(tx, ty + 15), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
                Cv2.PutText(dispMat, angStr, new CvPoint(tx, ty + 15), HersheyFonts.HersheySimplex, 0.7, _isOuterAngleOk ? Scalar.LimeGreen : Scalar.Red, 1);
            }

            if (EnableHoleCheck && _detectedHoles.Count >= 2 && _btmCenter.X != 0)
            {
                var sorted = _detectedHoles.OrderBy(p => p.X).ToList();
                foreach (var h in _detectedHoles) Cv2.Circle(dispMat, h, 8, Scalar.Orange, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, sorted.First(), sorted.Last(), Scalar.Yellow, 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(dispMat, _holeTopCenter, Scalar.Red, MarkerTypes.Cross, 20, 2);
                Cv2.Line(dispMat, _holeTopCenter, _projectedBtmHole, Scalar.Magenta, 1, LineTypes.AntiAlias);

                int tx = Math.Max(_holeTopCenter.X, _btmCenter.X) + 40, ty = (_holeTopCenter.Y + _btmCenter.Y) / 2 + 40;
                string offStr = "[B]Hole Off: " + _lastHoleOffsetMm.ToString("+0.00;-0.00") + "mm";
                string angStr = "[B]Hole Ang: " + LastHoleAngleDeg.ToString("+0.00;-0.00") + "deg";
                Cv2.PutText(dispMat, offStr, new CvPoint(tx, ty - 15), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
                Cv2.PutText(dispMat, offStr, new CvPoint(tx, ty - 15), HersheyFonts.HersheySimplex, 0.7, _isHoleOffsetOk ? Scalar.LimeGreen : Scalar.Red, 1);
                Cv2.PutText(dispMat, angStr, new CvPoint(tx, ty + 15), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 3);
                Cv2.PutText(dispMat, angStr, new CvPoint(tx, ty + 15), HersheyFonts.HersheySimplex, 0.7, _isHoleAngleOk ? Scalar.LimeGreen : Scalar.Red, 1);
            }

            if (EnableJigCheck && _jigEdgeDetected)
            {
                Scalar edgeCol = IsJigOk ? Scalar.LimeGreen : Scalar.Red;
                int midY = (JigLeftRoi.Y + JigLeftRoi.Height / 2 + JigRightRoi.Y + JigRightRoi.Height / 2) / 2;
                Cv2.Line(dispMat, new CvPoint(_jigLeftEdgeX, JigLeftRoi.Y), new CvPoint(_jigLeftEdgeX, JigLeftRoi.Y + JigLeftRoi.Height), edgeCol, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, new CvPoint(_jigRightEdgeX, JigRightRoi.Y), new CvPoint(_jigRightEdgeX, JigRightRoi.Y + JigRightRoi.Height), edgeCol, 2, LineTypes.AntiAlias);
                Cv2.Line(dispMat, new CvPoint(_jigLeftEdgeX, midY), new CvPoint(_jigRightEdgeX, midY), edgeCol, 1, LineTypes.AntiAlias);
                Cv2.PutText(dispMat, IsJigOk ? $"EDGE OK ({LastJigDistanceMm:F1}mm)" : $"EDGE ERR ({LastJigDistanceMm:F1}mm)", new CvPoint(_jigLeftEdgeX, JigLeftRoi.Y - 10), HersheyFonts.HersheySimplex, 0.8, edgeCol, 2);
            }
        }

        public InspectionResult Inspect(Mat inputFrame)
        {
            int resultCode = Inspect(inputFrame, DebugRoi, IsDebugMode);

            var result = new InspectionResult
            {
                ProcessedTime = DateTime.Now,
                IsOk = (resultCode == 1)
            };

            if (resultCode == 1) result.ResultText = "OK";
            else if (resultCode == 2) result.ResultText = "NG";
            else result.ResultText = "ERR";

            var reasons = new List<string>();
            var measurements = new Dictionary<string, double>();

            if (EnableJigCheck)
            {
                measurements["JigDistanceMm"] = LastJigDistanceMm;
                if (!_jigEdgeDetected)
                {
                    reasons.Add("エッジ測定対象が検出できませんでした。");
                }
                else if (!IsJigOk)
                {
                    reasons.Add($"エッジ間距離が許容範囲外です (測定値: {LastJigDistanceMm:F2} mm, 目標: {TargetJigDistanceMm:F2} mm)");
                }
            }

            bool isBtmDetected = (_btmCenter.X != 0);
            if (!isBtmDetected)
            {
                reasons.Add("下部基準線が検出できませんでした。");
            }
            else
            {
                if (EnableOuterTiltCheck)
                {
                    measurements["OuterAngleDeg"] = LastOuterAngleDeg;
                    measurements["OuterOffsetMm"] = _lastOuterOffsetMm;

                    if (!_tiltEdgesFound)
                    {
                        reasons.Add("外形エッジが検出できませんでした。");
                    }
                    else
                    {
                        if (!_isOuterOffsetOk)
                        {
                            reasons.Add($"外形Xズレが許容値を超えています (測定値: {_lastOuterOffsetMm:F2} mm, 目標: {TargetOuterXOffsetMm:F2} mm)");
                        }
                        if (!_isOuterAngleOk)
                        {
                            reasons.Add($"外形傾き角度が許容値を超えています (測定値: {LastOuterAngleDeg:F2} deg, 目標: {TargetOuterAngleDeg:F2} deg)");
                        }
                    }
                }

                if (EnableHoleCheck)
                {
                    measurements["HoleAngleDeg"] = LastHoleAngleDeg;
                    measurements["HoleOffsetMm"] = _lastHoleOffsetMm;

                    if (_detectedHoles.Count < 2)
                    {
                        reasons.Add("基準穴が2個以上検出できませんでした。");
                    }
                    else
                    {
                        if (!_isHoleOffsetOk)
                        {
                            reasons.Add($"穴Xズレが許容値を超えています (測定値: {_lastHoleOffsetMm:F2} mm, 目標: {TargetXOffsetMm:F2} mm)");
                        }
                        if (!_isHoleAngleOk)
                        {
                            reasons.Add($"穴傾き角度が許容値を超えています (測定値: {LastHoleAngleDeg:F2} deg, 目標: {TargetAngleDeg:F2} deg)");
                        }
                    }
                }
            }

            result.FailureReasons = reasons;
            result.Measurements = measurements;

            // 出力画像の作成と描画
            Mat output = new Mat();
            if (inputFrame.Channels() == 1)
                Cv2.CvtColor(inputFrame, output, ColorConversionCodes.GRAY2BGR);
            else
                inputFrame.CopyTo(output);

            DrawOverlay(output);
            result.OutputImage = output;

            // 二値化デバッグ画像の設定
            Mat bin = new Mat();
            GetDebugImage(bin);
            result.BinaryImage = bin;

            return result;
        }
    }
}