using System.IO.Ports;
using System.Text;
using 串口调试助手.Serial;
using System.Collections.Generic;
using 串口调试助手.Models;
using 串口调试助手.Protocol;
using System.Threading;
using System.Threading.Tasks;

namespace 串口调试助手
{
    public partial class Form1 : Form
    {
        private readonly SerialPortManager _serialPortManager;
        private readonly HSBT400Protocol _hsbt400Protocol = new HSBT400Protocol();

        //发送命令和接收筛选都使用界面上选择的地址。
        private byte DeviceAddress
        {
            get { return (byte)nubAddress.Value; }
        }

        //当前连接参数
        private string _connectedPortName = "";
        private int _connectedBaudRate;

        //用于取消0x07循环读取任务
        private CancellationTokenSource _readCts;
        private bool _isReading = false;

        //保存一个正在等待设备响应的异步任务
        private TaskCompletionSource<byte[]>? _pendingResponse;

        //当前的info应该有多少字节
        private int _expectedInfoLength;
        private bool _settingsBusy;
        private bool _closing;
        public Form1()
        {
            InitializeComponent();
            InitializeLoadSettings();
            //初始地址和范围沿用设计器设置，每次增减1。
            nubAddress.DecimalPlaces = 0;
            nubAddress.Increment = 1;

            //创建串口管理对象
            _serialPortManager = new SerialPortManager();

            //订阅接收数据事件
            _serialPortManager.DataReceived += OnDataReceived;

            //绑定窗体关闭事件
            this.FormClosing += Form1_FormClosing;

            //选择串口或者波特率尝试自动连接
            cboBaudRate.SelectedIndexChanged += ConnectionSetting_Changed;
            cboPortName.SelectedIndexChanged += ConnectionSetting_Changed;


        }

        private void Form1_Load(object sender, EventArgs e)
        {
            LoadSerialPorts();
            LoadBaudRates();

            cboBaudRate.SelectedIndex = 0;
            cboPortName.SelectedIndex = -1;

            lblConnectionStatus.Text = "连接状态：未连接";
            lblConnectionStatus.ForeColor = Color.Red;

        }

        //固定显示COM1～COM20，打开时再判断串口是否可用。
        private void LoadSerialPorts()
        {
            cboPortName.Items.Clear();
            cboPortName.DropDownStyle = ComboBoxStyle.DropDownList;
            for (int i = 1; i <= 20; i++)
            {
                cboPortName.Items.Add("COM" + i);
            }
        }

        //加载波特率
        private void LoadBaudRates()
        {
            cboBaudRate.Items.Clear();

            cboBaudRate.DropDownStyle = ComboBoxStyle.DropDownList;
            cboBaudRate.Items.AddRange(new object[] { "9600", "19200", "38400", "57600" });

        }

        private void ConnectionSetting_Changed(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(cboPortName.Text))
                return;
            if (!int.TryParse(cboBaudRate.Text, out int baudRate))
                return;

            string portName = cboPortName.Text;

