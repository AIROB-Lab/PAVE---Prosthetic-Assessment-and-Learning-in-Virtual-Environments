using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;
using System;
using System.IO;
using Unity.Mathematics;
using System.Runtime.CompilerServices;


public class CollisionBall : MonoBehaviour
{
    public ParticleSystem particleSystemBall;
    public ParticleSystem particleSystemExplode;
    public TaskManager taskManager;
    public MjSiteScalarSensor[] forceSensors;
    

    public double currentTotalForce;

    private float emissionMax = 300f;
    private float emissionMin = 1f;
    private float sizeMax = 0.012f;
    private float sizeMin = 0.001f;
    public float successTime = 2f;

    public static float dynamicMaxForce = 0.1f;
    private float timeCount = 0;
    private float targetSize = 0.001f;


    //logging 
    private List<string[]> dataSensor = new List<string[]>(); // data rows for logging
    private List<string[]> dataFiltered = new List<string[]>(); // data rows for logging
    private List<string[]> dataForce = new List<string[]>(); // data rows for logging
    private List<string[]> dataEmission = new List<string[]>(); // data rows for logging
    private List<string[]> dataMaxForce = new List<string[]>(); // data rows for logging
    private List<string[]> dataSize = new List<string[]>(); // data rows for logging
    private float startTime;


    private double[] currentSensorData;
    private double[][] sensorHistory;
    private int windowSize = 100;
    private double[] filteredData;
    

    

    private bool ended = false;

    




    // Start is called before the first frame update
    void Start()
    {
        currentSensorData = new double[forceSensors.Length];
        filteredData = new double[forceSensors.Length];
        sensorHistory = new double[forceSensors.Length][];
        for(int i = 0; i< forceSensors.Length; i++)
        {
            sensorHistory[i] = new double[windowSize];
        }

        GameObject.Find("Ball_geom").GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");


        string header = "time_stamp_s";

        for(int i = 0; i< forceSensors.Length; i++)
        {
            header += "," + forceSensors[i].name.ToString();
        }
        header += Environment.NewLine;

       
        LoggingManager.CreateNewLog("Stage2FilteredSensor", header);
        LoggingManager.CreateNewLog("Stage2TotalForce", "time_stamp_s" + "," + "total_force" + "," + "dynamMaxForce" + Environment.NewLine);
        LoggingManager.CreateNewLog("Stage2Particle", "time_stamp_s" + "," + "emission_rate" + "," + "partice_size" + Environment.NewLine);
    }

