using OpenCvSharp;

namespace InspectionSystem_GenericBase
{
    /// <summary>
    /// 各種検査処理を行うエンジンの共通インターフェース
    /// </summary>
    public interface IInspectionEngine
    {
        /// <summary>
        /// 検査エンジンの識別名
        /// </summary>
        string EngineName { get; }

        /// <summary>
        /// フレーム画像を入力し、詳細な検査を実行して結果を返す
        /// </summary>
        /// <param name="inputFrame">入力のカメラフレーム</param>
        /// <returns>検査結果オブジェクト</returns>
        InspectionResult Inspect(Mat inputFrame);

        /// <summary>
        /// リアルタイムデバッグ用の最新二値化画像を取得する
        /// </summary>
        /// <param name="dst">出力先となるMat</param>
        void GetDebugImage(Mat dst);
    }
}
