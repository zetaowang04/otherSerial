using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;

namespace 串口调试助手.Serial
{
    internal class SerialPortManager
    {

        //保存串口对象
        private readonly SerialPort _serialPort;

        //接收数据时通知界面
        public event Action<byte[]> DataReceived;

        public bool IsOpen
        {
            get { return _serialPort.IsOpen; }
        }

        public SerialPortManager()
        {
            _serialPort = new SerialPort();

            _serialPort.DataBits = 8;//数据位
            _serialPort.Parity = Parity.None;//奇偶校验位
            _serialPort.StopBits = StopBits.One;//停止位

            //读取跟写入超时时间
            _serialPort.ReadTimeout = 500;
            _serialPort.WriteTimeout = 500;

            //串口接收事件
            _serialPort.DataReceived += SerialPort_DataReceived;
        }

        public void Open(string portName,int baudRate)
        {
            if (_serialPort.IsOpen)
                return;

            _serialPort.PortName=portName;
            _serialPort.BaudRate=baudRate;

            _serialPort.Open();
        }

        public void Close()
        {
            if(!_serialPort.IsOpen) return;
            _serialPort.Close();
        }

        //清空串口接收缓冲区
        public void DiscardInput()
        {
            if (_serialPort.IsOpen) 
                _serialPort.DiscardInBuffer();
        }

        //发送字节数据
        public void Send(byte[] data)
        {
            if (!_serialPort.IsOpen)
                throw new InvalidOperationException("串口未打开");

            if (data == null || data.Length == 0)
                return;

            //从data的第0个字节开始发送全部数据
            _serialPort.Write(data,0,data.Length);
        }
            
        //串口接收事件：读取收到的字节
        private void SerialPort_DataReceived(object sender,SerialDataReceivedEventArgs e)
        {
            try
            {

                int count = _serialPort.BytesToRead;

                if (count <= 0)
                    return;

                //根据本次收到数据长度创建数组
                byte[] buffer = new byte[count];

                int bytesRead = _serialPort.Read(buffer, 0, count);

                if (bytesRead <= 0)
                    return;

                //实际读取的数据长度可能比分配的数组小，则重新调整数组大小
                if(bytesRead!=buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }

                //触发接收事件
                DataReceived?.Invoke(buffer);
            }

            catch(InvalidOperationException) 
            {

            }
            catch(TimeoutException)
            {

            }
        }

        public void Dispose()
        {
            if (_serialPort.IsOpen)
                _serialPort.Close();

            _serialPort.DataReceived-= SerialPort_DataReceived;
            _serialPort.Dispose();
        }
    }
}
