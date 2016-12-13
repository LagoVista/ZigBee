using LagoVista.ZigBee.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LagoVista.ZigBee
{

    public interface IZigBeeComms
    {
        event EventHandler<Models.Frame> FrameReady;
        event EventHandler<bool> ConnectionStateChanged;

        Task RefreshAvailablePorts();

        ObservableCollection<PortInfo> SerialDevices { get; }

        bool Connected { get; }

        Task Init();

        Task<bool> SendFrameAsync(Models.Frame frame);
        Task<bool> SendATFrameAsync(String command, byte[] payload = null);

        void SendATFrame(String command, byte[] payload = null);

        Task<bool> OpenAsync(PortInfo comPort);

        void Close();

        void Listen();

        bool ShowDiagnostics { get; set; }
    }
}
