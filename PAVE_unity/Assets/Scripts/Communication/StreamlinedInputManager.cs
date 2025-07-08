using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using ViveSR.anipal.Eye;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using static UnityEngine.XR.OpenXR.Features.Interactions.HTCViveTrackerProfile;
using System.Collections;
using Unity.VisualScripting;
using System.Linq;
using UnityEngine.Android;
using static StreamlinedInputManager;

// ********************************************************************************************************************
//  USAGE
//
// 1. Select in the Unity inspector which devices you want to have tracked.
// 2. Select whether you want to log your tracking or not. If logging is activated, all tracked devices are being logged.
//
// 3. Tracking information for tracked devices can be accessed while the program is running through the properties of
//      - headTracker
//      - leftControllerTracker
//      - rightControllerTracker
//      - eyeTracker
//      and
//      - all TrackerTracking instances contained in the trackerTrackers dictionary (accessible thorugh the name of the tracker as a key)
//  4. Logged information is written in the folder ./data

//  -------------------------------------------------------------------------------------------------------------------

//  UNDERSTAND THE INFORMATION that is being logged with this script.
//
//  WHAT is being logged?
//
//      Inside Update function:
//          - Position + Rotation of the HEADSET
//          - Position + Rotation of the LEFT CONTROLLER
//          - Position + Rotation of the RIGHT CONTROLLER
//          - Position + Rotation of the different VIVE TRACKERS
//
//      With 120Hz using SRanipal EyeFramework:
//          - EYETACKING data
//
//  WHAT POSITION is logged?
//
//       Headset:    MainCamera.transform = Headset.position 
//       Controllers', Headset's and Trackers' positions are logged in the real world coordinate system you 
//       can see on the floor while the game is loading up. It is derived by the position
//       the boundaries you have marked with your controller in the room when setting up SteamVR.
//       The posiiton in the world space is therefore XROrigin.transform + CameraOffset.transform +
//       Position of the controller/headset/tracker (equivalent to their position in Unity!)
//
//       x-Axis: positive towards the base stations (thumb)
//       z-Axis: positive towards the left when looking at the base stations (pointing finger)
//       y-Axis: positive towards the top (middle finger)
//          --> LEFT-HANDED COORDINATE SYSTEM
//      
//
//  WHAT EYETRACKING data is logged?
//      
//      - System timestamp (unix) & timestamp from the SRanipal Eye Framework (since start of application?)
//      - Data from the Left Eye, the Right Eye and Combined Eye Data 
//      - The eyetracking gaze direction is inside a right-handed coordinate system with z pointing to the front, x to the side and y to the top
//
// ********************************************************************************************************************


// class needed for letting user choose from Unity inspector which trackers to track

public class StreamlinedInputManager : MonoBehaviour
{
    public class HeadTracking
    {
        public double headTimestamp;
        public Vector3 headPosition;
        public Quaternion headRotation;
        Vector3 Position
        {
            get { return headPosition;  }
        }
        Quaternion Rotation
        {
            get { return headRotation; }
        }
        double Timestamp
        {
            get { return headTimestamp; }
        }
    }

    public class LeftControllerTracking
    {
        public double leftControllerTimestamp;
        public Vector3 leftControllerPosition;
        public Quaternion leftControllerRotation;
        Vector3 Position
        {
            get { return leftControllerPosition; }
        }
        Quaternion Rotation
        {
            get { return leftControllerRotation; }
        }
        double Timestamp
        {
            get { return leftControllerTimestamp; }
        }
    }

    public class RightControllerTracking
    {
        public double rightControllerTimestamp;
        public Vector3 rightControllerPosition;
        public Quaternion rightControllerRotation;
        Vector3 Position
        {
            get { return rightControllerPosition; }
        }
        Quaternion Rotation
        {
            get { return rightControllerRotation; }
        }
        double Timestamp
        {
            get { return rightControllerTimestamp; }
        }
    }

    public class EyeTracking
    {
        internal Vector3 debugPos;
        internal Quaternion debugOri;

        //private double eyeTimestamp;

        // unix timestamp, eyedata
        private (double, EyeData) current_eyeData;
        private List<(double, EyeData)> last_eyeDatas = new List<(double, EyeData)>();
        public (double, EyeData) EyeData
        {
            get { lock (eyeLock) { return current_eyeData; } }
            set { lock (eyeLock) { 
                    current_eyeData = value; 
                    last_eyeDatas.Add(value);

                    while (last_eyeDatas.Count() > last_eyeData_size)
                    {
                        last_eyeDatas.RemoveAt(0);
                    }
                } 
            }
        }
        public (double, EyeData)[] Last_eyeDatas
        {
            get {lock (eyeLock)
                {
                    // last eye datas to array
                    (double, EyeData)[] arrOut = last_eyeDatas.ToArray();
                    // clear eye data buffer => Important can only get it once!
                    last_eyeDatas.Clear();
                    // return
                    return arrOut;
                }}
        }
        //public double EyeTimestamp
        //{
        //    get { lock (eyeLock) { return eyeTimestamp; } }
        //    set { lock (eyeLock) { eyeTimestamp = value; } }
        //}
    }

