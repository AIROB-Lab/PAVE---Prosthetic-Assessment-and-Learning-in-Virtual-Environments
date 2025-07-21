using Mujoco;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PubSensorData : MonoBehaviour
{
    public bool logging;
    public bool UdpSending;
    public byte SIM_SensorCategory;

    [Serializable]
    public class SensorInfo
    {
        public GameObject GO;
        public byte Subcategory;
        public Vector3 lastValue;
    }
    public List<SensorInfo> sensorList = new List<SensorInfo>();

    // Start is called before the first frame update
    void Start()
    {
        string header = "time_stamp_s" + "," + "sensor_name" + "," + "ForceX" + "," + "ForceY" + "," + "ForceZ" + Environment.NewLine;
        LoggingManager.CreateNewLog("ForceLogs", header);
    }

    // Update is called once per frame
    private void Update()
    {
        for (int i = 0; i < sensorList.Count; i++)
        {
            Vector3 sensorReading = new(); 
            string name = sensorList[i].GO.name;
            var sensorComp = sensorList[i].GO.GetComponent<MjSiteVectorSensor>();

            if (sensorComp != null)
            {
                sensorReading = sensorComp.SensorReading;
                sensorList[i].lastValue = sensorReading;
            }

            else // meaning this is a scalar sensor
            {
                var sensorComp2 = sensorList[i].GO.GetComponent<MjSiteScalarSensor>();
                double sensorReadingf = sensorComp2.SensorReading;
                sensorReading = new Vector3(0, (float)sensorReadingf, 0);
            }
            if (logging)
            {
                string message = $"{StreamlinedInputManager.Now},{name},{sensorReading.x},{sensorReading.y},{sensorReading.z}" + Environment.NewLine;
                LoggingManager.AddToBuffer("ForceLogs", message);
            }

            if (UdpSending)
            {
                SimUdpSender.SendArrayAsUDPmessage(new double[] { sensorReading.x, sensorReading.y, sensorReading.z }, (SIM_SensorCategory, sensorList[i].Subcategory));
            }
        }
    }

    public Vector3[] GetAllSensorDataAsArray()
    {
        List<Vector3> sensorVectors = new ();
        for (int i = 0; i < sensorList.Count; i++)
        {
            sensorVectors.Add(sensorList[i].lastValue);
        }

        return sensorVectors.ToArray();
    }

    public float GetSumOfAllSensorData(bool componentsX = true, bool componentsY = true, bool componentsZ = true)
    {
        Vector3[] allVectors = GetAllSensorDataAsArray();

        float sum = 0;

        for (int i = 0; i < allVectors.Length; i++)
        {
            if(componentsX) sum += allVectors[i].x;
            if(componentsX) sum += allVectors[i].y;
            if(componentsZ) sum += allVectors[i].z;
        }

        return sum;

    }
}
