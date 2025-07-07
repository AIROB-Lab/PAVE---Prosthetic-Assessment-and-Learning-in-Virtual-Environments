using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;

public class SimUdpSender : MonoBehaviour
{
    // Start is called before the first frame update

    public int port;
    static private bool initialised = false;

    void Start()
    {
        string IpAddress = "localhost";
        // open UDP stream client!
        print($"connecting to ip address {IpAddress} and port {port}");
        _UdpClient.Connect("localhost", port);

        initialised = true;
    }

    #region From iM-Blocks

    /// <summary>
    /// Udp client for the connection with e.g. Unity
    /// </summary>
    static private UdpClient _UdpClient = new UdpClient();

    /// <summary>
    /// This is the general UDP counter of sent messages over all types
    /// </summary>
    static private UInt16 UDP_counter = 0;

    // Arrays of supported data types
    static Type[] floatingTypes = { typeof(float), typeof(double) };
    static Type[] signedTypes = { typeof(sbyte), typeof(short), typeof(int), typeof(long) };
    static Type[] unsignedTypes = { typeof(ushort), typeof(uint), typeof(ulong), typeof(byte) };

    /// <summary>
    /// Function to get the specific command for UDP Protocol to hand over info on data type
    /// </summary>
    /// <param name="type">is a data type e.g. float </param>
    /// <returns></returns>
    static private byte GetByteForType(Type type)
    {
        if (!floatingTypes.Contains(type) && !signedTypes.Contains(type) && !unsignedTypes.Contains(type))
        {
            throw new Exception("This data type is not support yet by the UDP protocol: " + type.Name);
        }

        byte typeInfo = 0b_0000_0000;

        // decimal?
        if (floatingTypes.Contains(type))
        {
            typeInfo += 0b_0010_0000;
        }
        // if not check if signed or unsigned (only positive)
        else if (signedTypes.Contains(type))
        {
            typeInfo += 0b_0001_0000;
        }
        // num of bytes?
        typeInfo += (byte)Marshal.SizeOf(type);

        return typeInfo;
    }



    public struct UdpPayload
    {
        // The count of data elements (not raw bytes)
        public byte NumCount;
        // The .NET type, e.g. typeof(double) or typeof(sbyte)
        public Type ValType;
        // Category + subcategory
        public (byte, byte) DataType;
        // The actual raw bytes to send
        public byte[] Data;
    }
    
    /// <summary>
    /// Send an array as UDP stream with the sim protocol
    /// </summary>
    /// <param name="array"></param> the array to be sent
    /// <param name="dataType">the arbitrary datatype it should be sent as (category, subcategory) so that the recv understands what came in</param>
    public static void SendArrayAsUDPmessage(double[] array, (byte, byte) dataType)
    {
        List<byte> dataList = new List<byte>();

        // get the corresponding value
        for (int i = 0; i < array.Length; i++)
        {
            // needs add range as it might add more then one byte
            dataList.AddRange(BitConverter.GetBytes(array[i]));
        }
        // send standardized UDP message
        SendUDPmessage((byte)array.Length, typeof(double), dataType, dataList.ToArray());
    }

    public static void SendUDPmessage(byte numCount, Type valType, (byte, byte) dataType, byte[] data)
    {
        if (initialised)
        {
            // add components
            List<byte> udpMessage = new List<byte>
            {
                numCount,
                GetByteForType(valType),
                dataType.Item1,
                dataType.Item2,
            };
            udpMessage.AddRange(BitConverter.GetBytes(StreamlinedInputManager.Now));
            udpMessage.AddRange(data);
            udpMessage.AddRange(BitConverter.GetBytes(UDP_counter));

            UDP_counter++;


            _UdpClient.SendAsync(udpMessage.ToArray(), udpMessage.Count);
        }
        else
        {
            print("Sender not initialised yet");
        }
    }

    #endregion
}
