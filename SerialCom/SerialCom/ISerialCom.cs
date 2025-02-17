namespace SerialCom
{
    public interface ISerialCom : IDisposable
    {
        event EventHandler<Exception> OnError; // 오류 발생 시 발생하는 이벤트

        string PortName
        {
            get; set;
        } // 포트 이름
        int BaudRate
        {
            get; set;
        } // 보드레이트

        List<byte[]> CustomEndOfMessageBytes
        {
            get; set;
        } // 엔드 코드 설정

        bool IsOpen(); // 포트가 열려 있는지 확인
        bool OpenComPort(); // 포트 열기
        bool CloseComPort(); // 포트 닫기

        void Send(byte[] data); // 바이트 배열 전송
        void Send(string data); // 문자열 전송
        void SendLine(byte[] data); // 바이트 배열을 문자열로 변환하여 전송
        void SendLine(string data); // 문자열 전송

        event Action<byte[]> DataReceived;
    }




}
