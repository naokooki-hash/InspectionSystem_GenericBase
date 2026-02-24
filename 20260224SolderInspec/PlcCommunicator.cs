using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace _20260224SolderInspec
{
    public class PlcCommunicator : IDisposable
    {
        // 将来的なPLC通信のためのプレースホルダー

        /// <summary>
        /// 検査結果をPLCに送信します。
        /// </summary>
        /// <param name="result">1: OK, 2: NG</param>
        public void SendResult(int result)
        {
            // TODO: 将来的にMCプロトコル（TCP）でD100に書き込む実装を追加してください。
            //
            // 以下の手順で実装可能です:
            // 1. TcpClientを使用してPLCに接続
            // 2. MCプロトコルの書き込みコマンド（バイナリコード）を送信
            // 3. レスポンスを受信・確認

            Debug.WriteLine($"[PLC] Sending result: {result} (D100)");

            // ダミー遅延（通信時間をシミュレート）
            // Task.Delay(10).Wait();
        }

        public void Dispose()
        {
            // TcpClientなどのリソース解放処理
        }
    }
}
