using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace InspectionSystem_GenericBase
{
    public class ProductionAnalyzer
    {
        public class InspectionRecord
        {
            public DateTime Timestamp { get; set; }
            public double CycleTimeSec { get; set; }
            public double TargetValue { get; set; }  // 汎用化: 角度や寸法など
            public double EnvValue { get; set; }     // 汎用化: 輝度や面積など
            public string Result { get; set; } = "OK";
            public string StoppageType { get; set; } = "正常";
        }

        private List<InspectionRecord> _records = new List<InspectionRecord>();
        private DateTime _lastTriggerTime = DateTime.MinValue;

        // 【完全汎用化】プロジェクトごとに設定できる名前と単位
        public string TargetValueName { get; set; } = "測定値";
        public string TargetValueUnit { get; set; } = "unit";
        public string EnvValueName { get; set; } = "環境値";
        public string EnvValueUnit { get; set; } = "val";

        public double ChocoTeiThresholdSec { get; set; } = 10.0;
        public double DokaTeiThresholdSec { get; set; } = 60.0;
        public double TargetUpperSpecification { get; set; } = 2.5;  // USL
        public double TargetLowerSpecification { get; set; } = -2.5; // LSL

        public void AddRecord(double targetVal, double envVal, bool isOk, string logDirPath)
        {
            DateTime now = DateTime.Now;
            double cycleTime = 0;

            if (_lastTriggerTime != DateTime.MinValue) cycleTime = (now - _lastTriggerTime).TotalSeconds;
            _lastTriggerTime = now;

            string stoppageType = "正常";
            if (cycleTime >= DokaTeiThresholdSec) stoppageType = "ドカ停";
            else if (cycleTime >= ChocoTeiThresholdSec) stoppageType = "チョコ停";

            var record = new InspectionRecord
            {
                Timestamp = now,
                CycleTimeSec = cycleTime,
                TargetValue = targetVal,
                EnvValue = envVal,
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
                if (!Directory.Exists(logDirPath)) Directory.CreateDirectory(logDirPath);
                string dir = Path.Combine(logDirPath, DateTime.Now.ToString("yyyyMMdd"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string logFile = Path.Combine(dir, "ProductionAnalysisLog.csv");
                bool isNewFile = !File.Exists(logFile);

                using (StreamWriter sw = new StreamWriter(logFile, true, System.Text.Encoding.UTF8))
                {
                    if (isNewFile)
                    {
                        // ヘッダーがプロジェクトの設定名に自動で切り替わります
                        sw.WriteLine($"時刻,判定,サイクルタイム(秒),停止タイプ,{TargetValueName},{EnvValueName}");
                    }
                    sw.WriteLine($"{r.Timestamp:HH:mm:ss},{r.Result},{r.CycleTimeSec:F2},{r.StoppageType},{r.TargetValue:F3},{r.EnvValue:F2}");
                }
            }
            catch { }
        }

        public List<InspectionRecord> GetRecords() => _records.ToList();
        public int GetChocoTeiCount() => _records.Count(r => r.StoppageType == "チョコ停");
        public int GetDokaTeiCount() => _records.Count(r => r.StoppageType == "ドカ停");

        public (double Cp, double Cpk, double Average, double StdDev) CalculateCpCpk()
        {
            if (_records.Count < 2) return (0, 0, 0, 0);
            double[] data = _records.Select(r => r.TargetValue).ToArray();
            double avg = data.Average();
            double sumOfSquares = data.Select(val => (val - avg) * (val - avg)).Sum();
            double stdDev = Math.Sqrt(sumOfSquares / (data.Length - 1));

            if (stdDev == 0) return (0, 0, avg, 0);
            double cp = (TargetUpperSpecification - TargetLowerSpecification) / (6 * stdDev);
            double cpu = (TargetUpperSpecification - avg) / (3 * stdDev);
            double cpl = (avg - TargetLowerSpecification) / (3 * stdDev);
            return (cp, Math.Min(cpu, cpl), avg, stdDev);
        }

        public (double Average, double StdDev) CalculateEnvStats()
        {
            if (_records.Count < 2) return (0, 0);
            double[] data = _records.Select(r => r.EnvValue).ToArray();
            double avg = data.Average();
            double sumOfSquares = data.Select(val => (val - avg) * (val - avg)).Sum();
            return (avg, Math.Sqrt(sumOfSquares / (data.Length - 1)));
        }
    }
}