    public class TrackerTracking
    {
        public double trackerTimestamp;
        public Vector3 trackerPosition;
        public Quaternion trackerRotation;
        Vector3 Position
        {
            get { return trackerPosition; }
        }
        Quaternion Rotation
        {
            get { return trackerRotation; }
        }
        double Timestamp
        {
            get { return trackerTimestamp; }
        }
    }

    public class ReceivingUdp
    {
        // infos
        // the key is a Int16 composed of the two category bytes sent
        public Dictionary<UInt16, List<UdpObject>> lastReceivedUDPPackets = new Dictionary<UInt16, List<UdpObject>>();
        // public List<UdpObject> allReceivedUDPPackets = new List<UdpObject>(); // clean up this from time to time!
        public double lastReceivedUDPTS = -1;
        public object[] getData(byte category, byte subCategory, int samplesBack = 0, float timeBack = 0)
        {
            if (samplesBack != 0 && timeBack != 0) throw new Exception("Only samples back or time back can be set");

            byte[] key = { category, subCategory };
            List<UdpObject> res;

            // lock udp received messages
            lock (udpLock)
            {
                if (!lastReceivedUDPPackets.TryGetValue(BitConverter.ToUInt16(key, 0), out res))
                {
                    //print("The key you are trying to access a value from has not once been received yet!");
                    return null;
                }

                // get the most current sample
                else if (timeBack == 0 && samplesBack == 0) return res[res.Count-1].Data; // standard get most current data

                // get a sample which was received earlier and is still available
                else if (timeBack == 0 && samplesBack != 0 && samplesBack < res.Count) return res[res.Count - samplesBack].Data; // standard get most current data

                // get a sample with a certain timestamp
                else if (timeBack != 0 && samplesBack == 0)
                {
                    UdpObject closestObject = res[res.Count - 1];
                    double newestObjectTime = closestObject.timestamp;
                    
                    double minDifference = timeBack;

                    for (int i = res.Count-1; i >= 0; i--)
                    {
                        double difference = Math.Abs(newestObjectTime - timeBack - res[i].timestamp);
                        if (difference <= minDifference)
                        {
                            minDifference = difference;
                            closestObject = res[i];
                        }
                        // if it gets bigger again then we can quite early as the samples are ordered by time
                        else
                        {
                            break;
                        }
                    }

                    return closestObject.Data;
                }

                // if none of these cases return null
                else return null;
            }
        }

        public UdpObject getUdpObject(byte category, byte subCategory)
        {
            byte[] key = { category, subCategory };
            List<UdpObject> res;

            // lock udp received messages
            lock (udpLock)
            {
                if (!lastReceivedUDPPackets.TryGetValue(BitConverter.ToUInt16(key, 0), out res))
                {
                    print("The key you are trying to access a value from has not once been received yet!");
                    return null;
                }
                return res[0];
            }
        }
    }

    // struc tbecause we add objects of this tyüe to a list and need copies isntead of references of them saved
    public class UdpObject
    {
        private int dataPointSize;
        public byte category;
        public byte subCategory;
        public double timestamp;
        public byte[] data;
        public UInt16 counter;
        public Type dtype;

        public UdpObject(byte[] received)
        {
            category = received[2];
            subCategory = received[3];
            dtype = getType(received[1]);
            timestamp = getTimestamp(new ArraySegment<byte>(received, 4, 8));
            counter = BitConverter.ToUInt16(received, received.Length - 2);
            dataPointSize = Marshal.SizeOf(dtype);

            // Copy the received data part of the message into the data field of the UdpObject
            data = new byte[received[0] * Marshal.SizeOf(dtype)];
            Array.Copy(received, 12, data, 0, data.Length);
        }

