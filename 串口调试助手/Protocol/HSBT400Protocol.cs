using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using 串口调试助手.Models;

namespace 串口调试助手.Protocol
{
    // 统一处理HSBT400命令生成、接收拆帧和数据解析；串口收发仍由Serial负责。
    internal class HSBT400Protocol
    {
        // 帧头、帧尾和设备正常返回码。
        private const byte SOI = 0xEE;
        private const byte EOI = 0xEF;
        private const byte RTN_OK = 0xF0;

        //读取八通道负载参数
        public const byte CMD_READ_SETTINGS = 0x06;

        //读取八通道实时数据
        public const byte CMD_READ_ALL_CHANNELS = 0x07;

        //设置全部八通道负载参数
        public const byte CMD_SET_ALL_LOAD_SETTINGS = 0x40;

        //总共8通道
        private const int CHANNEL_COUNT = 8;

        //0x07数据长度
        private const int GLOBAL_INFO_LENGTH = 13;
        private const int CHANNEL_INFO_LENGTH = 62;
        private const int INFO_LENGTH = GLOBAL_INFO_LENGTH + CHANNEL_COUNT * CHANNEL_INFO_LENGTH;
        private const int RESPONSE_FRAME_LENGTH = INFO_LENGTH + 7;

        //0x06和0x40数据长度
        private const int LOAD_CHANNEL_INFO_LENGTH = 11;
        private const int LOAD_SETTING_INFO_LENGTH = CHANNEL_COUNT * LOAD_CHANNEL_INFO_LENGTH;

        // 接收缓存
        private readonly List<byte> _receiveBuffer = new List<byte>();

        // 沿用现有设备地址0x01；协议出厂默认地址为十进制77，需按实际设备传入。
        public static byte[] CreateReadAllChannelsCommand(byte address = 0x01)
        {
            return CreateCommand(address, CMD_READ_ALL_CHANNELS);
        }

        //生成0x06读取负载参数命令
        public static byte[] CreateReadSettingsCommand(byte address = 0x01)
        {
            return CreateCommand(address, CMD_READ_SETTINGS);
        }

        //生成0x40设置八通道负载参数命令
        public static byte[] CreateSetAllLoadSettingsCommand(IReadOnlyList<LoadSetting> settings, byte address = 0x01)
        {
            return CreateCommand(address, CMD_SET_ALL_LOAD_SETTINGS, EncodeLoadSettings(settings));
        }

        // 通用组帧：帧头、地址、命令、长度、INFO、校验和、帧尾。
        public static byte[] CreateCommand(byte address, byte command, byte[]? info = null)
        {
            if (address is 0 or > 127) 
                throw new ArgumentOutOfRangeException(nameof(address));
            if (info == null)
                info = new byte[0];
            var frame = new byte[info.Length + 7];
            frame[0] = SOI;
            frame[1] = address;
            frame[2] = command;
            frame[3] = unchecked((byte)(info.Length + 3));

            //将info拷贝到完整帧中
            info.CopyTo(frame, 4);

            //大端序：高位字节在前
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(frame.Length - 3), CalculateChecksum(frame, 1, frame.Length - 4));
            frame[frame.Length - 1] = EOI;
            return frame;
        }
    
        //清空协议层缓存
        public void ClearReceiveBuffer()
        {
            _receiveBuffer.Clear();
        }

