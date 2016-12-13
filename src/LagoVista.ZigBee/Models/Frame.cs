using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LagoVista.ZigBee.Models
{
    public class Frame
    {
        private int _bytePointer = 1; /* When starting with a new instance, we already have the delimiter read in so start with 2nd byte */

        public enum FrameTypes
        {
            TxATCommand = 0x08,
            TxATCommandQueueParameterValue = 0x09,
            TxATCommandTransmitRequest = 0x10,
            TxExplicitAddressCommand = 0x11,
            TxRemotAttCommand = 0x17,
            TxCreatSourceRoute = 0x21,
            TxRegisterJoiningDevice = 0x24,
            RxAtCommandResponse = 0x88,
            RxModemStatus = 0x8A,
            RxTransmitStatus = 0x8B,
            RxReceivePacket = 0x91,
            RxIODataSampleRxIndicator = 0x92,
            RxXBeeSensorReadIndicator = 0x94,
            RxNodeIdentificationIndicator = 0x95,
            RxRemoteATCommandResponse = 0x97,
            RxExtendedModemStatus = 0x98,
            RxOverTheAirFirmwareUpdateStatus = 0xA0,
            RxRouterRecordIndicator = 0xA1,
            RxManyToOneRouteRequestIndicator = 0xA3,
            RxJoinNotificationStatus = 0xA5,
        }

        public enum ModemStatuss
        {
            HardwareReset = 0,
            WatchdogTimerReset = 1,
            JoinedNetwork = 2,
            Disassociated = 3,
            CoordinatorStarted = 4,
            NetworkKeyUpdated = 5,
            VoltageExceeded = 0x0D,
            ModemConfigChangedJoinInProgress = 0x11
        }

        public enum ATCommandStatuss
        {
            OK = 0,
            Error = 1,
            InvalidCommand = 2,
            InvalidParameter = 3,
            TxFailure = 4,
        }

        public enum DiscoveryStatuss
        {
            NoDiscoveryOverhead = 0x00,
            AddressDiscovery = 0x01,
            RouteDiscovery = 0x02,
            AddressAndRoute = 0x03,
            ExtendedTimeoutDiscovery = 0x40
        }

        public enum DeliveryStatuss
        {
            OK = 0,
            MACAckFailure = 1,
            CCAFailure = 0x02,
            InvalidDestinationEndpoint = 0x15,
            NetworkACKFailure = 0x21,
            NotJoinedToNetwork = 0x22,
            SelfAddress = 0x23,
            AddressNotFound = 0x24,
            RouteNotFound = 0x25,
            BroadcastSourceFailedToHearNeighborRelay = 0x26,
            InvalidBindingTableIndex = 0x2B,
            ResourceErrorLackOfFreeBuffers = 0x2C,
            AttemptBroadcastWithAPSTx = 0x2D,
            AttemptUnicodeWithAPSTxButEE = 0x2E,
            ResourceErrorLackOfFreeBuffers2 = 0x32,
            DataPayloadTooLarge = 0x74,
            IndirectMessageUnrequested = 0x75,
        }

        public UInt16 Length { get; set; }
        public FrameTypes FrameType { get; set; }
        public byte FrameId { get; set; }

        public UInt16 ATCommand { get; set; }

        public String ATCommandString
        {
            set
            {
                var apCommand = value.ToCharArray();
                if (apCommand.Length != 2)
                    throw new Exception("AP Command must alwasy be two characters");

                ATCommand = Convert.ToUInt16((Convert.ToByte(apCommand[0]) << 8) | Convert.ToByte(apCommand[1]));
            }

            get
            {
                if (ATCommand == 0)
                    return "-";

                var char1 = Convert.ToChar(ATCommand >> 8);
                var char2 = Convert.ToChar(ATCommand & 0xFF);

                return String.Format("{0}{1}", char1, char2);
            }
        }


        public ATCommandStatuss ATCommandStatus { get; set; }

        public UInt64 DestinationAddress64Bit { get; set; }
        public UInt16 DestinationAddress16Bit { get; set; }

        public byte SourceEndPoint { get; set; }
        public byte DestinationEndPoint { get; set; }
        public UInt16 ClusterId { get; set; }
        public UInt16 ProfileId { get; set; }
        public Byte BroadcastRadius { get; set; }
        public Byte Options { get; set; }

        public byte TransmitRetryCount { get; set; }
        public DeliveryStatuss DeliveryStatus { get; set; }

        public DiscoveryStatuss DiscoveryStatus { get; set; }

        public byte ReceiveOptions { get; set; }

        public Byte[] Payload { get; set; }

        public Byte CheckSum { get; set; }

        public ModemStatuss ModemStatus { get; set; }


        private static byte CalculateCheckSum(byte[] buffer, int len)
        {
            ulong checkSum = 0x00;
            for (var idx = 3; idx < len + 1; ++idx)
                checkSum += buffer[idx];

            return Convert.ToByte(0xFF - (checkSum & 0xFF));

        }

        private static byte _frameId;

        private static byte GetNextFrameId()
        {
            _frameId++;

            return _frameId;
        }

        public byte[] MessageBuffer
        {
            get
            {
                FrameId = GetNextFrameId();
                var ptr = 0;

                var buffer = new byte[1024];
                buffer[ptr++] = 0x7E;
                buffer[ptr++] = 0xFF; /* Place holder for empty */
                buffer[ptr++] = 0xFF; /* Place holder for empty */
                buffer[ptr++] = Convert.ToByte(FrameType);
                buffer[ptr++] = FrameId;

                if (FrameType == FrameTypes.TxExplicitAddressCommand ||
                    FrameType == FrameTypes.TxATCommandTransmitRequest)
                {
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit >> 54);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit >> 48);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit >> 40);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit >> 32);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit >> 24);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit >> 16);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit >> 8);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress64Bit & 0xFF);

                    buffer[ptr++] = Convert.ToByte(DestinationAddress16Bit >> 8);
                    buffer[ptr++] = Convert.ToByte(DestinationAddress16Bit & 0xFF);

                    if (FrameType == FrameTypes.TxExplicitAddressCommand)
                    {
                        buffer[ptr++] = SourceEndPoint;
                        buffer[ptr++] = DestinationEndPoint;

                        buffer[ptr++] = Convert.ToByte(ClusterId >> 8);
                        buffer[ptr++] = Convert.ToByte(ClusterId & 0xFF);

                        buffer[ptr++] = Convert.ToByte(ProfileId >> 8);
                        buffer[ptr++] = Convert.ToByte(ProfileId & 0xFF);
                    }

                    buffer[ptr++] = BroadcastRadius;
                    buffer[ptr++] = Options;

                    while (ptr < 23 + Payload.Length)
                    {
                        buffer[ptr] = Payload[ptr - 23];
                        ptr++;
                    }
                }

                if (FrameType == FrameTypes.TxATCommand ||
                    FrameType == FrameTypes.TxATCommandQueueParameterValue)
                {
                    buffer[ptr++] = Convert.ToByte(ATCommand >> 8);
                    buffer[ptr++] = Convert.ToByte(ATCommand & 0xFF);

                    if (Payload != null)
                    {
                        while (ptr < 7 + Payload.Length)
                        {
                            buffer[ptr] = Payload[ptr - 7];
                            ptr++;
                        }
                    }
                }

                buffer[1] = Convert.ToByte((ptr - 3) >> 8);
                buffer[2] = Convert.ToByte((ptr - 3) & 0xFF);

                CheckSum = CalculateCheckSum(buffer, ptr - 1);

                buffer[ptr] = CheckSum;



                var sendBuffer = new byte[ptr + 1];
                for (var idx = 0; idx <= ptr; ++idx)
                    sendBuffer[idx] = buffer[idx];

                return sendBuffer;
            }
        }

        public bool HandleByte(byte ch)
        {
            var completed = false;

            if (_bytePointer == 1) Length = Convert.ToUInt16((UInt16)ch << 8);
            if (_bytePointer == 2) Length |= (UInt16)ch;
            if (_bytePointer == 3) FrameType = (FrameTypes)ch;

            if (FrameType == FrameTypes.RxModemStatus)
            {
                if (_bytePointer == 4) ModemStatus = (ModemStatuss)ch;

                if (_bytePointer == 5)
                {
                    CheckSum = ch;
                    completed = true;
                }
            }

            if (FrameType == FrameTypes.RxAtCommandResponse)
            {
                if (_bytePointer == 3) Payload = new byte[Length - 5];

                if (_bytePointer == 4) FrameId = ch;
                if (_bytePointer == 5) ATCommand = Convert.ToUInt16((UInt16)ch << 8);
                if (_bytePointer == 6) ATCommand |= Convert.ToUInt16((UInt16)ch & 0xFF);
                if (_bytePointer == 7) ATCommandStatus = (ATCommandStatuss)ch;
                if (_bytePointer > 7 && _bytePointer < Length + 3)
                    Payload[_bytePointer - 8] = ch;

                if (_bytePointer == Length + 3)
                {
                    CheckSum = ch;
                    completed = true;
                }
            }

            if (FrameType == FrameTypes.RxReceivePacket)
            {
                if (_bytePointer == 3) Payload = new byte[Length - 18];

                if (_bytePointer == 4) DestinationAddress64Bit = Convert.ToUInt64((UInt64)ch << 54);
                if (_bytePointer == 5) DestinationAddress64Bit |= Convert.ToUInt64((UInt64)ch << 48);
                if (_bytePointer == 6) DestinationAddress64Bit |= Convert.ToUInt64((UInt64)ch << 40);
                if (_bytePointer == 7) DestinationAddress64Bit |= Convert.ToUInt64((UInt64)ch << 32);
                if (_bytePointer == 8) DestinationAddress64Bit |= Convert.ToUInt64((UInt64)ch << 24);
                if (_bytePointer == 9) DestinationAddress64Bit |= Convert.ToUInt64((UInt64)ch << 16);
                if (_bytePointer == 10) DestinationAddress64Bit |= Convert.ToUInt64((UInt64)ch << 8);
                if (_bytePointer == 11) DestinationAddress64Bit |= Convert.ToUInt64((UInt64)ch);

                if (_bytePointer == 12) DestinationAddress16Bit = Convert.ToUInt16((UInt16)ch << 8);
                if (_bytePointer == 13) DestinationAddress16Bit |= Convert.ToUInt16((UInt16)ch);

                if (_bytePointer == 14) SourceEndPoint = ch;
                if (_bytePointer == 15) DestinationEndPoint = ch;

                if (_bytePointer == 16) ClusterId = Convert.ToUInt16((UInt16)ch << 8);
                if (_bytePointer == 17) ClusterId |= Convert.ToUInt16((UInt16)ch);

                if (_bytePointer == 18) ProfileId = Convert.ToUInt16((UInt16)ch << 8);
                if (_bytePointer == 19) ProfileId |= Convert.ToUInt16((UInt16)ch);

                if (_bytePointer == 20) ReceiveOptions = ch;

                if (_bytePointer > 20 && _bytePointer < Length + 3)
                    Payload[_bytePointer - 21] = ch;

                if (_bytePointer == Length + 3)
                {
                    CheckSum = ch;
                    completed = true;
                }
            }

            if (FrameType == FrameTypes.RxTransmitStatus)
            {
                if (_bytePointer == 5) DestinationAddress16Bit = Convert.ToUInt16((UInt16)ch << 8);
                if (_bytePointer == 6) DestinationAddress16Bit |= Convert.ToUInt16((UInt16)ch);

                if (_bytePointer == 7) TransmitRetryCount = ch;

                if (_bytePointer == 8) DeliveryStatus = (DeliveryStatuss)ch;

                if (_bytePointer == 9) DiscoveryStatus = (DiscoveryStatuss)ch;

                if (_bytePointer == Length + 3)
                {
                    CheckSum = ch;
                    completed = true;
                }
            }


            if (!completed)
                _bytePointer++;



            return completed;
        }

        private String FormatHex(UInt64 val)
        {
            /* PROBABLY A MUCH SMART ALGORITHM...VERY LONG DAY AND ON SECOND DRINK! */
            var bldr = new StringBuilder();
            bldr.AppendFormat("{0:X2} ", val >> 54);
            bldr.AppendFormat("{0:X2} ", ((val >> 48) & 0xFF));
            bldr.AppendFormat("{0:X2} ", ((val >> 40) & 0xFF));
            bldr.AppendFormat("{0:X2} ", ((val >> 32) & 0xFF));
            bldr.AppendFormat("{0:X2} ", ((val >> 24) & 0xFF));
            bldr.AppendFormat("{0:X2} ", ((val >> 16) & 0xFF));
            bldr.AppendFormat("{0:X2} ", ((val >> 8) & 0xFF));
            bldr.AppendFormat("{0:X2}", (val & 0xFF));

            return bldr.ToString();
        }

        private String FormatHex(UInt16 val)
        {
            /* PROBABLY A MUCH SMART ALGORITHM...VERY LONG DAY AND ON SECOND DRINK! */
            var bldr = new StringBuilder();
            bldr.AppendFormat("{0:X2} ", ((val >> 8) & 0xFF));
            bldr.AppendFormat("{0:X2}", (val & 0xFF));

            return bldr.ToString();
        }

        private void WritePayload(StringBuilder bld)
        {
            bld.AppendFormat("Payload           \t: ");

            if (Payload != null)
            {
                foreach (var ch in Payload)
                {
                    bld.Append(ch.ToString("X2"));
                    bld.Append(" ");
                }
            }
            else
                bld.Append("[none]");

            bld.AppendLine();
        }

        public override string ToString()
        {
            var bld = new StringBuilder();
            if ((byte)FrameType < 0x7F)
                bld.AppendFormat("\r\n>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>\r\n");
            else
                bld.AppendFormat("\r\n<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<\r\n");


            bld.AppendFormat("Frame Type        \t: {0}\r\n", FrameType.ToString());

            if (FrameType == FrameTypes.RxModemStatus)
                bld.AppendFormat("Modem Status      \t: {0}\r\n", ModemStatus.ToString());

            if (FrameType == FrameTypes.RxTransmitStatus)
            {
                bld.AppendFormat("Retry Count       \t: {0}\r\n", TransmitRetryCount);
                bld.AppendFormat("Transmit Status   \t: {0}\r\n", DeliveryStatus);
                bld.AppendFormat("Discovery Status  \t: {0}\r\n", DiscoveryStatus);
            }

            if (FrameType == FrameTypes.TxATCommand)
            {
                bld.AppendFormat("AT Command        \t: {0}\r\n", ATCommandString);

                WritePayload(bld);
            }

            if (FrameType == FrameTypes.RxAtCommandResponse)
            {
                bld.AppendFormat("AT Command        \t: {0}\r\n", ATCommandString);
                bld.AppendFormat("Sending Frame Id  \t: {0}\r\n", FrameId);
                bld.AppendFormat("AT Status         \t: {0}\r\n", ATCommandStatus);

                WritePayload(bld);
            }

            if (FrameType == FrameTypes.RxReceivePacket)
            {
                bld.AppendFormat("Length            \t: {0}\r\n", FormatHex(Length));
                bld.AppendFormat("Payload Length    \t: {0}\r\n", FormatHex(Convert.ToUInt16(Length - 17)));
                bld.AppendFormat("Dest 64 Bit Addr  \t: {0}\r\n", FormatHex(DestinationAddress64Bit));
                bld.AppendFormat("Dest 16 Bit Addr  \t: {0}\r\n", FormatHex(DestinationAddress16Bit));

                bld.AppendFormat("Source End Point  \t: {0:X2}\r\n", SourceEndPoint);
                bld.AppendFormat("Dest End Point    \t: {0:X2}\r\n", DestinationEndPoint);

                bld.AppendFormat("Cluster ID        \t: {0}\r\n", FormatHex(ClusterId));
                bld.AppendFormat("Profile ID        \t: {0}\r\n", FormatHex(ProfileId));

                bld.AppendFormat("Receive Options   \t: {0:X2}\r\n", ReceiveOptions);

                WritePayload(bld);
            }

            if (FrameType == FrameTypes.TxExplicitAddressCommand)
            {
                bld.AppendFormat("Frame ID          \t: {0:X2}\r\n", FrameId);
                bld.AppendFormat("Dest 64 Bit Addr  \t: {0}\r\n", FormatHex(DestinationAddress64Bit));
                bld.AppendFormat("Dest 16 Bit Addr  \t: {0}\r\n", FormatHex(DestinationAddress16Bit));

                bld.AppendFormat("Source End Point  \t: {0:X2}\r\n", SourceEndPoint);
                bld.AppendFormat("Dest End Point    \t: {0:X2}\r\n", DestinationEndPoint);

                bld.AppendFormat("Cluster ID        \t: {0}\r\n", FormatHex(ClusterId));
                bld.AppendFormat("Profile ID        \t: {0}\r\n", FormatHex(ProfileId));

                bld.AppendFormat("Broadcast Radius  \t: {0:X2}\r\n", BroadcastRadius);
                bld.AppendFormat("Transmit Options  \t: {0:X2}\r\n", Options);

                bld.AppendFormat("Check Sum         \t: {0:X2}\r\n", CheckSum);

                WritePayload(bld);
            }

            if ((byte)FrameType < 0x7F)
                bld.AppendFormat(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>\r\n");
            else
                bld.AppendFormat("<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<\r\n");

            bld.Append("\r\n");

            return bld.ToString();


        }
    }
}
