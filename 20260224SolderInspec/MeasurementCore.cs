using System;
using System.Diagnostics;
using OpenCvSharp;

namespace _20260224SolderInspec
{
    public class MeasurementCore
    {
        // 検査ロジッククラス

        /// <summary>
        /// 指定されたROIの平均輝度を計算します。
        /// </summary>
        /// <param name="img">入力画像 (グレースケール)</param>
        /// <param name="roi">計算対象の矩形領域</param>
        /// <returns>平均輝度 (0-255)</returns>
        public double CalculateBrightness(Mat img, Rect roi)
        {
            if (img == null || img.Empty())
                return 0.0;

            // ROIが画像範囲内かチェック
            Rect validRoi = roi & new Rect(0, 0, img.Width, img.Height);
            if (validRoi.Width <= 0 || validRoi.Height <= 0)
                return 0.0;

            using (Mat roiImg = new Mat(img, validRoi))
            {
                Scalar mean = Cv2.Mean(roiImg);
                return mean.Val0;
            }
        }

        /// <summary>
        /// 検査を実行します。
        /// </summary>
        /// <param name="img">検査対象画像</param>
        /// <returns>検査結果 (1:OK, 2:NG)</returns>
        public int Inspect(Mat img)
        {
            // ここに実際の画像処理検査ロジックを実装します。
            // 現時点ではダミー実装として常にOK(1)を返します。
            // 必要に応じてNG(2)を返すロジックを追加してください。

            Debug.WriteLine("Running inspection logic...");

            // 例: 画像全体の平均輝度が極端に低い場合はNGとするなど
            // double avg = Cv2.Mean(img).Val0;
            // if (avg < 10) return 2;

            return 1; // OK
        }
    }
}
