using System;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading.Tasks;

namespace _20260224SolderInspec
{
    public class PlcCommunicator : IDisposable
    {
        private TcpClient _client;
        private string _ipAddress = "192.168.3.250"; // ※実際のPLCのIPアドレスに合わせてください
        private int _port = 5002;                    // SLMP用ポート番号

        // 接続状態確認
        public bool IsConnected => _client != null && _client.Connected;

        public async Task<bool> ConnectAsync()
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_ipAddress, _port);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PLC接続失敗: {ex.Message}");
                return false;
            }
        }

        public void SendResult(int result)
        {
            if (!IsConnected) return;

            try
            {
                // MCプロトコル 3Eフレーム 書き込みコマンド (D100へ1点書き込み)
                byte[] request = new byte[] {
                    0x50, 0x00,         // サブヘッダ (要求)
                    0x00,               // ネットワーク番号
                    0xFF,               // PC番号
                    0xFF, 0x03,         // 要求先ユニットI/O番号
                    0x00,               // 要求先局番号
                    0x0C, 0x00,         // 要求データ長 (これ以降のバイト数: 12バイト)
                    0x10, 0x00,         // CPU監視タイマ
                    0x01, 0x14,         // コマンド (一括書込み)
                    0x00, 0x00,         // サブコマンド (ワード単位)
                    0x64, 0x00, 0x00,   // 先頭デバイス番号 (D100 = 0x64)
                    0xA8,               // デバイスコード (D = 0xA8)
                    0x01, 0x00,         // 書込み点数 (1点)
                    (byte)(result & 0xFF), (byte)((result >> 8) & 0xFF) // 書込みデータ
                };

                NetworkStream stream = _client.GetStream();
                stream.Write(request, 0, request.Length);

                // 応答の受信（バッファをクリアするため）
                byte[] response = new byte[256];
                stream.Read(response, 0, response.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PLC送信エラー: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _client?.Close();
            _client?.Dispose();
        }
    }
}