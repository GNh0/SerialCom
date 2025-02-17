Language : c#

Framework : .NET Framework 8.0

Project : c# Class Library

목적 : 여러 프로젝트에서 사용하기 위한 SerialCom 라이브러리 구현

기능 :

List<byte[]> 형으로 CustomEndOfMessageBytes 를 지정하여 수신데이터 EndCode를 설정

DataReceived 데이터 수신 이벤트 연결 메서드 OnDataReceived(byte[] data)로 데이터 수신부 처리



To-do

속성 Stopbits,Parity,Databit 추가

StartCode, DataLangth 값으로 데이터 처리 추가