        //将新收到的字节加入缓存，尝试提取完整数据帧
        public List<byte[]> AppendReceiveData(byte[] data, out string error)
        {
            error = "";
            //将新数据加入到缓存
            _receiveBuffer.AddRange(data);
            //保存本次成功解析的完整帧
            var frames = new List<byte[]>();

            //缓存中还有数据时进行处理，先查找有效帧头
            while (_receiveBuffer.Count > 0)
            {
                int start = _receiveBuffer.IndexOf(SOI);
                //查找不到帧头，缓存没有有效帧起点
                if (start < 0) 
                {
                    _receiveBuffer.Clear();
                    break; 
                }

                //丢弃帧头前无关数据
                if (start > 0) 
                    _receiveBuffer.RemoveRange(0, start);
                if (_receiveBuffer.Count < 4) 
                    break;
                // 0x07 的 INFO 为 509 字节，LENGTH=(509+3)%256=0。
                int length = _receiveBuffer[3] == 0 ? RESPONSE_FRAME_LENGTH : _receiveBuffer[3] + 4;
                if (length < 7) 
                { 
                    _receiveBuffer.RemoveAt(0); error = "无效 LENGTH";
                    continue; 
                }
                if (_receiveBuffer.Count < length) 
                    break;
                
                var frame = _receiveBuffer.GetRange(0, length).ToArray();

                //检查帧尾和校验和
                if (frame[frame.Length-1] != EOI || BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(length - 3)) != CalculateChecksum(frame, 1, frame.Length - 4))
                {
                    error = "收到帧尾或校验和错误的数据，已丢弃并重新同步";
                    //丢弃第一个字节，继续查找帧头
                    _receiveBuffer.RemoveAt(0);
                    continue;
                }

                //从缓存中移除已经处理的完整帧，然后保存
                _receiveBuffer.RemoveRange(0, length);
                frames.Add(frame);
            }
            return frames;
        }

        //负载参数编解码
        public static byte[] EncodeLoadSettings(IReadOnlyList<LoadSetting> settings)
        {
            if (settings.Count != CHANNEL_COUNT) 
                throw new ArgumentException("必须包含 8 个通道参数");

            var info = new byte[LOAD_SETTING_INFO_LENGTH];
            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                //获取当前通道设置
                var s = settings[i];

                if (s.ChannelNumber != i + 1 || s.Mode > 9)
                    throw new ArgumentException("通道必须按 1～8 排列，模式必须为 0～9");
                
                //计算当前通道在info中的起始位置
                int offset = i * LOAD_CHANNEL_INFO_LENGTH;

                info[offset] = s.Mode;
                BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(offset + 1), s.VonRaw);
                BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(offset + 3), s.ValueRaw);
                BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(offset + 7), s.ExtraRaw);
            }
            return info;
        }
    
        //将88字节info转换为8个LoadSetting对象
        public static List<LoadSetting> ParseLoadSettings(byte[] info)
        {
            if (info.Length != LOAD_SETTING_INFO_LENGTH) 
                throw new FormatException("八通道参数应为 88 字节（8 × 11）");
            var settings = new List<LoadSetting>();
            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                //计算当前通道起始位置
                int offset = i * LOAD_CHANNEL_INFO_LENGTH;
                if (info[offset] > 9) 
                    throw new FormatException($"CH{i + 1} 返回未知模式 0x{info[offset]:X2}");

                //读取当前通道的模式和3个参数
                settings.Add(new LoadSetting
                {
                    ChannelNumber = i + 1, Mode = info[offset],
                    VonRaw = BinaryPrimitives.ReadUInt16BigEndian(info.AsSpan(offset + 1)),
                    ValueRaw = BinaryPrimitives.ReadUInt32BigEndian(info.AsSpan(offset + 3)),
                    ExtraRaw = BinaryPrimitives.ReadUInt32BigEndian(info.AsSpan(offset + 7))
                });
            }
            return settings;
        }

        //实时数据解析与公共校验、字节转换
        //解析完整的0x07正常响应帧
        internal bool TryParseReadAllChannelsResponse(byte[] frame, out DeviceStatus status, out string error)
        {
            status = null;
            error = "";

            if (frame.Length != RESPONSE_FRAME_LENGTH)
            {
                error = "响应帧长度不正确";
                return false;
            }

            if (frame[0] != SOI || frame[frame.Length - 1] != EOI)
            {
                error = "帧头或帧尾不正确";
                return false;
            }

            //返回帧的CID是RTN
            if (frame[2] != RTN_OK)
            {
                error = "设备返回错误码：0x" + frame[2].ToString("X2");
                return false;
            }

            //根据文档中INFO+3，单字节溢出的规则
            //509+3=512，溢出后LENGTH为0
            byte expectedLength = unchecked((byte)(INFO_LENGTH + 3));

            if (frame[3] != expectedLength)
            {
                error = "LENGTH与当前采用的0x07帧格式不一致";
                return false;
            }
            //校验和所在位置
            int checksumIndex = frame.Length - 3;

            //读取校验和高字节跟低字节，高字节左移8位再组合
            ushort receivedChecksum = (ushort)((frame[checksumIndex] << 8) | frame[checksumIndex + 1]);
            //重新计算校验和
            ushort calculatedChecksum = CalculateChecksum(frame, 1, checksumIndex - 1);

            if (receivedChecksum != calculatedChecksum)
            {
                error = "校验和错误";
                return false;
            }

            //解析
            status = ParseChannels(frame);
            return true;
        }

        //解析全局信息后8个通道
        private DeviceStatus ParseChannels(byte[] frame)
        {
            DeviceStatus deviceStatus= new DeviceStatus();

            //INFO起始位置为frame[4]
            //全局数据
            deviceStatus.VGridRaw = ReadUInt16(frame, 4);
            deviceStatus.IGridRaw=ReadInt16(frame, 6);
            deviceStatus.MainVbusRaw = ReadUInt16(frame, 8);
            deviceStatus.TempHeatsinkRaw = frame[15];
            deviceStatus.Aux7V5Raw=frame[11];
            deviceStatus.IfanRaw = frame[14];


            //第1通道在完整帧中起始位置为4+13=17
            for(int i=0;i<CHANNEL_COUNT;i++)
            {
                int channelStart = 4 + GLOBAL_INFO_LENGTH + i * CHANNEL_INFO_LENGTH;

                ChannelStatus channel = new ChannelStatus();

                channel.ChannelNumber = i + 1;

                //通道内偏移0-1：Load线电压。1V=100
                channel.VinRaw=ReadInt16(frame, channelStart);

                //通道内偏移2-5：有符号电流，1A=10000
                channel.IinMCURaw=ReadInt32(frame, channelStart+2);

                //通道内偏移8：MCU在线标志
                channel.MCUOnlineRaw= frame[channelStart+8];

                //通道内偏移9-10：DCDC状态字
                channel.DcdcStatusRaw = ReadUInt16(frame, channelStart + 9);

                //通道内偏移12-13，两支路电感电流之和
                channel.IHallSumRaw=ReadInt16(frame, channelStart + 12);

                //通道内偏移48
                channel.VsoftRaw=ReadUInt16(frame, channelStart + 48);

                deviceStatus.Channels.Add(channel);
            }
            return deviceStatus;
        }

        //按大端序读取无符号16位数
        private static ushort ReadUInt16(byte[] data,int offset)
        {
            return (ushort)(data[offset] << 8 | data[offset + 1]);
        }

        //按大端序读取有符号16位数
        private static short ReadInt16(byte[] data,int offset)
        {
            return unchecked((short)ReadUInt16(data, offset));
        }

        //按大端序读取有符号32位数
        private static int ReadInt32(byte[] data,int offset)
        {
            uint value = ((uint)data[offset] << 24)|((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset+3];
            return unchecked((int)value);
        }

        //累加指定范围内字节，保留低16位
        private static ushort CalculateChecksum(byte[] data, int startIndex, int endIndex)
        {
            uint sum = 0;
            for(int i = startIndex; i <= endIndex; i++)
                sum+= data[i];
            return (ushort)(sum & 0xFFFF);
        }
    }
}

