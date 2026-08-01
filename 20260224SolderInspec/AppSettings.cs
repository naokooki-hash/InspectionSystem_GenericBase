using System;
using System.IO;
using System.Text.Json;

namespace _20260224SolderInspec
{
    /// <summary>
    /// PLC通信およびトリガーモードを管理するための設定クラス
    /// </summary>
    public class AppSettings
    {
        // --- PLC通信設定 ---
        public string PlcIpAddress { get; set; } = "192.168.3.250"; // 実機IPアドレス
        public int PlcPort { get; set; } = 5000;                     // 実機ポート

        // --- デバイスアドレス設定 ---
        public int OkDeviceAddress { get; set; } = 100;    // 良品結果書き込み先 (M100)
        public int NgDeviceAddress { get; set; } = 101;    // 不良品結果書き込み先 (M101)
        public int ReadDeviceAddress { get; set; } = 102;  // トリガー信号を読み取る先 (M102)
        public int WriteDeviceAddress { get; set; } = 100; // 互換用

        // --- 動作モード設定 ---
        public string TriggerMode { get; set; } = "Plc"; // "Plc" または "Visual"
        public int InspectionIntervalMs { get; set; } = 500; // PLC監視の同期間隔(ms)

        // --- JSON読み書き用メソッド ---
        public static AppSettings Load(string filePath = "settings.json")
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定ファイルの読み込みに失敗しました: {ex.Message}");
            }
            // ファイルが無い、またはエラーの場合はデフォルト設定を作って保存・返却する
            var defaultSettings = new AppSettings();
            defaultSettings.Save(filePath);
            return defaultSettings;
        }

        public void Save(string filePath = "settings.json")
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定ファイルの保存に失敗しました: {ex.Message}");
            }
        }
    }
}