using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace 串口调试助手.Models
{
    //设备整体状态，保存全局数据和8个通道数据
    internal class DeviceStatus
    {
        public List<ChannelStatus> Channels { get; set; }
        = new List<ChannelStatus> { };

        //=====================全局数据=====================

        //电网电压：原始值/0.9625
        public ushort VGridRaw { get; set; }
        public double VGrid => VGridRaw / 0.9625;
       

        //并网电流：原始值/12.4
        public short IGridRaw { get; set; }
        public double IGrid => IGridRaw / 12.4;

        //母线电压：原始值/7.695
        public ushort MainVbusRaw { get; set; }
        public double MainVbus => MainVbusRaw / 7.695;
      
        //逆变散热器温度：原始值
        public byte TempHeatsinkRaw { get; set; }

        //逆变辅源电压：原始值/7.35
        public byte Aux7V5Raw { get; set; }
        public double Aux7V5 => Aux7V5Raw / 7.35;

        //风扇电流：原始值/115
        public byte IfanRaw { get; set; }
        public double Ifan => IfanRaw/ 115.0;
    }

    // 单个通道的实时状态;DeviceStatus.Channels集合中的每一项都是这种对象。
    internal class ChannelStatus
    {
        // 通道号1～8，界面用它找到对应的CH1～CH8文本框。
        public int ChannelNumber { get; set; }

        //MCU在线标志
        public byte MCUOnlineRaw { get; set; }
        public bool MCUOnline => MCUOnlineRaw != 0;

        //Load线电压:原始值/100
        public short VinRaw { get; set; }
        public double Vin => VinRaw / 100.0;

        // MCU电流：有符号原始值/10000.
        public int IinMCURaw { get; set; }
        public double IinMCU => IinMCURaw / 10000.0;

        // DCDC状态字，暂时用于显示到VinStatus文本框。
        public ushort DcdcStatusRaw { get; set; }

        //两支路电感电流之和
        public short IHallSumRaw { get; set; }
        public double IHallSum => IHallSumRaw / 100.0;

        //软启动电压：原始值/20
        public ushort VsoftRaw { get; set; }
        public double Vsoft => VsoftRaw / 20.0;

    }
}
