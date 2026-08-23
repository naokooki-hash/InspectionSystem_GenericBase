using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace InspectionSystem_GenericBase
{
    public class PlcCommunicator
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private AppSettings _settings;
        private readonly SemaphoreSlim _tcpLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _heartbeatCts;

        public event Action<string, bool>? OnLog;

        public bool IsConnected => _client != null && _client.Connected;

        public PlcCommunicator(AppSettings settings)
        {
            _settings = settings;
            StartHeartbeat();
        }

        private void StartHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts = new CancellationTokenSource();
            var token = _heartbeatCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (IsConnected)
                    {
                        try
                        {
                            ReadDevice(_settings.HeartbeatAddress);
                        }
                        catch (Exception ex)
                        {
                            Log($"Heartbeat error: {ex.Message}", true);
                        }
                    }
                    await Task.Delay(1000, token);
                }
            }, token);
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

        public int ReadDevice(int deviceAddress)
        {
            if (!IsConnected || _stream == null) return -1;

            _tcpLock.Wait();
            try
            {
                if (_settings.PlcVendor == "Keyence")
                {
                    string prefix = _settings.PlcDataType == "Word" ? "DM" : "MR";
                    string cmd = $"RD {prefix}{deviceAddress}\r";
                    byte[] command = System.Text.Encoding.ASCII.GetBytes(cmd);
                    _stream.Write(command, 0, command.Length);

                    byte[] response = new byte[256];
                    int bytesRead = _stream.Read(response, 0, response.Length);
                    string resStr = System.Text.Encoding.ASCII.GetString(response, 0, bytesRead).TrimEnd('\r', '\n');

                    if (resStr.EndsWith("E"))
                    {
                        Log($"読出し応答エラー: {resStr}", true);
                        return -1;
                    }

                    if (int.TryParse(resStr, out int val))
                    {
                        return val;
                    }
                    return -1;
                }
                else // Mitsubishi
                {
                    byte[] addressBytes = BitConverter.GetBytes(deviceAddress);
                    bool isWord = _settings.PlcDataType == "Word";

                    byte subCommand = isWord ? (byte)0x00 : (byte)0x01;
                    byte deviceCode = isWord ? (byte)0xA8 : (byte)0x90; // A8 = D register, 90 = M register

                    byte[] command = new byte[]
                    {
                        0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, // サブヘッダ等
                        0x0C, 0x00,                               // データ長 (12バイト)
                        0x10, 0x00,                               // 監視タイマ
                        0x01, 0x04,                               // コマンド (0401:一括読出し)
                        subCommand, 0x00,                         // サブコマンド (0000:ワード単位, 0001:ビット単位)
                        addressBytes[0], addressBytes[1], 0x00,   // 先頭デバイス番号 (リトルエンディアン)
                        deviceCode,                               // デバイスコード
                        0x01, 0x00                                // デバイス点数 (1点)
                    };

                    _stream.Write(command, 0, command.Length);

                    int expectedResponseLength = isWord ? 13 : 12;
                    byte[] response = new byte[expectedResponseLength];
                    int bytesRead = _stream.Read(response, 0, response.Length);

                    if (bytesRead >= 11 && response[9] == 0x00 && response[10] == 0x00) // 終了コード0 (正常)
                    {
                        if (isWord)
                        {
                            if (bytesRead >= 13)
                                return BitConverter.ToUInt16(response, 11);
                        }
                        else
                        {
                            if (bytesRead >= 12)
                                return (response[11] & 0xF0) == 0x10 ? 1 : 0;
                        }
                    }
                    Log($"読出し応答エラー (受信バイト数: {bytesRead})", true);
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Log($"{deviceAddress} 読出しエラー: {ex.Message}", true);
                Disconnect();
                return -1;
            }
            finally
            {
                _tcpLock.Release();
            }
        }

        public bool WriteDevice(int deviceAddress, int writeValue)
        {
            if (!IsConnected || _stream == null) return false;

            _tcpLock.Wait();
            try
            {
                if (_settings.PlcVendor == "Keyence")
                {
                    string prefix = _settings.PlcDataType == "Word" ? "DM" : "MR";
                    string cmd = $"WR {prefix}{deviceAddress} {writeValue}\r";
                    byte[] command = System.Text.Encoding.ASCII.GetBytes(cmd);
                    _stream.Write(command, 0, command.Length);

                    byte[] response = new byte[256];
                    int bytesRead = _stream.Read(response, 0, response.Length);
                    string resStr = System.Text.Encoding.ASCII.GetString(response, 0, bytesRead).TrimEnd('\r', '\n');

                    if (resStr == "OK")
                    {
                        Log($"{prefix}{deviceAddress} に {writeValue} を書き込みました");
                        return true;
                    }
                    else
                    {
                        Log($"{prefix}{deviceAddress} 書込み応答エラー: {resStr}", true);
                        return false;
                    }
                }
                else // Mitsubishi
                {
                    byte[] addressBytes = BitConverter.GetBytes(deviceAddress);
                    bool isWord = _settings.PlcDataType == "Word";

                    byte subCommand = isWord ? (byte)0x00 : (byte)0x01;
                    byte deviceCode = isWord ? (byte)0xA8 : (byte)0x90;

                    byte[] writeDataBytes;
                    int dataLength;

                    if (isWord)
                    {
                        writeDataBytes = BitConverter.GetBytes((ushort)writeValue);
                        dataLength = 14;
                    }
                    else
                    {
                        writeDataBytes = new byte[] { writeValue != 0 ? (byte)0x10 : (byte)0x00 };
                        dataLength = 13;
                    }

                    byte[] command = new byte[dataLength + 9]; // ヘッダ等9バイト分追加

                    // ヘッダ等設定
                    Buffer.BlockCopy(new byte[] { 0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00 }, 0, command, 0, 7);

                    // データ長 (コマンド以降のバイト数)
                    byte[] lenBytes = BitConverter.GetBytes((ushort)dataLength);
                    command[7] = lenBytes[0];
                    command[8] = lenBytes[1];

                    // 残りのヘッダ
                    command[9] = 0x10; command[10] = 0x00; // 監視タイマ
                    command[11] = 0x01; command[12] = 0x14; // コマンド (1401:一括書込み)
                    command[13] = subCommand; command[14] = 0x00;
                    command[15] = addressBytes[0]; command[16] = addressBytes[1]; command[17] = 0x00;
                    command[18] = deviceCode;
                    command[19] = 0x01; command[20] = 0x00; // デバイス点数

                    // 書き込みデータ
                    Buffer.BlockCopy(writeDataBytes, 0, command, 21, writeDataBytes.Length);

                    _stream.Write(command, 0, command.Length);

                    byte[] response = new byte[11];
                    int bytesRead = _stream.Read(response, 0, response.Length);

                    bool success = (bytesRead >= 11 && response[9] == 0x00 && response[10] == 0x00);
                    if (success)
                    {
                        Log($"{deviceAddress} に {writeValue} を書き込みました");
                    }
                    else
                    {
                        Log($"{deviceAddress} 書込み応答エラー (受信バイト数: {bytesRead})", true);
                    }
                    return success;
                }
            }
            catch (Exception ex)
            {
                Log($"{deviceAddress} 書込みエラー (値: {writeValue}): {ex.Message}", true);
                Disconnect();
                return false;
            }
            finally
            {
                _tcpLock.Release();
            }
        }
    }
}