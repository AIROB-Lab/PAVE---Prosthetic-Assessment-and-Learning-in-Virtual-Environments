using System;
using Sim;
using UnityEngine;

/// <summary>PAVE feedback destination; encoding and sockets belong to the upstream SIM package.</summary>
public class SimUdpSender : MonoBehaviour
{
    public string host = "localhost";
    public int port;
    static SimUdpSender active;
    SimUdpConnection connection;
    ushort counter;
    void OnEnable()
    {
        active = this;
        connection = new SimUdpConnection();
    }

    void OnDisable()
    {
        connection?.Dispose();
        connection = null;
        if (active == this)
        {
            active = null;
        }
    }

    public static void SendArrayAsUDPmessage(double[] array, (byte, byte) dataType, bool sendWithLastUdpTs = false)
    {
        if (active == null || active.connection == null)
        {
            return;
        }

        double time = sendWithLastUdpTs ? StreamlinedInputManager.udpReceiver.lastReceivedUDPTS : SimClock.Now;
        if (sendWithLastUdpTs && time < 0)
        {
            return;
        }

        try
        {
            byte[] bytes = SimPacketCodec.Encode(array, dataType.Item1, dataType.Item2, time, active.counter++);
            active.connection.Send(bytes, active.host, active.port);
        }
        catch (Exception error)
        {
            Debug.LogError("SIM feedback failed: " + error.Message, active);
        }
    }
}
