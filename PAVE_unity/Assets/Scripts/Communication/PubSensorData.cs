using System;
using System.Collections.Generic;
using System.Globalization;
using Mujoco;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>Reads supported MuJoCo sensors and optionally publishes/logs their measured values.
/// Scalar has one value, vectors xyz and quaternions xyzw; quantities retain the sensor's units.</summary>
public class PubSensorData : MonoBehaviour
{
    [FormerlySerializedAs("logging")]
    public bool LOGGING;
    [FormerlySerializedAs("UdpSending")]
    public bool UDP_SENDING;
    public bool SEND_WITH_LAST_UDP_TS;
    public byte SIM_SensorCategory;
    [Serializable]
    public class SensorInfo
    {
        public GameObject GO;
        public byte Subcategory;
        [NonSerialized]
        public double[] lastValue = Array.Empty<double>();
        [NonSerialized]
        internal MjBaseSensor component;
    }

    public List<SensorInfo> sensorList = new List<SensorInfo>();
    void Start()
    {
        if (LOGGING)
        {
            LoggingManager.CreateNewLog("SensorLogs", "time_stamp_s,sensor_name,values\n");
        }

        foreach (var item in sensorList)
        {
            item.component = item.GO == null ? null : item.GO.GetComponent<MjBaseSensor>();
            if (item.component == null)
            {
                Debug.LogWarning("Missing sensor in PubSensorData.", this);
            }
        }
    }

    void Update()
    {
        foreach (var item in sensorList)
        {
            item.lastValue = Read(item.component);
            if (item.lastValue.Length == 0)
            {
                continue;
            }

            if (LOGGING)
            {
                string values = string.Join(" ", Array.ConvertAll(item.lastValue, x => x.ToString("R", CultureInfo.InvariantCulture)));
                string name = item.GO.name.Replace("\"", "\"\"");
                LoggingManager.AddToBuffer("SensorLogs", FormattableString.Invariant($"{Sim.SimClock.Now},\"{name}\",[{values}]\n"));
            }

            if (UDP_SENDING)
            {
                SimUdpSender.SendArrayAsUDPmessage(item.lastValue, (SIM_SensorCategory, item.Subcategory), SEND_WITH_LAST_UDP_TS);
            }
        }
    }

    static double[] Read(MjBaseSensor sensor)
    {
        switch (sensor)
        {
            case MjJointScalarSensor s:
                return new[]
                {
                    s.SensorReading
                };
            case MjSiteScalarSensor s:
                return new[]
                {
                    s.SensorReading
                };
            case MjActuatorScalarSensor s:
                return new[]
                {
                    s.SensorReading
                };
            case MjSiteVectorSensor s:
                return Vector(s.SensorReading);
            case MjBodyVectorSensor s:
                return Vector(s.SensorReading);
            case MjGeomVectorSensor s:
                return Vector(s.SensorReading);
            case MjSiteQuaternionSensor s:
                return Quaternion(s.SensorReading);
            case MjBodyQuaternionSensor s:
                return Quaternion(s.SensorReading);
            case MjGeomQuaternionSensor s:
                return Quaternion(s.SensorReading);
            default:
                return Array.Empty<double>();
        }
    }

    static double[] Vector(Vector3 v) => new double[]
    {
        v.x,
        v.y,
        v.z
    };
    static double[] Quaternion(UnityEngine.Quaternion q) => new double[]
    {
        q.x,
        q.y,
        q.z,
        q.w
    };
    public List<double[]> GetAllSensorDataAsArray()
    {
        var result = new List<double[]>();
        foreach (var item in sensorList)
        {
            result.Add(item.lastValue);
        }

        return result;
    }

    /// <summary>Select xyz terms; a scalar uses X. Quaternion W is not part of a vector-force sum.</summary>
    public float GetSumOfAllSensorData(bool componentsX = true, bool componentsY = true, bool componentsZ = true)
    {
        double sum = 0;
        foreach (var item in sensorList)
        {
            var v = item.lastValue;
            if (v.Length > 0 && componentsX)
            {
                sum += v[0];
            }

            if (v.Length > 1 && componentsY)
            {
                sum += v[1];
            }

            if (v.Length > 2 && componentsZ)
            {
                sum += v[2];
            }
        }

        return (float)sum;
    }
}