        public object[] Data
        { 
            get 
            { 
                object[] ret = new object[data.Length / dataPointSize];
                if (dtype == typeof(float))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToSingle(data, i*dataPointSize);
                    }
                    return ret;
                }
                else if (dtype == typeof(double))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToDouble(data, i * dataPointSize);
                    }
                    return ret;
                }
                else if (dtype == typeof(sbyte))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = unchecked((sbyte)data[i]);
                    }
                    return ret;
                }
                else if (dtype == typeof(short))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToInt16(data, i * dataPointSize);
                    }
                    return ret;
                }
                else if (dtype == typeof(int))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToInt32(data, i * dataPointSize);
                    }
                    return ret;
                }
                else if (dtype == typeof(long))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToInt64(data, i * dataPointSize);
                    }
                    return ret;
                }
                else if (dtype == typeof(byte))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = data[i];
                    }
                    return ret;
                }
                else if (dtype == typeof(ushort))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToUInt16(data, i * dataPointSize);
                    }
                    return ret;
                }
                else if (dtype == typeof(uint))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToUInt32(data, i * dataPointSize);
                    }
                    return ret;
                }
                else if (dtype == typeof(ulong))
                {
                    for (int i = 0; i < ret.Length; i++)
                    {
                        ret[i] = BitConverter.ToUInt64(data, i * dataPointSize);
                    }
                    return ret;
                }
                else
                {
                    throw new InvalidOperationException("UDP data type (defined in second sent byte) not compatible! Needs to be one of [float, double, long, ulong, int, uint, short, ushort, byte, sbyte]!");
                }
            }
        }

        // timestamp as double
        private static double getTimestamp(ArraySegment<byte> byteTime)
        {
            return BitConverter.ToDouble(byteTime);
        }

        // deetrmine type of the values sent in an udp message
        private static Type getType(byte typeInfo)
        {
            // float vs integer
            if((typeInfo & 0b_0010_0000) != 0)
            {
                // floating point
                // num bytes
                switch (typeInfo & 0b_0000_1111)
                {
                    case 4:
                        return typeof(float);
                    case 8:
                        return typeof(double);
                    default:
                        throw new InvalidOperationException("UDP data type (defined in second sent byte) not compatible! Needs to be one of [float, double, long, ulong, int, uint, short, ushort, byte, sbyte]!");
                }
            }
            // integer
            else
            {
                // signed vs. unsigned
                if ((typeInfo & 0b_0001_0000) != 0)
                {
                    // signed
                    // num bytes
                    switch (typeInfo & 0b_0000_1111)
                    {
                        case 1:
                            return typeof(sbyte);
                        case 2:
                            return typeof(short);
                        case 4:
                            return typeof(int);
                        case 8:
                            return typeof(long);
                        default:
                            throw new InvalidOperationException("UDP data type (defined in second sent byte) not compatible! Needs to be one of [float, double, long, ulong, int, uint, short, ushort, byte, sbyte]!");
                    }
                } 
                // unsigned
                else
                {
                    // num bytes
                    switch (typeInfo & 0b_0000_1111)
                    {
                        case 1:
                            return typeof(byte);
                        case 2:
                            return typeof(ushort);
                        case 4:
                            return typeof(uint);
                        case 8:
                            return typeof(ulong);
                        default:
                            throw new InvalidOperationException("UDP data type (defined in second sent byte) not compatible! Needs to be one of [float, double, long, ulong, int, uint, short, ushort, byte, sbyte]!");
                    }

                }
            }
        }
    }

    [System.Serializable]
    public class TrackerLog
    {
        public bool used;
        public position pos;
        public TrackerLog(position pos)
        {
            this.used = false;
            this.pos = pos;
        }
    }

    public enum position : uint
    {
        TrackerLeftFoot = 0x1000u,
        TrackerRightFoot = 0x2000u,
        TrackerLeftShoulder = 0x4000u,
        TrackerRightShoulder = 0x8000u,
        TrackerLeftElbow = 0x10000u,
        TrackerRightElbow = 0x20000u,
        TrackerLeftKnee = 0x40000u,
        TrackerRightKnee = 0x80000u,
        TrackerWaist = 0x100000u,
        TrackerChest = 0x200000u,
        TrackerCamera = 0x400000u,
        TrackerKeyboard = 0x800000u
    }

    partial class SIMHeader
    {
        static public string eyeHeader = 

        // timestamps
        "time_stamp_s" + "," + // from stopwatch
        "time_stamp_eyeData" + "," +

        // combined eye data
        "gaze_direction_combined_x" + "," +
        "gaze_direction_combined_y" + "," +
        "gaze_direction_combined_z" + "," +
        "gazeOriginCombinedX_mm" + "," +
        "gazeOriginCombinedY_mm" + "," +
        "gazeOriginCombinedZ_mm" + "," +
        "convergenceDistanceValidity" + "," +
        "convergenceDistance_mm" + "," +

        // right eye Data
        "eyeDataValidataBitMaskRight" + "," +
        "gazeDirectionRightX" + "," +
        "gazeDirectionRightY" + "," +
        "gazeDirectionRightZ" + "," +
        "pupilDiameterRight_mm" + "," +
        "eyeOpennessRight" + "," +
        "pupilPositionInSensorAreaRightX" + "," +
        "pupilPositionInSensorAreaRightY" + "," +

        // left eye Data
        "eyeDataValidataBitMaskLeft" + "," +
        "gazeDirectionLeftX" + "," +
        "gazeDirectionLeftY" + "," +
        "gazeDirectionLeftZ" + "," +
        "pupilDiameterLeft_mm" + "," +
        "eyeOpennessLeft" + "," +
        "pupilPositionInSensorAreaLeftX" + "," +
        "pupilPositionInSensorAreaLeftY" +

        Environment.NewLine;

        static public string headHeader =
        "time_stamp_s" + "," +
        "position_x" + "," +
        "position_y" + "," +
        "position_z" + "," +
        "rotation_w" + "," +
        "rotation_x" + "," +
        "rotation_y" + "," +
        "rotation_z" +
        Environment.NewLine;

        static public string leftControllerHeader =
        "time_stamp_s" + "," +
        "position_x" + "," +
        "position_y" + "," +
        "position_z" + "," +
        "rotation_w" + "," +
        "rotation_x" + "," +
        "rotation_y" + "," +
        "rotation_z" +
        Environment.NewLine;

        static public string rightControllerHeader =
        "time_stamp_s" + "," +
        "position_x" + "," +
        "position_y" + "," +
        "position_z" + "," +
        "rotation_w" + "," +
        "rotation_x" + "," +
        "rotation_y" + "," +
        "rotation_z" +
        Environment.NewLine;


        static public string trackerHeader = 
        "time_stamp(ms)" +
        Environment.NewLine;

        static public string udpHeader =
        "time_stamp_s" + "," +
        "time_stamp(imBlocks)" + "," +
        "category" + "," +
        "subcategory" + "," +
        "data" + "," +
        "counter" +
        Environment.NewLine;

        static public string roiHeader = "time_stamp_s" + "," + "hand" + "," + "object" + "," + "target" + ","+ "hitProperties" + Environment.NewLine;

    }

    //log bools and UDP port
    public bool activateLogging;
    public static bool S_activateLogging;

    //public static Logging Logger = null;
    public static bool isEyeCallbackWorking;
    public bool receiveUdp;
    public double lastUdpTimestamp;
    public int availableUDPSamples;
    // public string IP = "127.0.0.1"; default local
    public int port; // define > init
    public bool trackEyes; 
    public static bool trackEyesCallback;
    public static int last_eyeData_size = 100;

    public bool trackHead;
    public bool trackLeftController;
    public bool trackRightController;
    public List<TrackerLog> usedTrackers = new List<TrackerLog>();

    
    private static bool eye_callback_registered = false;

    private static readonly object eyeLock = new object();

    // Cone cast would be better, but good enough with relatively fixed distance to regions of interest
    public float spherecastRadius;


    // sphere that follows eye
    public GameObject eyeBall;

    // camera reference
    public GameObject main_camera;

    // time
    private static double stopwatchTime;
    private static Stopwatch stopWatch;
    private static double startTimeUnix = 0;
    // private static double lastUpdateTime = 0;

    // devices
    private static List<InputDevice> headDevices = new List<InputDevice>();
    private static List<InputDevice> rightControllerDevices = new List<InputDevice>();
    private static List<InputDevice> leftControllerDevices = new List<InputDevice>();
    private static List<InputDevice> trackDevices = new List<InputDevice>();

    // tracking instances
    public static HeadTracking headTracker = new HeadTracking();
    public static LeftControllerTracking leftControllerTracker = new LeftControllerTracking();
    public static RightControllerTracking rightControllerTracker = new RightControllerTracking();
    public static EyeTracking eyeTracker = new EyeTracking();
    public static Dictionary<string, TrackerTracking> trackerTrackers = new Dictionary<string, TrackerTracking>();
    public static ReceivingUdp udpReceiver = new ReceivingUdp();

    //tracking buffer
    //private string headBuffer;
    //private string leftControllerBuffer;
    //private string rightControllerBuffer;
    //private static string eyeBuffer;
    //private string trackerBuffer;
    //private string roiBuffer;
    // udp buffer
    //private string udpBuffer;



    // udp lock
    private static readonly object udpLock = new object();

    // receiving Thread
    Thread receiveThread;

    // udpclient object
    UdpClient client;

    // init of the vr devices
    // initialization needs to be delayed a little (so that all devices are registered)
    // this bool is set to true as soon as init is done
    private static bool vrInitDone = false;
    private static bool udpInitDone = false;

    // Now - Use the Windows Stopwatch class to obtain precise timestamps
    public static double Now
    {
        get
        {
            if (stopWatch == null) { stopWatch = Stopwatch.StartNew(); startTimeUnix = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds; }
            return startTimeUnix + (double)stopWatch.ElapsedTicks / (double)Stopwatch.Frequency;
        }
    }

    public static bool lookingAtHand { get; private set; } = false;
    public static bool lookingAtObject { get; private set; } = false;
    public static bool lookingAtTarget { get; private set; } = false;
    public static bool eyeValid { get; private set; } = false;
    public static bool eyeValid2 { get; private set; } = false;

    



    // initialize the devices such that this is only done once per program start
    // called within Start()
    private void initDevices()
    {
        // only for debugging purposes / overview of VR devices
        //var inputDevices = new List<UnityEngine.XR.InputDevice>();
        //InputDevices.GetDevices(inputDevices);

        //foreach (var device in inputDevices)
        //{
        //    UnityEngine.Debug.Log(string.Format("Device found with name '{0}' and role '{1}'", device.name, device.characteristics.ToString()));
        //}

        var desiredCharacteristics = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller;
        InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, leftControllerDevices);
        desiredCharacteristics = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller;
        InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, rightControllerDevices);
        desiredCharacteristics = InputDeviceCharacteristics.HeadMounted;
        InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, headDevices);
        InputDevices.GetDevicesAtXRNode(XRNode.HardwareTracker, trackDevices);

        // we want one dict entry for every used tracker
        for (int i = 0; i < usedTrackers.Count; i++)
        {
            if (usedTrackers[i].used)
            {
                trackerTrackers.Add(usedTrackers[i].pos.ToString(), new TrackerTracking());
            }
        }

        // How to access a specific tracker: InputDevices.GetDevicesWithCharacteristics((InputDeviceCharacteristics) 0x20000u, trackDevices);
        vrInitDone = true;
    }

    // initialize for Udp
    private void initUdp()
    {
        // Endpunkt definieren, von dem die Nachrichten gesendet werden.
        print("UDPSend.init()");

        // status
        print("Sending to 127.0.0.1 : " + port);


        client = new UdpClient(port);

        receiveThread = new Thread(
            new ThreadStart(StartUDPlistener));
        receiveThread.IsBackground = true;
        receiveThread.Start();

        udpInitDone = true;    }

    private void StartUDPlistener()
    {
        try
        {
            // this starts recursive(!) async loop
            client.BeginReceive(new AsyncCallback(recv), null);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }

    }

    //Recursive CallBack!
    void recv(IAsyncResult res)
    {
        // receives from any ip and any port, which send to this port 11000 (as this is only receiving, no need to specify sender for now)
        IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
        
        // client will be null after stopping the scene
        if (client != null)
        {
            byte[] received = client.EndReceive(res, ref RemoteIpEndPoint);

            // watch out for next sample
            client.BeginReceive(new AsyncCallback(recv), null);

            UdpObject receivedUdpObject = new UdpObject(received);

            UInt16 categoryKey = BitConverter.ToUInt16(received, 2);

            lock (udpLock)
            {
                // change the latest udp information and lock data access object during doing so
                if (!udpReceiver.lastReceivedUDPPackets.ContainsKey(categoryKey))
                {
                    udpReceiver.lastReceivedUDPPackets.Add(categoryKey, new List<UdpObject>());
                }

                // now it should contain it. 
                udpReceiver.lastReceivedUDPPackets[categoryKey].Add(receivedUdpObject);
                udpReceiver.lastReceivedUDPTS = receivedUdpObject.timestamp;

                while (udpReceiver.lastReceivedUDPPackets[categoryKey].Count > availableUDPSamples)
                {
                    udpReceiver.lastReceivedUDPPackets[categoryKey].RemoveAt(0);
                }
            }

            // udpReceiver.allReceivedUDPPackets.Add(receivedUdpObject);

            // log the udp messages (timestamp, category, subCategory, data, counter)
            if (S_activateLogging)
            {
                string addBuffer = Now + "," + receivedUdpObject.timestamp.ToString() + "," + receivedUdpObject.category.ToString() + "," + receivedUdpObject.subCategory.ToString() + "," + "[" + String.Join(" ", new List<object>(receivedUdpObject.Data).ConvertAll(i => i.ToString()).ToArray()) + "]" + "," + receivedUdpObject.counter.ToString()
                + Environment.NewLine;
                LoggingManager.AddToBuffer("UDP_Received", addBuffer);
            }
            //client.BeginReceive(new AsyncCallback(recv), null);
        }

    }

    private void Start()
    {
        S_activateLogging = activateLogging;

        if (S_activateLogging)
        {
            // adds header for all used trackers
            for (int i = 0; i < usedTrackers.Count; i++)
            {
                if (usedTrackers[i].used)
                {
                    LoggingManager.addTracker(usedTrackers[i].pos.ToString());
                }
            }
            LoggingManager.CreateNewLog(fileName: "UDP_Received", header: SIMHeader.udpHeader, logRate: 5f);
            LoggingManager.CreateNewLog(fileName: "Eye_Tracking", header: SIMHeader.eyeHeader, logRate: 5f);
            LoggingManager.CreateNewLog(fileName: "RightController_Tracking", header: SIMHeader.rightControllerHeader, logRate: 5f);
            LoggingManager.CreateNewLog(fileName: "LeftController_Tracking", header: SIMHeader.leftControllerHeader, logRate: 5f);
            LoggingManager.CreateNewLog(fileName: "Tracker_Tracking", header: SIMHeader.trackerHeader, logRate: 5f);
            LoggingManager.CreateNewLog(fileName: "HMD_Tracking", header: SIMHeader.headHeader, logRate: 5f);
            LoggingManager.CreateNewLog(fileName: "ROI", header: SIMHeader.roiHeader, logRate: 5f);
        }

        //init devices
        Invoke("initDevices", 3f);

        //start listening over udp port 11000
        if (receiveUdp)
            initUdp();

        double imBlockTimestamp = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        UnityEngine.Debug.Log("Starting the application at " + imBlockTimestamp);
    }

    void Update()
    {
        // make logEye bool accessible for Eye data callback function
        trackEyesCallback = trackEyes;

        // Eyetracking with SRanipal
        if (SRanipal_Eye_Framework.Status == SRanipal_Eye_Framework.FrameworkStatus.WORKING)
        {
            if (SRanipal_Eye_Framework.Instance.EnableEyeDataCallback == true && eye_callback_registered == false)
            {
                SRanipal_Eye.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback));
                eye_callback_registered = true;
            }
            else if (SRanipal_Eye_Framework.Instance.EnableEyeDataCallback == false && eye_callback_registered == true)
            {
                SRanipal_Eye.WrapperUnRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback));
                eye_callback_registered = false;
            }

            foreach ((double, EyeData) eyeTimeData in eyeTracker.Last_eyeDatas)
            {
                if (vrInitDone && eye_callback_registered) TrackingROI(eyeTimeData.Item1, eyeTimeData.Item2);
            }

            // Debug view eye tracking
            eyeBall.transform.position = eyeTracker.debugPos;
            eyeBall.transform.rotation = eyeTracker.debugOri;

            eyeValid = eyeTracker.EyeData.Item2.verbose_data.left.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY);
            eyeValid2 = eyeTracker.EyeData.Item2.verbose_data.right.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY);

        }
        // All other tracking done with openXR
        if (vrInitDone == true)
        {
            if (trackHead)
                trackingHead();
            if (trackLeftController)
                trackingLeftController();
            if (trackRightController)
                trackingRightController();
            foreach (var tracker in usedTrackers)
            {
                if (tracker.used)
                {
                    trackingTrackers();
                    break;
                }
            }
        }

        // two checks if udpReceive state has changed since start of application
        if(receiveUdp & !udpInitDone)
            initUdp();

        if (!receiveUdp & udpInitDone)
        {
            receiveThread.Abort();
            client.Close();
            udpInitDone = false;
        }
            
        // change the position of the eyeBall to visualize the functionality eye tracking
        //Vector3 gazeDirection = eyeData.verbose_data.combined.eye_data.gaze_direction_normalized.normalized;
        //gazeDirection.x = -gazeDirection.x;
        //Vector3 direction = main_camera.transform.rotation * gazeDirection * 1;
        //Vector3 newPosition = main_camera.transform.position + eyeData.verbose_data.combined.eye_data.gaze_origin_mm * 0.001f + direction;
        //eyeBall.transform.position = newPosition;
}

    // unregister eye data callback and end receiving over udp when necessary
    private void OnDisable()
    {
        // eye callback
        Release();

        // udp
        if (receiveThread != null)
            receiveThread.Abort();

        if (client != null)
        {
            client.Close();
            client = null;
        }
            
    }

    void OnApplicationQuit()
    {
        // eye callback
        Release();

        // udp
        if (receiveThread != null)
            receiveThread.Abort();

        if (client != null)
        {
            client.Close();
            client = null;
        }

        // log a final time (out of cycle)
        // logWrapper();
    }

    /// <summary>
    /// Release callback thread when disabled or quit
    /// </summary>
    private static void Release()
    {
        if (eye_callback_registered == true)
        {
            SRanipal_Eye.WrapperUnRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate((SRanipal_Eye.CallbackBasic)EyeCallback));
            eye_callback_registered = false;
        }
    }

    /// <summary>
    /// Required class for IL2CPP scripting backend support
    /// </summary>
    internal class MonoPInvokeCallbackAttribute : System.Attribute
    {
        public MonoPInvokeCallbackAttribute() { }
    }

    /// <summary>
    /// Eye tracking data callback thread.
    /// Reports data at ~120hz
    /// MonoPInvokeCallback attribute required for IL2CPP scripting backend
    /// </summary>
    /// <param name="eye_data">Reference to latest eye_data</param>
    [MonoPInvokeCallback]
    private static void EyeCallback(ref EyeData eye_data)
    {
        if (isEyeCallbackWorking == false)
        {
            isEyeCallbackWorking = true;
            UnityEngine.Debug.Log("Eye Callback is working!");
        }

        lock (eyeLock)
        {        
            double lastUpdateTime = Now;
            
            // eye data: timestamp, eyedata
            eyeTracker.EyeData = (lastUpdateTime, eye_data);

            if (S_activateLogging)
            {
                string addBuffer = lastUpdateTime.ToString() + "," + eye_data.timestamp.ToString() + "," + eye_data.verbose_data.combined.eye_data.gaze_direction_normalized.normalized.x.ToString() + "," + eye_data.verbose_data.combined.eye_data.gaze_direction_normalized.normalized.y.ToString() + "," + eye_data.verbose_data.combined.eye_data.gaze_direction_normalized.normalized.z.ToString() + "," + eye_data.verbose_data.combined.eye_data.gaze_origin_mm.normalized.x.ToString() + "," + eye_data.verbose_data.combined.eye_data.gaze_origin_mm.normalized.y.ToString() + "," + eye_data.verbose_data.combined.eye_data.gaze_origin_mm.normalized.z.ToString() + "," + eye_data.verbose_data.combined.convergence_distance_validity.ToString() + "," + eye_data.verbose_data.combined.convergence_distance_mm.ToString() + "," +
                    eye_data.verbose_data.right.eye_data_validata_bit_mask.ToString() + "," + eye_data.verbose_data.right.gaze_direction_normalized.normalized.x.ToString() + "," + eye_data.verbose_data.right.gaze_direction_normalized.normalized.y.ToString() + "," + eye_data.verbose_data.right.gaze_direction_normalized.normalized.z.ToString() + "," + eye_data.verbose_data.right.pupil_diameter_mm.ToString() + "," + eye_data.verbose_data.right.eye_openness.ToString() + "," + eye_data.verbose_data.right.pupil_position_in_sensor_area.x.ToString() + "," + eye_data.verbose_data.right.pupil_position_in_sensor_area.y.ToString() + "," + eye_data.verbose_data.left.eye_data_validata_bit_mask.ToString() + "," + eye_data.verbose_data.left.gaze_direction_normalized.normalized.x.ToString() + "," + eye_data.verbose_data.left.gaze_direction_normalized.normalized.y.ToString() + "," + eye_data.verbose_data.left.gaze_direction_normalized.normalized.z.ToString() + "," +
                    eye_data.verbose_data.left.pupil_diameter_mm.ToString() + "," + eye_data.verbose_data.left.eye_openness.ToString() + "," + eye_data.verbose_data.left.pupil_position_in_sensor_area.x.ToString() + "," + eye_data.verbose_data.left.pupil_position_in_sensor_area.y.ToString() +
                    Environment.NewLine;
                LoggingManager.AddToBuffer("Eye_Tracking", addBuffer);
            }
        }
    }

    // access head tracking data and write to file
    private void trackingHead()
    {
        UnityEngine.Debug.Assert(headDevices.Count != 0, "No HEAD Device has been found!");
        UnityEngine.Debug.Assert(headDevices.Count == 1, "More than one HEAD Device has been found!");

        headDevices[0].TryGetFeatureValue(CommonUsages.devicePosition, out headTracker.headPosition);
        headDevices[0].TryGetFeatureValue(CommonUsages.deviceRotation, out headTracker.headRotation);

        double lastUpdateTime = Now;
        headTracker.headTimestamp = lastUpdateTime;

        if (S_activateLogging)
        {
            string addBuffer = lastUpdateTime.ToString() + "," + headTracker.headPosition.x.ToString() + "," + headTracker.headPosition.y.ToString() + "," + headTracker.headPosition.z.ToString() + "," + headTracker.headRotation.w.ToString() + "," + headTracker.headRotation.x.ToString() + "," + headTracker.headRotation.y.ToString() + "," + headTracker.headRotation.z.ToString()
            + Environment.NewLine;
            LoggingManager.AddToBuffer("HMD_Tracking", addBuffer);
        }
    }

    // access left controller tracking data and write to file
    private void trackingLeftController()
    {
        UnityEngine.Debug.Assert(leftControllerDevices.Count != 0, "No Left Controller Devices has been found!");
        UnityEngine.Debug.Assert(leftControllerDevices.Count <= 1, "More than one LEFT CONTROLLER Device has been found!");

        leftControllerDevices[0].TryGetFeatureValue(CommonUsages.devicePosition, out leftControllerTracker.leftControllerPosition);
        leftControllerDevices[0].TryGetFeatureValue(CommonUsages.deviceRotation, out leftControllerTracker.leftControllerRotation);

        double lastUpdateTime = Now;
        leftControllerTracker.leftControllerTimestamp = lastUpdateTime;

        if (S_activateLogging)
        {
            string addBuffer = lastUpdateTime.ToString() + "," + leftControllerTracker.leftControllerPosition.x.ToString() + "," + leftControllerTracker.leftControllerPosition.y.ToString() + "," + leftControllerTracker.leftControllerPosition.z.ToString() + "," + leftControllerTracker.leftControllerRotation.w.ToString() + "," + leftControllerTracker.leftControllerRotation.x.ToString() + "," + leftControllerTracker.leftControllerRotation.y.ToString() + "," + leftControllerTracker.leftControllerRotation.z.ToString()
                + Environment.NewLine;
            LoggingManager.AddToBuffer("LeftController_Tracking", addBuffer);
        }
    }

    // access right controller tracking data and write to file
    private void trackingRightController()
    {
        UnityEngine.Debug.Assert(rightControllerDevices.Count != 0, "No Right Controller Devices has been found!");
        UnityEngine.Debug.Assert(rightControllerDevices.Count <= 1, "More than one RIGHT CONTROLLER Device has been found!");

        rightControllerDevices[0].TryGetFeatureValue(CommonUsages.devicePosition, out rightControllerTracker.rightControllerPosition);
        rightControllerDevices[0].TryGetFeatureValue(CommonUsages.deviceRotation, out rightControllerTracker.rightControllerRotation);

        double lastUpdateTime = Now;
        rightControllerTracker.rightControllerTimestamp = lastUpdateTime;

        if (S_activateLogging)
        {
            string addBuffer = lastUpdateTime.ToString() + "," + rightControllerTracker.rightControllerPosition.x.ToString() + "," + rightControllerTracker.rightControllerPosition.y.ToString() + "," + rightControllerTracker.rightControllerPosition.z.ToString() + "," + rightControllerTracker.rightControllerRotation.w.ToString() + "," + rightControllerTracker.rightControllerRotation.x.ToString() + "," + rightControllerTracker.rightControllerRotation.y.ToString() + "," + rightControllerTracker.rightControllerRotation.z.ToString()
                + Environment.NewLine;
            LoggingManager.AddToBuffer("RightController_Tracking", addBuffer);
        }
    }

    // access tracking data of external trackers and write to file
    private void trackingTrackers()
    {
        UnityEngine.Debug.Assert(trackDevices.Count != 0, "No TRACKER Devices has been found!");
        UnityEngine.Debug.Assert(trackDevices.Count == usedTrackers.Count, "More or less than 12 tracking devices have been found! Check that there are only 12 devices listed in the inspector! Otherwise, this is new behaviour and requires a code update!");

        double lastUpdateTime = Now;

        // Iterate over all the trackers
        for (int i = 0; i < usedTrackers.Count; ++i)
        {
            // Check if the corresponding tracker is supposed to be tracked (box checked in insprector)
            if (usedTrackers[i].used)
            {
                trackDevices[i].TryGetFeatureValue(CommonUsages.devicePosition, out trackerTrackers[usedTrackers[i].pos.ToString()].trackerPosition);
                trackDevices[i].TryGetFeatureValue(CommonUsages.deviceRotation, out trackerTrackers[usedTrackers[i].pos.ToString()].trackerRotation);

                if (S_activateLogging)
                {
                    string addBuffer = lastUpdateTime.ToString() + "," + trackerTrackers[usedTrackers[i].pos.ToString()].trackerPosition.x.ToString() + "," + trackerTrackers[usedTrackers[i].pos.ToString()].trackerPosition.y.ToString() + "," + trackerTrackers[usedTrackers[i].pos.ToString()].trackerPosition.z.ToString() + "," + trackerTrackers[usedTrackers[i].pos.ToString()].trackerRotation.w.ToString() + "," + trackerTrackers[usedTrackers[i].pos.ToString()].trackerRotation.x.ToString() + "," + trackerTrackers[usedTrackers[i].pos.ToString()].trackerRotation.y.ToString() + "," + trackerTrackers[usedTrackers[i].pos.ToString()].trackerRotation.z.ToString() + Environment.NewLine;
                    LoggingManager.AddToBuffer(usedTrackers[i].pos.ToString(), addBuffer);
                }
            }
        }
    }

    private void TrackingROI(double timestamp, EyeData eyedata)
    {
        // Use layer mask 10 for regions of interest
        var layerMask = 1 << 10;

        var anipalOrigin = eyedata.verbose_data.combined.eye_data.gaze_origin_mm * 0.001f;
        var leftHandedOrigin = new Vector3(-anipalOrigin.x, anipalOrigin.y, anipalOrigin.z);

        // Changed to just using camera as const offset of leftHandedOrigin
        // var origin = main_camera.transform.position + leftHandedOrigin;

        var origin = main_camera.transform.position;
        var anipalDirection = eyedata.verbose_data.combined.eye_data.gaze_direction_normalized;
        var leftHandedDirection = new Vector3(-anipalDirection.x, anipalDirection.y, anipalDirection.z);
        var worldSpaceDirection = main_camera.transform.rotation * leftHandedDirection;

        var hits = Physics.SphereCastAll(origin, spherecastRadius, worldSpaceDirection, 10f, layerMask);

        lookingAtHand = hits.Any(hit => hit.collider.CompareTag("hand_collider"));
        lookingAtObject = hits.Any(hit => hit.collider.CompareTag("object_collider"));
        lookingAtTarget = hits.Any(hit => hit.collider.CompareTag("target_collider"));

        // log all objects which are "hits" and their distance
        string hitsProperties = "";

        if (hits.Length > 0)
        {
            foreach (var hit in hits)
            {
                hitsProperties += "[" + hit.collider.gameObject.name + ";" + hit.distance.ToString() + "] ";
            }
            hitsProperties = hitsProperties.Remove(hitsProperties.Length - 1);
        }
        hitsProperties = hitsProperties.Insert(0, "[");
        hitsProperties = hitsProperties.Insert(hitsProperties.Length, "]");

        string addBuffer = $"{timestamp},{lookingAtHand},{lookingAtObject},{lookingAtTarget},{hitsProperties}{Environment.NewLine}";
        LoggingManager.AddToBuffer("ROI", addBuffer);

        // Debug view
        eyeTracker.debugPos = origin + (worldSpaceDirection * 0.5f);
        eyeTracker.debugOri = Quaternion.LookRotation(worldSpaceDirection);
    }

    public void CalibrateEyes()
    {
        SRanipal_Eye_v2.LaunchEyeCalibration();
    }
}
