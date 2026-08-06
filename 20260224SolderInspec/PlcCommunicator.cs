using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace _20260224SolderInspec
{
    public class PlcCommunicator
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private AppSettings _settings;

        public event Action<string, bool>? OnLog;

        public bool IsConnected => _client != null && _client.Connected;

        public PlcCommunicator(AppSettings settings)
        {
            _settings = settings;
        }

        private void Log(string message, bool isError = false)
        {
            OnLog?.Invoke(message, isError);
            Console.WriteLine($"[PLC] {message}");
        }

        // --- 接続・切断 ---
        public bool Connect()
        {
            try
            {
                if (IsConnected) return true;

                _client = new TcpClient();
                // タイムアウト設定 (ミリ秒)
                _client.ReceiveTimeout = 2000;
                _client.SendTimeout = 2000;

                _client.Connect(_settings.PlcIpAddress, _settings.PlcPort);
                _stream = _client.GetStream();
                Log($"PLCに接続しました: {_settings.PlcIpAddress}:{_settings.PlcPort}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"PLC接続エラー ({_settings.PlcIpAddress}:{_settings.PlcPort}): {ex.Message}", true);
                return false;
            }
        }

        public void Disconnect()
        {
            if (_client == null) return;
            try
            {
                _stream?.Close();
                _client?.Close();
                Log("PLCを切断しました");
            }
            catch (Exception ex)
            {
                Log($"PLC切断エラー: {ex.Message}", true);
            }
            finally
            {
                _stream = null;
                _client = null;
            }
        }

        // --- 判定結果送信 (良品=ON, 不良=ON) ---
        public bool SendResult(bool isOk, int okAddress, int ngAddress)
        {
            bool success = true;
            success &= WriteDevice(okAddress, isOk ? 1 : 0);
            success &= WriteDevice(ngAddress, isOk ? 0 : 1);
            return success;
        }

        // --- MCプロトコル: 一括読出し (0401) ※ビット単位 (サブコマンド0001) ---
        public int ReadDevice(int deviceAddress)
        {
            if (!IsConnected) return -1;

            try
            {
                // Mレジスタ (90h) - ビット単位での読み出し
                byte[] addressBytes = BitConverter.GetBytes(deviceAddress);
                byte[] command = new byte[]
                {
                    0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, // サブヘッダ等
                    0x0C, 0x00,                               // データ長 (12バイト)
                    0x10, 0x00,                               // 監視タイマ
                    0x01, 0x04,                               // コマンド (0401:一括読出し)
                    0x01, 0x00,                               // サブコマンド (0001:ビット単位)
                    addressBytes[0], addressBytes[1], 0x00,   // 先頭デバイス番号 (リトルエンディアン)
                    0x90,                                     // デバイスコード (0x90: Mレジスタ)
                    0x01, 0x00                                // デバイス点数 (1点)
                };

                _stream.Write(command, 0, command.Length);

                byte[] response = new byte[12]; // ヘッダ9 + 終了コード2 + データ1 = 12バイト
                int bytesRead = _stream.Read(response, 0, response.Length);

                if (bytesRead >= 12 && response[9] == 0x00 && response[10] == 0x00) // 終了コード0 (正常)
                {
                    // 12バイト目（インデックス11）のデータ部。上位4ビットが 1 (0x10) なら ON
                    return (response[11] & 0xF0) == 0x10 ? 1 : 0;
                }
                Log($"読出し応答エラー (受信バイト数: {bytesRead})", true);
                return -1;
            }
            catch (Exception ex)
            {
                Log($"M{deviceAddress} 読出しエラー: {ex.Message}", true);
                Disconnect(); // エラー時は切断して再接続を促す
                return -1;
            }
        }

        // --- MCプロトコル: 一括書込み (1401) ※ビット単位 (サブコマンド0001) ---
        public bool WriteDevice(int deviceAddress, int writeValue)
        {
            if (!IsConnected) return false;

            try
            {
                byte[] addressBytes = BitConverter.GetBytes(deviceAddress);
                // ビットONは 0x10、OFFは 0x00 (1ニブル指定)
                byte writeData = writeValue != 0 ? (byte)0x10 : (byte)0x00;

                byte[] command = new byte[]
                {
                    0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, // サブヘッダ等
                    0x0D, 0x00,                               // データ長 (13バイト)
                    0x10, 0x00,                               // 監視タイマ
                    0x01, 0x14,                               // コマンド (1401:一括書込み)
                    0x01, 0x00,                               // サブコマンド (0001:ビット単位)
                    addressBytes[0], addressBytes[1], 0x00,   // 先頭デバイス番号
                    0x90,                                     // デバイスコード (0x90: Mレジスタ)
                    0x01, 0x00,                               // デバイス点数 (1点)
                    writeData                                 // 書込みデータ
                };

                _stream.Write(command, 0, command.Length);

                byte[] response = new byte[11];
                int bytesRead = _stream.Read(response, 0, response.Length);

                bool success = (bytesRead >= 11 && response[9] == 0x00 && response[10] == 0x00);
                if (success)
                {
                    Log($"M{deviceAddress} に {(writeValue != 0 ? "ON" : "OFF")} を書き込みました");
                }
                else
                {
                    Log($"M{deviceAddress} 書込み応答エラー (受信バイト数: {bytesRead})", true);
                }
                return success;
            }
            catch (Exception ex)
            {
                Log($"M{deviceAddress} 書込みエラー (値: {writeValue}): {ex.Message}", true);
                Disconnect();
                return false;
            }
        }
    }
}