    private void FixedUpdate()
    {
        // reset summation
        currentTotalForce = 0;

        //get current sensor data
        for (int i = 0; i < forceSensors.Length; i++)
        {
            currentSensorData[i] = forceSensors[i].GetComponent<MjSiteScalarSensor>().SensorReading;
        }

      
        // ------- Moving Average Filter-----
        for (int i = 0; i < forceSensors.Length; i++)
        {
            //move old values (FIFO)
            for (int j = windowSize - 1; j > 0; j--)
            {
                sensorHistory[i][j] = sensorHistory[i][j - 1];
            }

            // add new value
            sensorHistory[i][0] = currentSensorData[i];

            //calculate moving average
            filteredData[i] = 0d;
            for (int j = 0; j < windowSize; j++)
            {
                filteredData[i] += sensorHistory[i][j];
            }
            filteredData[i] /= windowSize;
        }


        // sum all sensor readings for total force
        for (int i = 0; i < filteredData.Length; i++)
        {
            //currentTotalForce += forceSensors[i].GetComponent<MjSiteScalarSensor>().SensorReading; // get sensor readings of certain sensor
            currentTotalForce += filteredData[i];
        }

        //------------ log filtered data
        string message = $"{StreamlinedInputManager.Now}, {string.Join(",", filteredData)} " + Environment.NewLine;
        LoggingManager.AddToBuffer("Stage2FilteredSensor", message);
        
       
        // adjust particle system
        if (particleSystemBall != null)
        {
            var emission = particleSystemBall.emission;  // get value of emission from particle system
            var mainModule = particleSystemBall.main;

            // dynamically change emission rate over time by normalization
            //dynamicMaxForce = Mathf.Lerp(dynamicMaxForce, Mathf.Max(dynamicMaxForce, (float)previousTotalForce), smoothingFactor * Time.deltaTime); // update max force with frame before
            dynamicMaxForce = Mathf.Max((float) currentTotalForce, dynamicMaxForce);

            if (dynamicMaxForce < 0.01f) //avoid division by zero
            {
                dynamicMaxForce = 0.01f;
            }

            // log total and dynamic force
            message = $"{StreamlinedInputManager.Now}, {currentTotalForce}, {dynamicMaxForce} " + Environment.NewLine;
            LoggingManager.AddToBuffer("Stage2TotalForce", message);


            float normForce = Mathf.Clamp01((float)currentTotalForce / dynamicMaxForce); // normalize current force using dynamic max

            // Adjust particle rate over time with force
            float targetRateOverTime = Mathf.Lerp(emissionMin, emissionMax, normForce); // scale normalized force for rateOverTime
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(targetRateOverTime); // update PS

            // Adjust particle size with force
            targetSize = Mathf.Lerp(sizeMin, sizeMax, normForce); // scale normalized force for startSize
            mainModule.startSize = new ParticleSystem.MinMaxCurve(targetSize); // update PS

           
            // Debug.Log("emission: " + emission.rateOverTime.constant + " currentTotalForce: " + currentTotalForce + " dynamic max force: " + dynamicMaxForce);


            // log emission rate and particle size
            message = $"{StreamlinedInputManager.Now}, {emission.rateOverTime.constant}, {mainModule.startSize.constant} " + Environment.NewLine;
            LoggingManager.AddToBuffer("Stage2Particle", message);


        }
   
        
    }


    private void OnTriggerEnter(Collider other)
    {

        if (other.gameObject.name != "XR Origin" && other.gameObject.tag != "table_collider" && other.gameObject.tag != "noTrigger")
        {

            particleSystemBall.Play();
            

        }

        if(other.gameObject.name == "Plane") // Ball falls to the ground
        {
            // end task
            ended = true;
            StartCoroutine(EndTask(0.4f, particleSystemBall.emission));
        }

       

    }
    private void OnTriggerStay(Collider other)
    {
        if (other.gameObject.name != "XR Origin" && other.gameObject.tag != "table_collider" && other.gameObject.tag != "noTrigger")
        {

            
            GameObject.Find("Ball_geom").GetComponent<MeshRenderer>().material.EnableKeyword("_EMISSION");

            if (timeCount >= successTime && !ended) // End Task
            {
                // SUCCESS
                ended = true;
                StartCoroutine(EndTask(0.4f, particleSystemBall.emission));
            }
            else
            {
                timeCount += Time.deltaTime; // count time of contact with ball
            }
        }
    }


    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.name != "XR Origin" && other.gameObject.tag != "table_collider" && other.gameObject.tag != "noTrigger")
        {
            
            GameObject.Find("Ball_geom").GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");

            particleSystemBall.Stop();

            timeCount = 0;

        }
    }
   


    IEnumerator EndTask(float delay, ParticleSystem.EmissionModule emission)
    {
        GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
        GetComponent<Collider>().isTrigger = false;
      

        if (particleSystemExplode != null)
        {
            var mainModule = particleSystemExplode.main;
            mainModule.startSize = new ParticleSystem.MinMaxCurve(targetSize);
            particleSystemExplode.Play();
        }
            

        particleSystemBall.Stop();

        yield return new WaitForSeconds(delay);

        dynamicMaxForce = 0.1f; // reset dynamic max force
        timeCount = 0;
        TaskManager.taskState = TaskState.Successful;
        GetComponent<Collider>().isTrigger = true;
        ended = false;
        taskManager.EndCurrentTask();


    }
}
