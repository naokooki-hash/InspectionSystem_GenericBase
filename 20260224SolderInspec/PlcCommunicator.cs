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

        public bool IsConnected => _client != null && _client.Connected;

        public PlcCommunicator(AppSettings settings)
        {
            _settings = settings;
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
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PLC接続エラー: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }
            finally
            {
                _stream = null;
                _client = null;
            }
        }

        // --- 既存の互換性用メソッド (Form1から呼ばれることを想定) ---
        public bool SendResult(bool isOk)
        {
            // OKなら1、NGなら2を書き込む (現場の仕様に合わせて変更可能)
            int valueToWrite = isOk ? 1 : 2;
            return WriteDevice(_settings.WriteDeviceAddress, valueToWrite);
        }

        // --- MCプロトコル: 一括読出し (0401) ---
        public int ReadDevice(int deviceAddress)
        {
            if (!IsConnected) return -1;

            try
            {
                // Dレジスタ (A8)
                byte[] addressBytes = BitConverter.GetBytes(deviceAddress);
                byte[] command = new byte[]
                {
                    0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, // サブヘッダ等
                    0x0C, 0x00,                               // データ長 (12バイト)
                    0x10, 0x00,                               // 監視タイマ
                    0x01, 0x04,                               // コマンド (0401:一括読出し)
                    0x00, 0x00,                               // サブコマンド
                    addressBytes[0], addressBytes[1], 0x00,   // 先頭デバイス番号 (リトルエンディアン)
                    0xA8,                                     // デバイスコード (Dレジスタ)
                    0x01, 0x00                                // デバイス点数 (1点)
                };

                _stream.Write(command, 0, command.Length);

                byte[] response = new byte[13];
                int bytesRead = _stream.Read(response, 0, response.Length);

                if (bytesRead >= 13 && response[9] == 0x00 && response[10] == 0x00) // 終了コード0 (正常)
                {
                    // 11, 12バイト目が読み取った値 (リトルエンディアン)
                    return BitConverter.ToInt16(response, 11);
                }
                return -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"読出しエラー: {ex.Message}");
                Disconnect(); // エラー時は切断して再接続を促す
                return -1;
            }
        }

        // --- MCプロトコル: 一括書込み (1401) ---
        public bool WriteDevice(int deviceAddress, int writeValue)
        {
            if (!IsConnected) return false;

            try
            {
                byte[] addressBytes = BitConverter.GetBytes(deviceAddress);
                byte[] valueBytes = BitConverter.GetBytes((short)writeValue);

                byte[] command = new byte[]
                {
                    0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, // サブヘッダ等
                    0x0E, 0x00,                               // データ長 (14バイト)
                    0x10, 0x00,                               // 監視タイマ
                    0x01, 0x14,                               // コマンド (1401:一括書込み)
                    0x00, 0x00,                               // サブコマンド
                    addressBytes[0], addressBytes[1], 0x00,   // 先頭デバイス番号
                    0xA8,                                     // デバイスコード (Dレジスタ)
                    0x01, 0x00,                               // デバイス点数 (1点)
                    valueBytes[0], valueBytes[1]              // 書き込むデータ
                };

                _stream.Write(command, 0, command.Length);

                byte[] response = new byte[11];
                int bytesRead = _stream.Read(response, 0, response.Length);

                // 終了コードが 0x00 0x00 なら成功
                return (bytesRead >= 11 && response[9] == 0x00 && response[10] == 0x00);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"書込みエラー: {ex.Message}");
                Disconnect();
                return false;
            }
        }
    }
}