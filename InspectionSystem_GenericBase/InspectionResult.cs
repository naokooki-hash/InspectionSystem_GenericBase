using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace InspectionSystem_GenericBase
{
    /// <summary>
    /// 検査結果の情報を受け渡すためのクラス
    /// </summary>
    public class InspectionResult : IDisposable
    {
        /// <summary>
        /// 総合判定成否
        /// </summary>
        public bool IsOk { get; set; }

        /// <summary>
        /// 判定の文字列表現 ("OK", "NG", "ERR")
        /// </summary>
        public string ResultText { get; set; } = "ERR";

        /// <summary>
        /// アライメント判定成否
        /// </summary>
        public bool IsAlignmentOk { get; set; }

        /// <summary>
        /// アライメント詳細ステータス (1: OK, 2: NG(許容値超え), 3: 検出エラー)
        /// </summary>
        public int AlignmentStatus { get; set; } = 3;

        /// <summary>
        /// NG判定またはエラー時の詳細理由リスト（日本語）
        /// </summary>
        public List<string> FailureReasons { get; set; } = new List<string>();

        /// <summary>
        /// 各種検査枠や計測テキストを重畳描画した出力画像
        /// </summary>
        public Mat? OutputImage { get; set; }

        /// <summary>
        /// 二値化デバッグ表示用画像
        /// </summary>
        public Mat? BinaryImage { get; set; }

        /// <summary>
        /// 抽出された詳細測定値の辞書 (例: "HoleDistancePx", "AngleOffsetDeg" など)
        /// </summary>
        public Dictionary<string, double> Measurements { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// 検査処理が実行された日時
        /// </summary>
        public DateTime ProcessedTime { get; set; } = DateTime.Now;

        public void Dispose()
        {
            OutputImage?.Dispose();
            BinaryImage?.Dispose();
        }
    }
}
