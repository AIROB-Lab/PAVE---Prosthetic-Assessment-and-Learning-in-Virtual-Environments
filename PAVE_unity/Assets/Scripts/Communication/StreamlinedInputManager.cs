using System;
using System.Globalization;
using Sim;
using UnityEngine;

/// <summary>PAVE adapter for the upstream SIM runtime. Tracking lives in PaveTracking.
/// The original component name/GUID and serialized fields are retained for existing scenes.</summary>
public class StreamlinedInputManager : PaveTracking
{
    [Tooltip("Receive SIM commands on this local UDP port.")]
    public bool receiveUdp;
    public int port = 11000;
    [Min(1)] public int availableUDPSamples = 100;
    public double lastUdpTimestamp;
    SimUdpConnection connection;
    int boundPort;
    public int InvalidPacketCount => connection?.InvalidPackets ?? 0;
    public int DroppedPacketCount => connection?.DroppedPackets ?? 0;
    public static ReceivingUdp udpReceiver = new ReceivingUdp();

    /// <summary>Legacy lookup adapter; history storage and delay semantics belong to SIM upstream.</summary>
    public sealed class ReceivingUdp
    {
        internal readonly SimHistory History = new SimHistory();
        public double lastReceivedUDPTS => History.LastTimestampSeconds;
        public object[] getData(byte category, byte subCategory, int samplesBack = 0, float timeBack = 0)
            => History.Get(category, subCategory, samplesBack, timeBack)?.Values;
        public System.Collections.Generic.List<SimPacket> getUdpObjects(byte category, byte subcategory, bool clear = false)
        {
            var batch = History.ReadAll(category, subcategory, clear);
            return batch == null ? null : new System.Collections.Generic.List<SimPacket>(batch);
        }
        public SimPacket getUdpObject(byte category, byte subCategory) => History.Get(category, subCategory);
    }

    protected override void Start()
    {
        base.Start();
        udpReceiver.History.Clear();
        if (activateLogging) LoggingManager.CreateNewLog("UDP_Received", "time_stamp_s,source_time_s,category,subcategory,data,counter\n");
    }

    protected override void Update()
    {
        base.Update();
        if (!receiveUdp || (connection != null && boundPort != port))
        {
            connection?.Dispose();
            connection = null;
            udpReceiver.History.Clear();
            if (!receiveUdp) return;
        }
        if (connection == null)
        {
            try { connection = new SimUdpConnection(port); boundPort = port; }
            catch (Exception e) { Debug.LogError("Cannot open SIM input: " + e.Message, this); receiveUdp = false; return; }
        }
        udpReceiver.History.Capacity = Math.Max(1, availableUDPSamples);
        while (connection.TryReceive(out var packet))
        {
            udpReceiver.History.Add(packet);
            lastUdpTimestamp = packet.TimestampSeconds;
            if (activateLogging)
            {
                string data = string.Join(" ", Array.ConvertAll(packet.Values, value => Convert.ToString(value, CultureInfo.InvariantCulture)));
                LoggingManager.AddToBuffer("UDP_Received", FormattableString.Invariant($"{packet.ReceivedAtSeconds},{packet.TimestampSeconds},{packet.Category},{packet.Subcategory},[{data}],{packet.Counter}\n"));
            }
        }
    }

    protected override void OnDisable()
    {
        connection?.Dispose(); connection = null;
        udpReceiver.History.Clear();
        base.OnDisable();
    }
}