            if (_serialPortManager.IsOpen && portName == _connectedPortName && baudRate == _connectedBaudRate)
                return;
            try
            {
                //如果已经连接完成则关闭原串口
                _readCts?.Cancel();

                //清理之前的协议接收缓存
                _hsbt400Protocol.ClearReceiveBuffer();
                if (_serialPortManager.IsOpen)
                    _serialPortManager.Close();

                _connectedBaudRate = 0;
                _connectedPortName = "";

                //打开新串口
                _serialPortManager.Open(portName, baudRate);

                //保存当前参数
                _connectedPortName = portName;
                _connectedBaudRate = baudRate;

                lblConnectionStatus.Text = "连接状态：已连接";
                lblConnectionStatus.ForeColor = Color.Green;

            }
            catch (Exception ex)
            {
                lblConnectionStatus.Text = "连接状态：连接失败";
                lblConnectionStatus.ForeColor = Color.Red;
                MessageBox.Show(
                    "串口连接失败：" + ex.Message,
                    "错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnDataReceived(byte[] data)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            //BeginInvoke把后面操作切换到ui线程
            BeginInvoke(new Action(() =>
            {
                //将本次字节加入协议缓存，并尝试解析完整响应
                HandleReceivedFrames(data);
            }));
        }

        //将解析结果显示到ui界面
        private void UpdateInstantStatus(DeviceStatus status)
        {
            //"F"表示保留小数点后
            txtVGrid.Text = status.VGrid.ToString("F2");
            txtIGrid.Text = status.IGrid.ToString("F2");
            txtMainVbus.Text = status.MainVbus.ToString("F2");
            txtTempHeatsink.Text = status.TempHeatsinkRaw.ToString();
            txtAux7V5.Text = status.Aux7V5.ToString("F2");
            txtIfan.Text = status.Ifan.ToString("F2");

            TextBox[] onlineBoxes =
            {
                txtMCUOnlineCH1, txtMCUOnlineCH2, txtMCUOnlineCH3, txtMCUOnlineCH4, txtMCUOnlineCH5, txtMCUOnlineCH6, txtMCUOnlineCH7,txtMCUOnlineCH8
            };
            TextBox[] vinBoxes =
            {
                txtVinCH1, txtVinCH2, txtVinCH3, txtVinCH4, txtVinCH5, txtVinCH6, txtVinCH7, txtVinCH8
            };
            TextBox[] vinStatusBoxes =
            {
                txtVinStatusCH1, txtVinStatusCH2, txtVinStatusCH3, txtVinStatusCH4, txtVinStatusCH5, txtVinStatusCH6, txtVinStatusCH7, txtVinStatusCH8
            };
            TextBox[] iinBoxes =
            {
                txtIinCUNCH1, txtIinCUNCH2, txtIinCUNCH3, txtIinCUNCH4, txtIinCUNCH5, txtIinCUNCH6, txtIinCUNCH7, txtIinCUNCH8
            };
            TextBox[] iHallSumBoxes =
            {
                txtIHallSumCH1, txtIHallSumCH2, txtIHallSumCH3, txtIHallSumCH4, txtIHallSumCH5, txtIHallSumCH6, txtIHallSumCH7, txtIHallSumCH8
            };
            TextBox[] vsoftBoxes =
            {
                txtVsoftCH1, txtVsoftCH2, txtVsoftCH3, txtVsoftCH4, txtVsoftCH5, txtVsoftCH6, txtVsoftCH7, txtVsoftCH8
            };
            foreach (ChannelStatus channel in status.Channels)
            {
                int index = channel.ChannelNumber - 1;
                if (index < 0 || index >= 8)
                    continue;

                onlineBoxes[index].Text = channel.MCUOnline ? "在线" : "离线";
                vinBoxes[index].Text = channel.Vin.ToString("F2");
                vinStatusBoxes[index].Text = "0x" + channel.DcdcStatusRaw.ToString("X4");
                iinBoxes[index].Text = channel.IinMCU.ToString("F4");
                iHallSumBoxes[index].Text = channel.IHallSum.ToString("F2");
                vsoftBoxes[index].Text = channel.Vsoft.ToString("F2");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            _readCts?.Cancel();
            _pendingResponse?.TrySetCanceled();
            _serialPortManager.DataReceived -= OnDataReceived;
            _serialPortManager.Dispose();
        }

        private async void btnRead_Click(object sender, EventArgs e)
        {
            if (_settingsBusy) return;
            if (_isReading)
            {
                _readCts?.Cancel();
                return;
            }


            if (!_serialPortManager.IsOpen)
            {
                MessageBox.Show("请先连接串口");
                return;
            }

            if (!int.TryParse(txtCycleTime.Text, out int interval) || interval < 600)
            {
                MessageBox.Show("循环间隔请输入不小于 600 的整数（毫秒），以容纳 9600 波特率的完整回包。");
                return;
            }

            //创建取消任务的对象
            _readCts = new CancellationTokenSource();
            _isReading = true;
            //循环读取期间锁定地址，避免请求和响应对应到不同设备。
            nubAddress.Enabled = false;
            btnRead.Text = "停止";

            try
            {
                //开始循环读取
                await ReadLoopAsync(interval, _readCts.Token);
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                MessageBox.Show("循环读取失败：" + ex.Message);
            }
            finally
            {
                _isReading = false;
                if (!_closing) nubAddress.Enabled = true;
                if (!_closing) btnRead.Text = "读取";

                _readCts?.Dispose();
                _readCts = null;
            }

        }

        //循环发送0x07命令
        private async Task ReadLoopAsync(int interval, CancellationToken token)
        {
            while (true)
            {
                //检查是否收到取消请求
                token.ThrowIfCancellationRequested();

                if (!_serialPortManager.IsOpen)
                {
                    throw new InvalidOperationException("串口已断开");
                }

                byte[] command = HSBT400Protocol.CreateReadAllChannelsCommand(DeviceAddress);

                _serialPortManager.Send(command);
                //等待指定时间，不阻塞ui线程
                await Task.Delay(interval, token);
            }
        }

        private bool CheckReadingStatus()
        {
            if (_isReading)
            {
                MessageBox.Show("当前正在循环读取", "操作提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private async void readReadSettings_Click(object sender, EventArgs e)
        {
            if (!CheckReadingStatus())
                return;
            await RunSettingsOperationAsync(false);
        }

        private async void btnSetSettings_Click(object sender, EventArgs e)
        {
            if (!CheckReadingStatus())
                return;

            await RunSettingsOperationAsync(true);
        }

        // 根据控件名称查找groupBox2中的控件。
        private Control SettingControl(string name)
        {
            Control[] controls = groupBox2.Controls.Find(name, true);
            if (controls.Length != 1)
            {
                throw new InvalidOperationException("找不到唯一的参数控件：" + name);
            }
            return controls[0];
        }

        // 初始化八个通道的模式、默认参数和事件。
        private void InitializeLoadSettings()
        {
            groupBox2.Text = "八通道负载参数（读取 0x06 / 设置 0x40，保存 E2ROM）";
            for (int channel = 1; channel <= 8; channel++)
            {
                ComboBox modeBox = (ComboBox)SettingControl("cboModeCH" + channel);
                modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                modeBox.Items.Clear();
                modeBox.Items.AddRange(LoadModeHelper.ModeNames);

                // Tag可以保存控件附带的信息，这里保存该下拉框所属的通道号。
                modeBox.Tag = channel;
                modeBox.SelectedIndexChanged += LoadMode_SelectedIndexChanged;
                modeBox.SelectionChangeCommitted += LoadMode_SelectionChangeCommitted;
                modeBox.SelectedIndex = 0;

                for (int parameter = 1; parameter <= 3; parameter++)
                {
                    TextBox input = (TextBox)SettingControl("txtParam" + parameter + "CH" + channel);
                    input.Tag = new int[] { channel, parameter };
                    input.Validated += LoadParameter_Validated;
                }
                ApplyModeDefaults(channel);
            }
        }

        // 模式发生变化时，更新参数名称和单位（包括从设备回读时改变模式）.
        private void LoadMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender == null)
            {
                return;
            }
            ComboBox modeBox = (ComboBox)sender;
            if (modeBox.Tag == null)
            {
                return;
            }
            int channel = (int)modeBox.Tag;
            UpdateParameterLabels(channel);
        }

        // 只有用户手动选择模式才填默认值，设备回读不会触发这个事件。
        private void LoadMode_SelectionChangeCommitted(object? sender, EventArgs e)
        {
            if (sender == null)
            {
                return;
            }
            ComboBox modeBox = (ComboBox)sender;
            if (modeBox.Tag == null)
            {
                return;
            }
            int channel = (int)modeBox.Tag;
            ApplyModeDefaults(channel);
        }

        // 输入结束后整理小数位数。无效输入先保留，点击设置时再显示具体错误。
        private void LoadParameter_Validated(object? sender, EventArgs e)
        {
            if (sender == null)
            {
                return;
            }
            TextBox input = (TextBox)sender;
            if (input.Tag == null)
            {
                return;
            }
            int[] position = (int[])input.Tag;
            int channel = position[0];
            int parameter = position[1];
            ComboBox modeBox = (ComboBox)SettingControl("cboModeCH" + channel);
            if (modeBox.SelectedIndex < 0)
            {
                return;
            }
            LoadParameterDefinition[] definitions = LoadParameterDefinition.ForMode(modeBox.SelectedIndex);
            FormatParameterInput(input, definitions[parameter - 1]);
        }

        // 将一个输入框中的有效数字，转换为规定的小数位格式。
        private void FormatParameterInput(TextBox input, LoadParameterDefinition definition)
        {
            try
            {
                uint raw = definition.Parse(input.Text.Trim());
                input.Text = definition.Format(raw);
            }
            catch (FormatException)
            {

            }
        }

        // 按当前模式填入该通道的三个默认值。
        private void ApplyModeDefaults(int channel)
        {
            ComboBox modeBox = (ComboBox)SettingControl("cboModeCH" + channel);
            int mode = modeBox.SelectedIndex;
            if (mode < 0)
            {
                return;
            }

            string[] values = LoadParameterDefinition.DefaultValuesForMode(mode);
            for (int parameter = 1; parameter <= 3; parameter++)
            {
                TextBox input = (TextBox)SettingControl("txtParam" + parameter + "CH" + channel);
                input.Text = values[parameter - 1];
            }
        }

        // 模式改变后，更新该通道三个参数的名称、单位和显示格式。
        private void UpdateParameterLabels(int channel)
        {
            ComboBox modeBox = (ComboBox)SettingControl("cboModeCH" + channel);
            int mode = modeBox.SelectedIndex;
            if (mode < 0)
            {
                return;
            }

            LoadParameterDefinition[] definitions = LoadParameterDefinition.ForMode(mode);
            for (int parameter = 1; parameter <= 3; parameter++)
            {
                Label label = (Label)SettingControl("lblParam" + parameter + "CH" + channel);
                Label unitLabel = (Label)SettingControl("lblUnit" + parameter + "CH" + channel);
                TextBox input = (TextBox)SettingControl("txtParam" + parameter + "CH" + channel);

                int neighbor;
                if (channel < 8)
                {
                    neighbor = channel + 1;
                }
                else
                {
                    neighbor = channel - 1;
                }
                TextBox adjacent = (TextBox)SettingControl("txtParam" + parameter + "CH" + neighbor);
                int width = Math.Abs(adjacent.Left - input.Left) - 4;

                label.Text = definitions[parameter - 1].Name;
                SetParameterLabelLayout(label, input, width);
                unitLabel.Text = definitions[parameter - 1].Unit;
                FormatParameterInput(input, definitions[parameter - 1]);
            }
        }

        // 标签只占一行，长名称逐步缩小字号，防止遮住上一行输入框。
        private void SetParameterLabelLayout(Label label, TextBox input, int width)
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleCenter;
            Font previousFont = label.Font;
            Font fittedFont = new Font(groupBox2.Font, groupBox2.Font.Style);

            while (TextRenderer.MeasureText(label.Text, fittedFont).Width > width && fittedFont.Size > 7f)
            {
                float fontSize = fittedFont.Size - 0.5f;
                if (fontSize < 7f)
                {
                    fontSize = 7f;
                }
                Font smallerFont = new Font(fittedFont.FontFamily, fontSize, fittedFont.Style);
                fittedFont.Dispose();
                fittedFont = smallerFont;
            }

            label.Font = fittedFont;
            // 标签Tag保存上次创建的字体，替换后释放它，避免重复切换模式积累资源。
            Font? ownedFont = label.Tag as Font;
            if (ownedFont != null && ReferenceEquals(previousFont, ownedFont))
            {
                ownedFont.Dispose();
            }
            label.Tag = fittedFont;
            int height = TextRenderer.MeasureText(label.Text, fittedFont).Height;
            label.SetBounds(input.Left - 4, input.Top - height - 2, width, height);
        }

        // 读取八个通道的输入，检查范围和精度，转换成准备发送的设置数据。
        private List<LoadSetting> GetLoadSettings()
        {
            List<LoadSetting> result = new List<LoadSetting>();
            for (int channel = 1; channel <= 8; channel++)
            {
                ComboBox modeBox = (ComboBox)SettingControl("cboModeCH" + channel);
                int mode = modeBox.SelectedIndex;
                LoadParameterDefinition[] definitions = LoadParameterDefinition.ForMode(mode);
                uint[] values = new uint[3];

                for (int parameter = 1; parameter <= 3; parameter++)
                {
                    TextBox input = (TextBox)SettingControl("txtParam" + parameter + "CH" + channel);
                    try
                    {
                        values[parameter - 1] = definitions[parameter - 1].Parse(input.Text.Trim());
                    }
                    catch (FormatException ex)
                    {
                        input.Focus();
                        throw new FormatException("CH" + channel + ": " + ex.Message);
                    }
                }

                LoadSetting setting = new LoadSetting();
                setting.ChannelNumber = channel;
                setting.Mode = (byte)mode;
                setting.VonRaw = (ushort)values[0];
                setting.ValueRaw = values[1];
                setting.ExtraRaw = values[2];
                result.Add(setting);
            }
            return result;
        }

        // 将设备回读的参数换算成实际单位，显示到对应通道。
        private void ShowLoadSettings(List<LoadSetting> settings)
        {
            foreach (LoadSetting setting in settings)
            {
                int channel = setting.ChannelNumber;
                ComboBox modeBox = (ComboBox)SettingControl("cboModeCH" + channel);
                modeBox.SelectedIndex = setting.Mode;

                LoadParameterDefinition[] definitions = LoadParameterDefinition.ForMode(setting.Mode);
                uint[] values = { setting.VonRaw, setting.ValueRaw, setting.ExtraRaw };
                for (int parameter = 1; parameter <= 3; parameter++)
                {
                    TextBox input = (TextBox)SettingControl("txtParam" + parameter + "CH" + channel);
                    input.Text = definitions[parameter - 1].Format(values[parameter - 1]);
                }
            }
        }

        // 串口收到的字节先拼成完整帧，再判断是实时状态还是当前等候的参数响应。
        private void HandleReceivedFrames(byte[] data)
        {
            if (_closing)
            {
                return;
            }

            string frameError;
            List<byte[]> frames = _hsbt400Protocol.AppendReceiveData(data, out frameError);
            // 校验失败的帧由协议层丢弃，残缺帧则留在缓存中等下一批字节。
            foreach (byte[] frame in frames)
            {
                if (frame[1] != DeviceAddress)
                {
                    continue;
                }

                if (frame[2] != 0xF0)
                {
                    string message = "";
                    if (frame[2] == 0xF3)
                    {
                        message = "设备返回 0xF3：CID 无效（当前设置命令为协议规定的 0x40）";
                    }
                    else if (frame[2] == 0xF4)
                    {
                        message = "设备返回 0xF4：无效数据";
                    }

                    // 只有设备明确返回的错误码才结束等待；0x40/0x41回显不是成功应答。
                    if (message != "" && _pendingResponse != null)
                    {
                        _pendingResponse.TrySetException(new InvalidOperationException(message));
                    }
                    continue;
                }

                if (frame.Length == 516)
                {
                    DeviceStatus status;
                    string statusError;
                    bool success = _hsbt400Protocol.TryParseReadAllChannelsResponse(frame, out status, out statusError);
                    if (success)
                    {
                        UpdateInstantStatus(status);
                    }
                }
                else if (_pendingResponse != null && frame.Length - 7 == _expectedInfoLength)
                {
                    // 前4字节是帧头/地址/返回码/长度，最后3字节是校验和及帧尾。
                    // 这里只复制中间的INFO数据，不使用数组范围简写。
                    byte[] info = new byte[frame.Length - 7];
                    Array.Copy(frame, 4, info, 0, info.Length);
                    _pendingResponse.TrySetResult(info);
                }
            }
        }

        // 发送一次命令并等待返回数据。info允许为空，读取命令不需要携带设置参数。
        private async Task<byte[]> ExchangeSettingsAsync(byte command, byte[]? info, int expectedLength)
        {
            _hsbt400Protocol.ClearReceiveBuffer();
            _serialPortManager.DiscardInput();
            _expectedInfoLength = expectedLength;

            // 用于把接收事件与当前等待任务连接起来。
            TaskCompletionSource<byte[]> completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingResponse = completion;
            byte[] frame = HSBT400Protocol.CreateCommand(DeviceAddress, command, info);
            try
            {
                _serialPortManager.Send(frame);
                // 异步等待最多3秒，期间界面仍能响应操作，不能用Thread.Sleep替换。
                byte[] response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
                return response;
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("命令 0x" + command.ToString("X2")
                    + " 等待响应超时。请检查连接和设备地址；设置超时不代表设备未执行，请先读取核实。");
            }
            finally
            {
                _pendingResponse = null;
            }
        }

        // 请求期间关闭相关操作，防止同时发送不同请求，完成后再恢复。
        private void SetSettingsControlsEnabled(bool enabled)
        {
            groupBox2.Enabled = enabled;
            btnRead.Enabled = enabled;
            cboPortName.Enabled = enabled;
            cboBaudRate.Enabled = enabled;
            nubAddress.Enabled = enabled;
        }

        // 逐字节比较设置内容与设备回读内容，代替SequenceEqual简写。
        private bool AreSettingsEqual(byte[] expected, byte[] actual)
        {
            if (expected.Length != actual.Length)
            {
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                {
                    return false;
                }
            }
            return true;
        }

        // write为true表示设置并回读，为false表示只读取。
        private async Task RunSettingsOperationAsync(bool write)
        {
            if (_settingsBusy || _closing)
            {
                return;
            }
            if (!_serialPortManager.IsOpen)
            {
                MessageBox.Show("请先连接串口");
                return;
            }

            byte[] expected = new byte[0];
            try
            {
                if (write)
                {
                    List<LoadSetting> settings = GetLoadSettings();
                    expected = HSBT400Protocol.EncodeLoadSettings(settings);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "参数错误");
                return;
            }

            _settingsBusy = true;
            SetSettingsControlsEnabled(false);
            bool acknowledged = false;
            try
            {
                if (write)
                {
                    await ExchangeSettingsAsync(HSBT400Protocol.CMD_SET_ALL_LOAD_SETTINGS, expected, 0);
                    acknowledged = true;
                }

                // 设置得到成功应答后，还要回读确认；只读取时直接执行这一步。
                byte[] actual = await ExchangeSettingsAsync(HSBT400Protocol.CMD_READ_SETTINGS, null, 88);
                List<LoadSetting> settings = HSBT400Protocol.ParseLoadSettings(actual);
                ShowLoadSettings(settings);
                if (write && !AreSettingsEqual(expected, actual))
                {
                    throw new InvalidOperationException("设备已应答，但回读参数与发送值不一致；界面已显示设备实际值，请核对参数。");
                }

                if (!_closing)
                {
                    if (write)
                    {
                        MessageBox.Show("设置成功，八通道回读参数一致。", "负载参数");
                    }
                    else
                    {
                        MessageBox.Show("八通道参数读取成功。", "负载参数");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_closing)
                {
                    string message = ex.Message;
                    if (acknowledged)
                    {
                        message = "设置已收到成功应答，回读核对未完成：" + message;
                    }
                    MessageBox.Show(message, "负载参数", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _settingsBusy = false;
                if (!_closing)
                {
                    SetSettingsControlsEnabled(true);
                }
            }
        }
    }
}
