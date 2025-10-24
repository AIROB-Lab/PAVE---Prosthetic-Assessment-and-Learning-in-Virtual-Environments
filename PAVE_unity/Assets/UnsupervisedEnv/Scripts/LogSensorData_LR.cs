using Mujoco;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using UnityEngine;

public class LogSensorData : MonoBehaviour
{
    public List<GameObject> sensorVectorList = new List<GameObject>();
    public List<GameObject> sensorScalarList = new List<GameObject>();
    // Start is called before the first frame update
    void Start()
    {
        string vectorHeader = "time_stamp_s" + "," + "sensor_name" + "," + "ForceX" + "," + "ForceY" + "," + "ForceZ" + Environment.NewLine;
        string scalarHeader = "time_stamp_s" + "," + "sensor_name" + "," + "Value" + Environment.NewLine;
        LoggingManager.CreateNewLog("SensorForceLogs", vectorHeader);
        LoggingManager.CreateNewLog("SensorTouchLogs", scalarHeader);
    }

    // Update is called once per frame
    private void Update()
    {
        foreach ( var sensorGO in sensorVectorList)
        {
            var sensorCompVector = sensorGO.GetComponent<MjSiteVectorSensor>();

            if (sensorCompVector != null)
            {
                string name = sensorGO.name;
                float forceX = sensorCompVector.SensorReading[0];
                float forceY = sensorCompVector.SensorReading[1];
                float forceZ = sensorCompVector.SensorReading[2];

                string message = $"{StreamlinedInputManager.Now},{name},{forceX},{forceY},{forceZ}," + Environment.NewLine;
                LoggingManager.AddToBuffer("SensorForceLogs", message);
            }
        }
        foreach (var sensorGO in sensorScalarList)
        {
            var sensorCompScalar = sensorGO.GetComponent<MjSiteScalarSensor>();
            if (sensorCompScalar != null)
            {
                string name = sensorGO.name;
                double scalarValue = sensorCompScalar.SensorReading;

                string message = $"{StreamlinedInputManager.Now},{name},{scalarValue}" + Environment.NewLine;
                LoggingManager.AddToBuffer("SensorTouchLogs", message);
            }
        }




    }
}