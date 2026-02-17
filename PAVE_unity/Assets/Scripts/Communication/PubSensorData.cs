using Mujoco;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PubSensorData : MonoBehaviour
{
    public bool LOGGING;
    public bool UDP_SENDING;
    public bool SEND_WITH_LAST_UDP_TS;
    public byte SIM_SensorCategory;
    public Type[] SensorCategories = { typeof(MjBodyQuaternionSensor), typeof(MjBodyVectorSensor), typeof(MjActuatorScalarSensor), typeof(MjBaseSensor), typeof(MjGeomQuaternionSensor), typeof(MjGeomVectorSensor), typeof(MjJointScalarSensor), typeof(MjSiteQuaternionSensor), typeof(MjSiteScalarSensor), typeof(MjSiteVectorSensor),typeof(MjUserSensor) };

    [Serializable]
    public class SensorInfo
    {
        public GameObject GO;
        public byte Subcategory;
        // public bool active;                                 // could be used to send sensor FB or not -> has to be further implemented
        public double[] lastValue;

        // has to be filled dynamicly with different sensor objects from different classes
        public dynamic SensorComponent = null;

    }
    public List<SensorInfo> sensorList = new List<SensorInfo>();

    // Start is called before the first frame update
    void Start()
    {
        string header = "time_stamp_s" + "," + "sensor_name" + "," + "ForceX" + "," + "ForceY" + "," + "ForceZ" + Environment.NewLine;
        LoggingManager.CreateNewLog("ForceLogs", header);

        GetAllSensorTypes();
    }

    /// <summary>
    /// Check all corresponding types for our sensors. If one is applicable save the component for direct access
    /// </summary>
    private void GetAllSensorTypes()
    {
        for (int i = 0; i < sensorList.Count; i++)
        {
            for (int j = 0; j < SensorCategories.Length; j++)
            {
                if (sensorList[i].GO.GetComponent(SensorCategories[j]) != null)
                {
                    sensorList[i].SensorComponent = sensorList[i].GO.GetComponent(SensorCategories[j]);
                }
            }
        }
    }

    // Update is called once per frame
    private void Update()
    {
        double[] sensorReading = {};

        for (int i = 0; i < sensorList.Count; i++)
        {
            switch (sensorList[i].SensorComponent)
            {
                case MjJointScalarSensor s:
                    sensorReading = new double[] { s.SensorReading};
                    break;
                case MjSiteVectorSensor s:
                    sensorReading = new double[] { s.SensorReading.x, s.SensorReading.y, s.SensorReading.z };
                    break;
                case MjSiteQuaternionSensor s:
                    sensorReading = new double[] { s.SensorReading.x, s.SensorReading.y, s.SensorReading.z, s.SensorReading.w };
                    break;
            }

            sensorList[i].lastValue = sensorReading;

            string name = sensorList[i].GO.name;

            if (LOGGING)
            {
                string values = string.Join(",", sensorReading);
                string message = $"{StreamlinedInputManager.Now},{name},{values}{Environment.NewLine}"; 
                LoggingManager.AddToBuffer("SensorLogs", message);
            }

            if (UDP_SENDING)
            {
                SimUdpSender.SendArrayAsUDPmessage(sensorReading, (SIM_SensorCategory, sensorList[i].Subcategory), SEND_WITH_LAST_UDP_TS);
            }
        }
    }

    public List<double[]> GetAllSensorDataAsArray()
    {
        List<double[]> sensorVectors = new ();
        for (int i = 0; i < sensorList.Count; i++)
        {
            
            sensorVectors.Add(sensorList[i].lastValue);
        }

        return sensorVectors;
    }

    public float GetSumOfAllSensorData(bool componentsX = true, bool componentsY = true, bool componentsZ = true)
    {
        List<double[]> allVectors = GetAllSensorDataAsArray();

        double sum = 0;

        for (int i = 0; i < allVectors.Count; i++)
        {
            for (int j = 0; j < allVectors[i].Length; j++)
            {
                sum += allVectors[i][j];
            }
        }

        return (float)sum;

    }
}
