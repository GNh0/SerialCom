using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SerialCom
{
    public class SerialCom : ISerialCom
    {
        private SerialPort _serialPort;
        private MemoryStream _messageBuffer;
        private ConcurrentQueue<byte[]> _messageBufferQueue;
        private CancellationTokenSource _cancellationTokenSource;

        public event EventHandler<Exception> OnError;
        public delegate void DataReceivedEventHandler(byte[] data);
        public event Action<byte[]> DataReceived;
        public List<byte[]> CustomEndOfMessageBytes
        {
            get; set;
        } = new List<byte[]> { new byte[] { 0x0A, 0x0D }, new byte[] { 0x0D, 0x0A }, new byte[] { 0x0A }, new byte[] { 0x0D } };

        public string PortName
        {
            get; set;
        }
        public int BaudRate
        {
            get; set;
        }


        public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

        /// <summary>
        /// COM1,COM2,COM11,COM12 이렇게 있을때 COM1,COM11,COM12 순으로 정렬되던것을 COM1,COM2,COM11,COM12 로 정렬되게 적용
        /// </summary>
        /// <returns></returns>
        public static string[] GetSortedAvailablePorts()
        {
            var comports = SerialPort.GetPortNames().ToList();
            comports.Sort(ComparePortNames);
            return comports.ToArray();
        }

        /// <summary>
        /// 포트 이름을 비교하는 메서드
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private static int ComparePortNames(string x, string y)
        {
            int numX = ExtractNumber(x);
            int numY = ExtractNumber(y);

            int prefixComparison = string.Compare(ExtractPrefix(x), ExtractPrefix(y));

            if (prefixComparison != 0)
            {
                return prefixComparison;
            }

            return numX.CompareTo(numY);
        }

        /// <summary>
        /// 포트 이름에서 숫자를 추출하는 메서드
        /// </summary>
        /// <param name="portName"></param>
        /// <returns></returns>
        private static int ExtractNumber(string portName)
        {
            var match = Regex.Match(portName, @"\d+");
            return match.Success ? int.Parse(match.Value) : 0;
        }

        /// <summary>
        /// 포트 이름에서 접두사를 추출하는 메서드
        /// </summary>
        /// <param name="portName"></param>
        /// <returns></returns>
        private static string ExtractPrefix(string portName)
        {
            return Regex.Replace(portName, @"\d+", "").Trim();
        }


        public static List<int> GetSupportedBaudRates() => new List<int> { 9600, 19200, 38400, 57600, 115200 };

        private static SerialCom _instance = new SerialCom();
        private static readonly object _lock = new object();
        public static SerialCom Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new SerialCom();
                    }
                    return _instance;
                }
            }
        }


        public SerialCom()
        {

        }


        public SerialCom(string portName, int baudRate) : this()
        {
            PortName = portName;
            BaudRate = baudRate;

        }

        public bool IsOpen() => _serialPort?.IsOpen == true;

        public bool OpenComPort()
        {
            if (IsOpen())
            {
                throw new InvalidOperationException($"{PortName} is already open.");
            }


            try
            {
                _serialPort = new SerialPort(PortName, BaudRate)
                {
                    ReadTimeout = 100,
                    WriteTimeout = 100
                };

                _messageBuffer = new MemoryStream();
                _messageBufferQueue = new ConcurrentQueue<byte[]>();
                _cancellationTokenSource = new CancellationTokenSource();
                _serialPort.DataReceived += OnDataReceived;
                _serialPort.Open();
                return true;
            }
            catch (Exception ex)
            {
                LogException(ex);
                OnError?.Invoke(this, ex);
                return false;
            }
        }

        public bool CloseComPort()
        {
            if (!IsOpen())
                return false;

            try
            {
                _messageBuffer?.Close();
                _messageBufferQueue?.Clear();
                if (_serialPort != null)
                {
                    // _serialPort.BaseStream.Close();
                    _serialPort.DataReceived -= OnDataReceived;
                    _serialPort.Close();
                    _serialPort = null;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogException(ex);
                OnError?.Invoke(this, ex);
                return false;
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {

                int bytesToRead = _serialPort.BytesToRead;

                if (_serialPort.IsOpen && bytesToRead > 0)
                {
                    byte[] buffer = new byte[bytesToRead];

                    //int bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, 0, bytesToRead, token);
                    int nRecv = _serialPort.Read(buffer, 0, _serialPort.BytesToRead);


                    _messageBuffer.Write(buffer, 0, nRecv);

                    if (_messageBuffer.Length > 0)
                    {
                        ProcessClientMessage(_messageBuffer);
                    }

                    while (_messageBufferQueue.TryDequeue(out byte[] _dequeue))
                    {
                        OnDataReceived(_dequeue);
                    }
                }
            }
            catch (OperationCanceledException ocex)
            {
                // 작업이 취소된 경우
                Console.WriteLine("Data receiving was canceled.");
            }
            catch (Exception ex)
            {
                LogException(ex);
                OnError?.Invoke(this, ex);
            }
        }


        protected virtual void OnDataReceived(byte[] data)
        {

            DataReceived?.Invoke(data);

        }


        private void ProcessClientMessage(MemoryStream messageBuffer)
        {
            try
            {
                byte[] bufferArray = messageBuffer.ToArray();
                int endOfMessageIndex;
                // 메시지 주기적으로 확인
                while ((endOfMessageIndex = FindEndOfMessageIndex(bufferArray)) != -1)
                {
                    // 여기서 endCodeLength를 찾은 엔드 코드의 길이에 맞게 조정합니다.
                    int matchingEndCodeLength = FindMatchingEndCodeLength(bufferArray, endOfMessageIndex);
                    int completeMessageLength = endOfMessageIndex + matchingEndCodeLength;
                    var completeMessage = new byte[completeMessageLength];

                    Array.Copy(bufferArray, 0, completeMessage, 0, completeMessageLength);
                    //OnDataReceived(completeMessage);
                    _messageBufferQueue.Enqueue(completeMessage);


                    // 처리된 메시지를 제거합니다.
                    int remainingLength = bufferArray.Length - completeMessageLength;
                    byte[] remaining = new byte[remainingLength];

                    // 남은 버퍼를 새 배열에 복사합니다.
                    Array.Copy(bufferArray, completeMessageLength, remaining, 0, remainingLength);
                    messageBuffer.SetLength(0);
                    messageBuffer.Write(remaining, 0, remaining.Length);
                    // 갱신된 버퍼 배열로 처리 계속
                    bufferArray = remaining;
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                throw;
            }
        }


        private int FindMatchingEndCodeLength(byte[] bufferArray, int endOfMessageIndex)
        {
            try
            {
                foreach (var endBytes in CustomEndOfMessageBytes)
                {
                    bool match = true;

                    if (endOfMessageIndex + endBytes.Length > bufferArray.Length)
                    {
                        continue; // 범위를 벗어나면 다음 엔드 코드로 넘어감
                    }

                    // 엔드 코드의 길이만큼 비교
                    for (int j = 0; j < endBytes.Length; j++)
                    {
                        if (bufferArray[endOfMessageIndex + j] != endBytes[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        // 매칭되는 엔드 코드의 길이를 반환
                        return endBytes.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex);

            }

            // 기본값으로 0 반환
            return 0;
        }


        private int FindEndOfMessageIndex(byte[] bufferArray)
        {         // 엔드코드가 설정되어 있지 않다면, 메시지의 끝을 찾을 수 없습니다.
            if (CustomEndOfMessageBytes == null)
                return -1;

            // 가장 처음 나타나는 엔드코드의 시작 인덱스를 찾습니다.
            int endOfMessageIndex = -1;
            foreach (var endBytes in CustomEndOfMessageBytes)
            {
                for (int i = 0; i <= bufferArray.Length - endBytes.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < endBytes.Length; j++)
                    {
                        if (bufferArray[i + j] != endBytes[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        // 매칭이 확인되면, 엔드코드의 시작 인덱스를 반환합니다.
                        return i;
                    }
                }
            }
            return endOfMessageIndex;
        }


        public void Send(byte[] data)
        {
            try
            {
                if (IsOpen())
                {

                    _serialPort.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

        }

        public void Send(string data)
        {
            if (IsOpen())
            {
                _serialPort.Write(data);
            }
        }

        public void SendLine(byte[] data)
        {
            Send(Encoding.Default.GetString(data) + "\n");
        }

        public void SendLine(string data)
        {
            if (IsOpen())
            {
                _serialPort.WriteLine(data);
            }
        }

        public void Dispose()
        {
            try
            {
                _serialPort?.Dispose();
                _messageBuffer?.Dispose();
                _cancellationTokenSource?.Dispose();
                CloseComPort();
            }
            catch (Exception ex)
            {
                LogException(ex);
            }

        }


        private void LogException(Exception ex, [CallerMemberName] string functionName = "")
        {
            Debug.WriteLine($"{functionName}_{ex.Message}");
        }
    }
}
