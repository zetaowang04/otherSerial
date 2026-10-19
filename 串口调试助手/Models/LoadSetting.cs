using System.Globalization;

namespace 串口调试助手.Models;

internal class LoadSetting
{
    public int ChannelNumber { get; set; }

    //工作模式：0-9
    public byte Mode { get; set; }

    public ushort VonRaw { get; set; }

    public uint ValueRaw { get; set; }

    public uint ExtraRaw { get; set; }
}


//集中管理设备协议的模式编号，以及界面下拉框显示的模式名称
internal class LoadModeHelper
{
    public const byte CC_Slow = 0;
    public const byte CV = 1;
    public const byte CP = 2;
    public const byte CR = 3;
    public const byte CC_Fast = 4;
    public const byte LED = 5;
    public const byte BatDischarge = 6;
    public const byte Power = 7;
    public const byte BatChrgCCCV= 8;
    public const byte BatChrgCC= 9;

    public static readonly string[] ModeNames =
    {
        "CC_Slow",
        "CV",
        "CP",
        "CR",
        "CC_Fast",
        "LED",
        "BatDischarge",
        "Power",
        "BatChrgCCCV",
        "BatChrgCC"
    };
}

// 描述一个参数的名称、单位、协议倍率和原始整数上限。
internal class LoadParameterDefinition
{
    public string Name { get; private set; }
    public string Unit { get; private set; }
    public decimal Scale { get; private set; }
    public uint MaxRaw { get; private set; }

    //构造函数：创建一个参数定义
    public LoadParameterDefinition(string name, string unit, decimal scale, uint maxRaw)
    {
        Name = name;
        Unit = unit;
        Scale = scale;
        MaxRaw = maxRaw;
    }

    // 返回三个默认显示值，分别填入界面上的第1、2、3个参数框。
    public static string[] DefaultValuesForMode(int mode)
    {
        decimal[] values;

        // m表示decimal小数；多个case放在一起表示使用相同的默认值。
        switch (mode)
        {
            case LoadModeHelper.CC_Slow:
            case LoadModeHelper.CC_Fast:
                values = new decimal[] { 10m, 0.5m, 0m };
                break;
            case LoadModeHelper.CV:
                values = new decimal[] { 5m, 20m, 30m };
                break;
            case LoadModeHelper.CP:
                values = new decimal[] { 5m, 10m, 0m };
                break;
            case LoadModeHelper.CR:
                values = new decimal[] { 5m, 100m, 0m };
                break;
            case LoadModeHelper.LED:
                values = new decimal[] { 5m, 10m, 0.5m };
                break;
            case LoadModeHelper.BatDischarge:
            case LoadModeHelper.BatChrgCCCV:
            case LoadModeHelper.BatChrgCC:
                values = new decimal[] { 5m, 0.5m, 12.05m };
                break;
            case LoadModeHelper.Power:
                values = new decimal[] { 30m, 5m, 30m };
                break;
            default:
                throw new ArgumentOutOfRangeException("mode", "工作模式必须为0～9");
        }

        LoadParameterDefinition[] definitions = ForMode(mode);

        string[] texts = new string[3];

        for (int i = 0; i < 3; i++)
        {
            //从values中获取默认值，转换为字符串。
            string text = values[i].ToString(CultureInfo.InvariantCulture);
            uint raw = definitions[i].Parse(text);
            texts[i] = definitions[i].Format(raw);
        }
        return texts;
    }

    // 按模式确定三个参数分别代表什么，以及发送时要乘多少倍。
    public static LoadParameterDefinition[] ForMode(int mode)
    {
        LoadParameterDefinition first;
        LoadParameterDefinition second;
        LoadParameterDefinition third;

        // 第一个参数：Power为限流，其他模式为启动电压Von，倍率都是100。
        if (mode == LoadModeHelper.Power)
        {
            first = new LoadParameterDefinition("I Limit Power", "A", 100, ushort.MaxValue);
        }
        else
        {
            first = new LoadParameterDefinition("Von", "V", 100, ushort.MaxValue);
        }

        // 第二个参数：电压/电流乘10000，功率/电阻乘100。
        switch (mode)
        {
            case LoadModeHelper.CV:
            case LoadModeHelper.LED:
            case LoadModeHelper.Power:
                second = new LoadParameterDefinition("V Set", "V", 10000, uint.MaxValue);
                break;
            case LoadModeHelper.CP:
                second = new LoadParameterDefinition("P Set", "W", 100, uint.MaxValue);
                break;
            case LoadModeHelper.CR:
                second = new LoadParameterDefinition("R Set", "Ω", 100, uint.MaxValue);
                break;
            default:
                // 模式9也按充电电流乘10000处理；协议未单列该倍率，仍需固件确认。
                second = new LoadParameterDefinition("I Set", "A", 10000, uint.MaxValue);
                break;
        }

        // 第三个参数：延迟、限流或电压，按工作模式分别设置。
        switch (mode)
        {
            case LoadModeHelper.CC_Slow:
            case LoadModeHelper.CP:
            case LoadModeHelper.CR:
            case LoadModeHelper.CC_Fast:
                third = new LoadParameterDefinition("DelayTime", "s", 100, uint.MaxValue);
                break;
            case LoadModeHelper.CV:
                third = new LoadParameterDefinition("Current Limit", "A", 100, uint.MaxValue);
                break;
            case LoadModeHelper.Power:
                third = new LoadParameterDefinition("I Limit Load", "A", 100, uint.MaxValue);
                break;
            case LoadModeHelper.LED:
                third = new LoadParameterDefinition("I Set", "A", 10000, uint.MaxValue);
                break;
            default:
                // 电池模式：模式8是充电电压，模式6和9是终止电压。
                third = new LoadParameterDefinition("Terminate", "V", 10000, uint.MaxValue);
                break;
        }

        LoadParameterDefinition[] definitions = { first, second, third };
        return definitions;
    }

    // 输入框文本转成协议整数。例如0.5A乘10000得到5000。
    public uint Parse(string text)
    {
        decimal value;
        bool success = decimal.TryParse(text,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);

        // InvariantCulture固定用英文小数点；检查是否是数字、是否为负、是否超出范围。
        if (!success || value < 0 || value > MaxRaw / Scale)
        {
            throw new FormatException(Name + " 必须为 0～" + (MaxRaw / Scale)
                + " " + Unit + " 的数值（小数点用 .）");
        }

        decimal raw = value * Scale;
        // 协议传整数，不能丢掉小数部分。例如倍率100时不允许输入1.2345。
        if (raw != decimal.Truncate(raw))
        {
            throw new FormatException(Name + " 最小步进为 " + (1 / Scale) + " " + Unit);
        }
        return (uint)raw;
    }

    // 原始整数除以倍率，时间显示2位小数，其他显示4位小数。
    public string Format(uint raw)
    {
        decimal value = raw / Scale;
        if (Unit == "s")
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
        else
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}
