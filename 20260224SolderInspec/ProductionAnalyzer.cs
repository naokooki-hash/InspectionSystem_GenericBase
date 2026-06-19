using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _20260224SolderInspec
{
    public class ProductionAnalyzer
    {
        public class InspectionRecord
        {
            public DateTime Timestamp { get; set; }
            public double CycleTimeSec { get; set; }
            public double Angle { get; set; }
            public double Brightness { get; set; }
            public string Result { get; set; }
            public string StoppageType { get; set; }
        }

        private List<InspectionRecord> _records = new List<InspectionRecord>();
        private DateTime _lastTriggerTime = DateTime.MinValue;

        public double ChocoTeiThresholdSec { get; set; } = 10.0;
        public double DokaTeiThresholdSec { get; set; } = 60.0;
        public double AngleUpperSpecification { get; set; } = 2.5;
        public double AngleLowerSpecification { get; set; } = -2.5;

        public void AddRecord(double angle, double brightness, bool isOk, string logDirPath)
        {
            DateTime now = DateTime.Now;
            double cycleTime = 0;

            if (_lastTriggerTime != DateTime.MinValue)
            {
                cycleTime = (now - _lastTriggerTime).TotalSeconds;
            }
            _lastTriggerTime = now;

            string stoppageType = "正常";
            if (cycleTime >= DokaTeiThresholdSec) stoppageType = "ドカ停";
            else if (cycleTime >= ChocoTeiThresholdSec) stoppageType = "チョコ停";

            var record = new InspectionRecord
            {
                Timestamp = now,
                CycleTimeSec = cycleTime,
                Angle = angle,
                Brightness = brightness,
                Result = isOk ? "OK" : "NG",
                StoppageType = stoppageType
            };

            _records.Add(record);

            if (_records.Count > 5000) _records.RemoveAt(0);

            WriteDetailedCsv(record, logDirPath);
        }

        private void WriteDetailedCsv(InspectionRecord r, string logDirPath)
        {
            try
            {
                // 1. まず大元の「Logs」フォルダが確実に存在するかチェックして作成
                if (!Directory.Exists(logDirPath))
                {
                    Directory.CreateDirectory(logDirPath);
                }

                // 2. 今日の日付フォルダ（例: 20260619）の存在確認と作成
                string dir = Path.Combine(logDirPath, DateTime.Now.ToString("yyyyMMdd"));
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string logFile = Path.Combine(dir, "ProductionAnalysisLog.csv");
                bool isNewFile = !File.Exists(logFile);

                // 3. CSVファイルへの書き込み（ファイルが無ければ自動生成される）
                using (StreamWriter sw = new StreamWriter(logFile, true, System.Text.Encoding.UTF8))
                {
                    if (isNewFile)
                    {
                        sw.WriteLine("時刻,判定,サイクルタイム(秒),停止タイプ,実測角度,実測輝度");
                    }
                    sw.WriteLine($"{r.Timestamp:HH:mm:ss},{r.Result},{r.CycleTimeSec:F2},{r.StoppageType},{r.Angle:F2},{r.Brightness:F2}");
                }
            }
            catch
            {
                // 万が一OSのロック等で書き込めなくても、アプリ全体はクラッシュさせない
            }
        }

        public List<InspectionRecord> GetRecords() => _records.ToList();
        public int GetChocoTeiCount() => _records.Count(r => r.StoppageType == "チョコ停");
        public int GetDokaTeiCount() => _records.Count(r => r.StoppageType == "ドカ停");

        public (double Cp, double Cpk, double Average, double StdDev) CalculateCpCpk()
        {
            if (_records.Count < 2) return (0, 0, 0, 0);

            double[] data = _records.Select(r => r.Angle).ToArray();
            double avg = data.Average();
            double sumOfSquares = data.Select(val => (val - avg) * (val - avg)).Sum();
            double stdDev = Math.Sqrt(sumOfSquares / (data.Length - 1));

            if (stdDev == 0) return (0, 0, avg, 0);

            double cp = (AngleUpperSpecification - AngleLowerSpecification) / (6 * stdDev);
            double cpu = (AngleUpperSpecification - avg) / (3 * stdDev);
            double cpl = (avg - AngleLowerSpecification) / (3 * stdDev);
            double cpk = Math.Min(cpu, cpl);

            return (cp, cpk, avg, stdDev);
        }

        public (double Average, double StdDev) CalculateBrightnessStats()
        {
            if (_records.Count < 2) return (0, 0);
            double[] data = _records.Select(r => r.Brightness).ToArray();
            double avg = data.Average();
            double sumOfSquares = data.Select(val => (val - avg) * (val - avg)).Sum();
            double stdDev = Math.Sqrt(sumOfSquares / (data.Length - 1));
            return (avg, stdDev);
        }
    }
}