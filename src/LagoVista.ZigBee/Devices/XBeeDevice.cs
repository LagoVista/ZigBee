using LagoVista.Core.ServiceCommon;
using LagoVista.ZigBee.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LagoVista.ZigBee.Devices
{

    public abstract class XBeeDevice : ServiceBase, IDisposable
    {
        private class ATSettingValue
        {
            public String ATCommand { get; set; }
            public byte[] Payload { get; set; }
        }

        IZigBeeComms _comms;

        TaskCompletionSource<bool> _deviceQueryCompletionSource;

        TaskCompletionSource<bool> _deviceSendProfileCompletionSource;

        TaskCompletionSource<bool> _saveParametersCompletionSource;

        TaskCompletionSource<bool> _resetCompletionSource;
        TaskCompletionSource<bool> _disassociateCompletionSource;
        TaskCompletionSource<bool> _isConnectedCompletionSource;

        public event EventHandler<bool> ZigbeeJoinStatusChanged;

        public event EventHandler<States> DeviceStateChanged;

        private Queue<String> _atSettingsReadQueue = new Queue<string>();

        private Queue<ATSettingValue> _atSettingsWriteQueue = new Queue<ATSettingValue>();

        private Dictionary<string, byte[]> _deviceATSettings = new Dictionary<string, byte[]>();

        private Dictionary<string, byte[]> _deviceProfile = new Dictionary<string, byte[]>();


        public enum States
        {
            Offline,
            Initializing,
            Ready,
            Disposed,
        }

        States _currentState = States.Offline;

        public XBeeDevice(IZigBeeComms comms)
        {
            CreateXBeeProvision();
            _comms = comms;
            _comms.FrameReady += _comms_FrameReady;
        }

        public abstract void HandleFrame(Frame frame);

        public void AddSettingToQuery(String atSetting)
        {
            if (atSetting == null || atSetting.Length != 2)
                throw new Exception("AT Commands must be not null and exactly two characters.");

            atSetting = atSetting.ToUpper();

            if (!_atSettingsReadQueue.Contains(atSetting))
                _atSettingsReadQueue.Enqueue(atSetting);

            foreach (var atCmd in _deviceATSettings)
                LogMessage(atCmd.ToString());
        }

        public void AddSettingsToQuery(List<string> atParams)
        {
            _atSettingsReadQueue.Clear();

            foreach (var atParam in atParams)
                AddSettingToQuery(atParam);
        }

        public void BeginQuery(TimeSpan? timeout = null)
        {
            if (timeout == null)
                timeout = TimeSpan.FromMilliseconds(1500);

            Task.Run(async () =>
            {
                await Task.Delay(timeout.Value);
                lock (this)
                {
                    if (_deviceQueryCompletionSource != null)
                    {
                        Core.PlatformSupport.Services.Logger.LogException("BeginQuery", new Exception("Timeout reading parameter"));
                        _deviceQueryCompletionSource.SetResult(false);
                        _deviceQueryCompletionSource = null;
                    }
                }
            });


            if (_atSettingsReadQueue.Any())
            {
                var atSetting = _atSettingsReadQueue.Dequeue();
                _comms.SendATFrame(atSetting);
            }
            else
            {
                lock (this)
                {
                    _deviceQueryCompletionSource.SetResult(true);
                    _deviceQueryCompletionSource = null;
                }
            }
        }

        private String FormatByteArray(byte[] bytes)
        {
            var bldr = new StringBuilder();

            foreach (var ch in bytes)
                bldr.AppendFormat("{0:X2} ", ch);

            return bldr.ToString();
        }

        public void LogMessage(string message, params object[] args)
        {
            if (_comms.ShowDiagnostics)
                Core.PlatformSupport.Services.Logger.Log(Core.PlatformSupport.LogLevel.Message, "ZigbeeDevice", String.Format(message, args));
        }

        private void HandleATReadFrame(Frame frame)
        {
            if (_deviceATSettings.ContainsKey(frame.ATCommandString))
                _deviceATSettings.Remove(frame.ATCommandString);

            if (frame.ATCommandString == "AI")
            {
                IsJoinedToNetwork = frame.Payload[0] == 0x00;
                if (_isConnectedCompletionSource != null)
                {
                    _isConnectedCompletionSource.SetResult(IsJoinedToNetwork);
                    _isConnectedCompletionSource = null;
                }
                return;
            }

            /* This scenario we requested that the payload be read */
            _deviceATSettings.Add(frame.ATCommandString, frame.Payload);

            if (_atSettingsReadQueue.Any())
            {
                lock (this)
                {
                    var atSetting = _atSettingsReadQueue.Dequeue();
                    _comms.SendATFrame(atSetting);
                }
            }
            else
            {
                lock (this)
                {
                    if (_currentState != States.Ready)
                    {
                        _currentState = States.Ready;
                        if (DeviceStateChanged != null)
                            DeviceStateChanged(this, States.Ready);
                    }

                    LogMessage("\r\n--------------------------------------------------------");
                    LogMessage("Device Settings");
                    foreach (var setting in _deviceATSettings)
                        LogMessage(String.Format("AT SETTING : {0}    VALUE:  {1}", setting.Key, FormatByteArray(setting.Value)));

                    LogMessage("\r\n--------------------------------------------------------\r\n");

                    if (_deviceQueryCompletionSource != null)
                    {
                        _deviceQueryCompletionSource.SetResult(true);
                        _deviceQueryCompletionSource = null;
                    }
                }
            }
        }

        private void HandleATWriteFrame(Frame frame)
        {
            if (frame.ATCommand == Convert.ToUInt16(((ushort)'D') << 8 | (ushort)'A'))
            {
                if (_disassociateCompletionSource != null)
                {
                    _disassociateCompletionSource.SetResult(true);
                    _disassociateCompletionSource = null;
                }
            }
            if (_atSettingsWriteQueue.Any())
            {
                lock (this)
                {
                    var atSetting = _atSettingsWriteQueue.Dequeue();
                    _comms.SendATFrame(atSetting.ATCommand, atSetting.Payload);
                }
            }
            else
            {
                lock (this)
                {
                    if (_deviceSendProfileCompletionSource != null)
                    {
                        _deviceSendProfileCompletionSource.SetResult(true);
                        _deviceSendProfileCompletionSource = null;
                    }
                }
            }
        }

        private void HandleWriteEPROMFrame(Frame frame)
        {
            lock (this)
            {
                if (frame.ATCommandStatus == Frame.ATCommandStatuss.OK)
                {
                    if (_saveParametersCompletionSource != null)
                    {
                        _saveParametersCompletionSource.SetResult(true);
                        _saveParametersCompletionSource = null;
                    }
                }
                else
                {
                    if (_saveParametersCompletionSource != null)
                    {
                        _saveParametersCompletionSource.SetResult(false);
                        _saveParametersCompletionSource = null;
                    }
                }
            }
        }

        private const byte LOCAL_ENDPOINT = 0x01;

        private async void _comms_FrameReady(object sender, Frame frame)
        {
            if (_currentState == States.Disposed)
                return;

            if (frame.FrameType == Frame.FrameTypes.RxAtCommandResponse)
            {
                if (frame.ATCommandString == "WR")
                    HandleWriteEPROMFrame(frame);
                else if (frame.Payload != null && frame.Payload.Length > 0)
                    HandleATReadFrame(frame);
                else
                    HandleATWriteFrame(frame);
            }
            else if (frame.FrameType == Frame.FrameTypes.RxReceivePacket)
            {
                if (frame.ClusterId == 0x0005)
                {
                    var endpointFrame = new Frame();
                    endpointFrame.FrameType = Frame.FrameTypes.TxExplicitAddressCommand;
                    endpointFrame.ClusterId = Convert.ToUInt16(0x8000 | frame.ClusterId);

                    endpointFrame.DestinationAddress64Bit = 0x00000000;
                    endpointFrame.DestinationAddress16Bit = 0x0000;
                    endpointFrame.SourceEndPoint = 0x00;
                    endpointFrame.DestinationEndPoint = 0x00;

                    endpointFrame.Payload = new byte[6];
                    endpointFrame.Payload[0] = frame.Payload[0];
                    endpointFrame.Payload[1] = 0x00; /* OK */
                    endpointFrame.Payload[2] = frame.Payload[1];
                    endpointFrame.Payload[3] = frame.Payload[2];
                    endpointFrame.Payload[4] = 1; /* Number of end points */
                    endpointFrame.Payload[5] = LOCAL_ENDPOINT; /* Address of the end point */

                    await _comms.SendFrameAsync(endpointFrame);
                }

                else if (frame.ClusterId == 0x0004)
                {
                    var endpointFrame = new Frame();
                    endpointFrame.FrameType = Frame.FrameTypes.TxExplicitAddressCommand;
                    endpointFrame.ClusterId = Convert.ToUInt16(0x8000 | frame.ClusterId);

                    endpointFrame.DestinationAddress64Bit = 0x00000000;
                    endpointFrame.DestinationAddress16Bit = 0x0000;
                    endpointFrame.SourceEndPoint = 0x00;
                    endpointFrame.DestinationEndPoint = 0x00;

                    var inClusters = GetInClusters();

                    endpointFrame.Payload = new byte[13 + (inClusters.Count * 2)];
                    endpointFrame.Payload[0] = frame.Payload[0];
                    endpointFrame.Payload[1] = 0x00; /* OK */
                    endpointFrame.Payload[2] = frame.Payload[1];
                    endpointFrame.Payload[3] = frame.Payload[2];

                    endpointFrame.Payload[4] = Convert.ToByte(endpointFrame.Payload.Length - 5); /* Bytes Sent */

                    endpointFrame.Payload[5] = frame.Payload[3];

                    endpointFrame.Payload[6] = 0x04; /* HA Client */
                    endpointFrame.Payload[7] = 0x01;

                    endpointFrame.Payload[8] = 0x02; /* On/Off Output */
                    endpointFrame.Payload[9] = 0x00;

                    endpointFrame.Payload[10] = 0x30; /* Version */

                    endpointFrame.Payload[11] = Convert.ToByte(inClusters.Count); /* Number of Cluster Types We Accept */

                    var payloadIndex = 12;
                    foreach (var inCluster in inClusters)
                    {
                        endpointFrame.Payload[payloadIndex++] = Convert.ToByte(inCluster & 0xFF); /* Cluster Type => Basic */
                        endpointFrame.Payload[payloadIndex++] = Convert.ToByte(inCluster >> 8);
                    }

                    endpointFrame.Payload[payloadIndex] = 0x00;

                    await _comms.SendFrameAsync(endpointFrame);
                }
                else if (frame.DestinationEndPoint == LOCAL_ENDPOINT && frame.ProfileId == 0x0104)
                {
                    var myAddr = BitConverter.ToUInt16(_deviceATSettings["MY"], 0);
                    var endpointFrame = new Frame();
                    endpointFrame.FrameType = Frame.FrameTypes.TxExplicitAddressCommand;
                    //endpointFrame.ClusterId = Convert.ToUInt16(0x8000 | frame.ClusterId);
                    endpointFrame.ClusterId = frame.ClusterId;
                    endpointFrame.DestinationAddress64Bit = 0x00000000;
                    endpointFrame.DestinationAddress16Bit = 0x0000;
                    endpointFrame.SourceEndPoint = frame.DestinationEndPoint;
                    endpointFrame.DestinationEndPoint = frame.SourceEndPoint;
                    endpointFrame.ProfileId = 0x0104;


                    /*                    endpointFrame.Payload[0] = frame.Payload[0];
                                        endpointFrame.Payload[1] = 0x00; /* OK */
                    //endpointFrame.Payload[2] = frame.Payload[1]; /* Return Our Address */
                    //endpointFrame.Payload[3] = frame.Payload[2]; /* Return Our Address */
                    /*
                    endpointFrame.Payload[2] = Convert.ToByte((myAddr >> 8));
                    endpointFrame.Payload[3] = Convert.ToByte(myAddr & 0xFF);
                    endpointFrame.Payload[4] = Convert.ToByte(endpointFrame.Payload.Length - 5); // Bytes Sent 
                    endpointFrame.Payload[5] = frame.DestinationEndPoint;
                    endpointFrame.Payload[6] = (byte)'o' ;
                    endpointFrame.Payload[7] = (byte)'f';
                    endpointFrame.Payload[8] = (byte)'f';
                    endpointFrame.Payload[9] = 0x00;*/

                    endpointFrame.Payload = new byte[8];
                    endpointFrame.Payload[0] = 0x18;
                    endpointFrame.Payload[1] = frame.Payload[1];
                    endpointFrame.Payload[2] = 0x01; // Read response 
                    endpointFrame.Payload[3] = frame.Payload[3]; // ATTR LSB 
                    endpointFrame.Payload[4] = 0x00; // ATTR MSB
                    endpointFrame.Payload[5] = 0x00; // Status Success
                    endpointFrame.Payload[6] = 0x10; // Attr Type (Binary)
                    endpointFrame.Payload[7] = 0x00; // Attr VBalue - Off

                    /*                    endpointFrame.Payload[4] = 1;
                                        endpointFrame.Payload[5] = 0x10; */

                    await _comms.SendFrameAsync(endpointFrame);
                }
                else
                    HandleFrame(frame);
            }
            else if (frame.FrameType == Frame.FrameTypes.RxModemStatus)
            {
                switch (frame.ModemStatus)
                {
                    case Frame.ModemStatuss.JoinedNetwork: IsJoinedToNetwork = true; break;
                    case Frame.ModemStatuss.Disassociated: IsJoinedToNetwork = false; break;
                    case Frame.ModemStatuss.WatchdogTimerReset:
                        lock (this)
                        {
                            if (_resetCompletionSource != null)
                            {
                                _resetCompletionSource.SetResult(true);
                                _resetCompletionSource = null;
                            }
                        }
                        break;
                }
            }
        }

        private void GetClusterAttribute(ushort clusterId, ushort profileId)
        {

        }

        private bool _joinedToNetwork = false;
        public bool IsJoinedToNetwork
        {
            get { return _joinedToNetwork; }
            set
            {
                _joinedToNetwork = value;
                ZigbeeJoinStatusChanged?.Invoke(this, _joinedToNetwork);
                RaisePropertyChanged();
            }
        }

        public virtual List<Int16> GetInClusters()
        {
            var clusters = new List<Int16>();
            clusters.Add(0x0000);
            clusters.Add(0x0003);
            clusters.Add(0x0006);
            return clusters;
        }

        public async Task<bool> AnnounceAsync()
        {
            var msg = new Frame();

            var serialHigh = BitConverter.ToUInt32(_deviceATSettings["SH"], 0);
            var serialLow = BitConverter.ToUInt32(_deviceATSettings["SL"], 0);
            var myAddr = BitConverter.ToUInt16(_deviceATSettings["MY"], 0);

            msg.FrameType = Frame.FrameTypes.TxExplicitAddressCommand;
            msg.DestinationAddress64Bit = 0x0000000000000000;
            msg.DestinationAddress16Bit = 0xFFFC;
            msg.SourceEndPoint = 0x00;
            msg.DestinationEndPoint = 0x00;
            msg.ClusterId = 0x0013;
            msg.ProfileId = 0x0000;
            msg.BroadcastRadius = 0x00;
            msg.Options = 0x00;
            msg.Payload = new byte[12];
            msg.Payload[0] = 0x01; /* Frame ID */
            msg.Payload[1] = Convert.ToByte((myAddr >> 8));
            msg.Payload[2] = Convert.ToByte(myAddr & 0xFF);

            msg.Payload[3] = Convert.ToByte((serialLow >> 24) & 0xFF);
            msg.Payload[4] = Convert.ToByte((serialLow >> 16) & 0xFF);
            msg.Payload[5] = Convert.ToByte((serialLow >> 8) & 0xFF);
            msg.Payload[6] = Convert.ToByte(serialLow & 0xFF);

            msg.Payload[7] = Convert.ToByte((serialHigh >> 24) & 0xFF);
            msg.Payload[8] = Convert.ToByte((serialHigh >> 16) & 0xFF);
            msg.Payload[9] = Convert.ToByte((serialHigh >> 8) & 0xFF);
            msg.Payload[10] = Convert.ToByte(serialHigh & 0xFF);

            msg.Payload[11] = 0x04;



            //{ 0xAB, 0xA9, 0x9B, 0x5C, 0x15, 0x8C, 0x40, 0x00, 0xA2, 0x13, 0x00, 0x04 };

            return await SendFrame(msg);
        }

        public Task<bool> Disassociate()
        {
            _disassociateCompletionSource = new TaskCompletionSource<bool>();

            _comms.SendATFrame("DA");

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(3000));
                lock (this)
                {
                    if (_disassociateCompletionSource != null)
                    {
                        Core.PlatformSupport.Services.Logger.LogException("ResetDevice", new Exception("Timeout resetting device."));
                        _disassociateCompletionSource.SetResult(false);
                        _disassociateCompletionSource = null;
                    }
                }
            });

            return _disassociateCompletionSource.Task;
        }

        private void CreateXBeeProvision()
        {
            _deviceProfile.Clear();
            _deviceProfile.Add("SC", new byte[] { 0x7F, 0xFF });
            _deviceProfile.Add("ZS", new byte[] { 0x02 });
            _deviceProfile.Add("NJ", new byte[] { 0x5A });
            _deviceProfile.Add("NI", System.Text.UTF8Encoding.UTF8.GetBytes("Xbee End Point"));
            _deviceProfile.Add("NH", new byte[] { 0x1E });
            _deviceProfile.Add("NO", new byte[] { 0x03 });

            _deviceProfile.Add("SM", new byte[] { 0x01 });
            _deviceProfile.Add("EE", new byte[] { 0x01 });
            _deviceProfile.Add("EO", new byte[] { 0x01 });
            _deviceProfile.Add("AO", new byte[] { 0x03 });
            _deviceProfile.Add("KY", new byte[] { 0x5a, 0x69, 0x67, 0x42, 0x65, 0x65, 0x41, 0x6c, 0x6c, 0x69, 0x61, 0x6e, 0x63, 0x65, 0x30, 0x39 });
        }


        public States CurrentState { get { return _currentState; } }

        private Task<bool> QueryDeviceAsync()
        {
            _deviceQueryCompletionSource = new TaskCompletionSource<bool>();

            if (DeviceStateChanged != null)
                DeviceStateChanged(this, States.Initializing);

            var atParametersQueryList = new List<String>() { "OP", "OI", "SH", "SL", "MY", "SC", "ZS", "NJ", "NI", "NH", "NO", "AO", "SM", "EE", "EO" };
            AddSettingsToQuery(atParametersQueryList);
            BeginQuery();

            return _deviceQueryCompletionSource.Task;
        }

        private Task<bool> TransmitProfile()
        {
            _deviceSendProfileCompletionSource = new TaskCompletionSource<bool>();
            foreach (var key in _deviceProfile.Keys)
            {
                _atSettingsWriteQueue.Enqueue(new ATSettingValue() { ATCommand = key, Payload = _deviceProfile[key] });
            }

            var firstSetting = _atSettingsWriteQueue.Dequeue();
            _comms.SendATFrame(firstSetting.ATCommand, firstSetting.Payload);

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
                lock (this)
                {
                    if (_deviceSendProfileCompletionSource != null)
                    {
                        Core.PlatformSupport.Services.Logger.LogException("TransmitProfile", new Exception("Timeout sending new profile."));
                        _deviceSendProfileCompletionSource.SetResult(false);
                        _deviceSendProfileCompletionSource = null;
                    }
                }
            });

            return _deviceSendProfileCompletionSource.Task;
        }

        public Task<bool> ResetDevice()
        {
            _resetCompletionSource = new TaskCompletionSource<bool>();

            _comms.SendATFrame("FR");

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(3000));
                lock (this)
                {
                    if (_resetCompletionSource != null)
                    {
                        Core.PlatformSupport.Services.Logger.LogException("ResetDevice", new Exception("Timeout resetting device."));
                        _resetCompletionSource.SetResult(false);
                        _resetCompletionSource = null;
                    }
                }
            });

            return _resetCompletionSource.Task;
        }

        public Task<bool> GetIsConnected()
        {
            _isConnectedCompletionSource = new TaskCompletionSource<bool>();

            _comms.SendATFrame("AI");

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(3000));
                lock (this)
                {
                    if (_isConnectedCompletionSource != null)
                    {
                        Core.PlatformSupport.Services.Logger.LogException("GetIsConnected", new Exception("Timeout resetting device."));
                        _isConnectedCompletionSource.SetResult(false);
                        _isConnectedCompletionSource = null;
                    }
                }
            });

            return _isConnectedCompletionSource.Task;
        }


        private Task<bool> SaveEPROMSettings()
        {
            _saveParametersCompletionSource = new TaskCompletionSource<bool>();

            _comms.SendATFrame("WR");

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500));
                lock (this)
                {
                    if (_saveParametersCompletionSource != null)
                    {
                        Core.PlatformSupport.Services.Logger.LogException("SaveEPROMSettings", new Exception("Timeout updating EEPROM."));
                        _saveParametersCompletionSource.SetResult(false);
                        _saveParametersCompletionSource = null;
                    }
                }
            });

            return _saveParametersCompletionSource.Task;
        }

        public async Task<bool> InitializeAsync()
        {
            var result = await QueryDeviceAsync();
            if (result)
            {
                var countDiff = 0;
                foreach (var key in _deviceProfile.Keys)
                {
                    var profileArray = _deviceProfile[key];

                    if (key != "KY")
                    {
                        if (_deviceATSettings.ContainsKey(key))
                        {
                            var deviceArray = _deviceATSettings[key];
                            if (deviceArray.SequenceEqual(profileArray))
                                LogMessage("MATCH ON    " + key + ":  " + FormatByteArray(profileArray) + " == " + FormatByteArray(deviceArray));
                            else
                            {
                                LogMessage("NO MATCH ON " + key + ":  " + FormatByteArray(profileArray) + " != " + FormatByteArray(deviceArray));
                                countDiff++;
                            }
                        }
                        else
                        {
                            LogMessage("DOES NOT HAVE KEY  " + key);
                        }
                    }
                }

                if (countDiff > 0)
                {
                    result = await TransmitProfile();
                    if (result)
                        result = await SaveEPROMSettings();

                    if (result)
                        result = await ResetDevice();
                }
            }


            if (DeviceStateChanged != null)
                DeviceStateChanged(this, States.Ready);

            return result;
        }

        public async Task<bool> SendFrame(Frame frame)
        {
            return await _comms.SendFrameAsync(frame);
        }

        public async Task<bool> SendATFrame(string atCommand, byte[] payload = null)
        {
            return await _comms.SendATFrameAsync(atCommand, payload);
        }

        public async Task<bool> SendATFrame(string atCommand, string payload)
        {
            var chs = System.Text.Encoding.UTF8.GetBytes(payload);

            return await _comms.SendATFrameAsync(atCommand, chs);
        }

        public void Dispose()
        {
            _currentState = States.Disposed;

            _comms.FrameReady -= _comms_FrameReady;
            _comms = null;
        }
    }
}
