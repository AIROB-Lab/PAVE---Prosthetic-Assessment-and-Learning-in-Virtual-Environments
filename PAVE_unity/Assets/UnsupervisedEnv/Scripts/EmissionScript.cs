using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.IO.LowLevel.Unsafe;
using System;
using UnityEditor.PackageManager;
using UnityEngine.Rendering;
using Unity.Burst.Intrinsics;




public class EmissionScript : MonoBehaviour
{
    public int participantID;

    public enum EMGchannels { Eight = 8, Sixteen = 16, TwentyFour = 24, ThirtyTwo = 32  }
    public EMGchannels numberOfChannels = EMGchannels.TwentyFour; // number of emg channels default 32
    private int n_features; // to cast number of EMG channels to int



    public enum Events
    {
        StudyEvent
    }

    // Select in inspector 
    [Header("UDP")]
    public byte UDPCategory = 0; // Set UDP category("0.1" ARVs)
    public byte UDPSubCategory = 1;
    

    [Header("Game Objects")]
    [SerializeField] private GameObject parentLight; // light bulb parent
    [SerializeField] private GameObject spot; // light spot parent
    [SerializeField] private GameObject board; // background board
    [SerializeField] TextMeshPro textElement; // text field

    [Header("Material and PS")]
    [SerializeField] private Material[] emissiveMaterial; // emissive Material of all light bulbs
    [SerializeField] private ParticleSystem[] particlesLightBulbs; // particle Systems of all light bulbs
    [Header("Color Values")]
    [SerializeField] private float[] hueValue; //  array of hue values (length = # of light bulbs)
    [SerializeField] private float[] intensityValue; // array of intensity (length = # of light bulbs)

    [Header("Thresholds")]
    [SerializeField] private double activationThreshold = 0.3d; // ARV value to describe activity (myo 0.3, muovi 5)
    [SerializeField] private int numberOfRounds = 1; // # how often light bulbs activate again (# of explosion rounds)
    [SerializeField] private float timeThreshold = 3f; // time, when light bulb activates again
    


   

    // Create array of children later
    private GameObject[] child;
    private GameObject[] childLights;
    private GameObject[] spotLights;

    // color specific variables
    private Color[] color;

    // HSV colors
    private float colorHue = 0f;
    private float colorSaturation = 1f;
    private float colorValue = 0.5f;


    // Set range of color value
    private float min = 0.01f;
    [SerializeField] private float maxHue = 0.06f;
    [SerializeField] private float maxIntensity = 5.0f;
    [SerializeField] [Range(0, 10)] private float changeSteps = 4; // number of steps to calculate change speeds
    private float colorChangeSpeed; // how fast color should change 
    private float intensityChangeSpeed; // how fast intensity increases

    // counting variables
    private bool[] hasReachedThreshold;
    private int[] activationRound; // count numbers of activation
    private bool running = false;
    private bool finalLog = false;


    private List<StreamlinedInputManager_LR.UdpObject> arvValues = null;
    private double[] emgValuesGrouped;

    // event log 
    private string eventHeader = "time_stamp_s" + "," + "participant" +  "," + "event" + "," + "name" + "," + "light_bulb_number" + "," + "round" + Environment.NewLine;
    private string valueHeader = "time_stamp_s" + "," + "participant" + "," + "values" + Environment.NewLine;

    // Start is called before the first frame update
    void Start()
    {
        textElement.text = "EXPLORE YOUR \nMUSCLE SPACE!";
        childLights = GetChildren(parentLight, 2);
        spotLights = GetChildren(spot, 1);
        color = new Color[8];
        hasReachedThreshold = new bool[childLights.Length];
        activationRound = new int[childLights.Length];

        


        for (int i = 0; i < emissiveMaterial.Length; i++)
        {
            emissiveMaterial[i].EnableKeyword("_EMISSION");
            // Set Color at the begining to the start color
            color[i] = Color.HSVToRGB(colorHue, colorSaturation, colorValue);
            emissiveMaterial[i].SetColor("_EmissionColor", color[i]);
        }

        emgValuesGrouped = new double[childLights.Length]; // array of 8


        // Logging
        LoggingManager.CreateNewLog("Stage1StudyEvents", eventHeader, 5f);
        LoggingManager.CreateNewLog("Stage1RMS", valueHeader, 5f);
        LoggingManager.CreateNewLog("Stage1EMGgrouped", valueHeader, 5f);
        LoggingManager.CreateNewLog("Stage1ColorValue", valueHeader, 5f);
        LoggingManager.CreateNewLog("Stage1IntensityValue", valueHeader, 5f);

    }

    // Update is called depending on framerate 
    void Update()
    {
        // set the right change speeds
        colorChangeSpeed = (maxHue - min) / changeSteps;
        intensityChangeSpeed = (maxIntensity - min) / changeSteps;

        // get the number of emg channels as int
        n_features = (int)numberOfChannels;

        // START OF LIGHT CHANGE (CLICK ON UI BUTTON)
        if (running)
        {
            if (arvValues != null)
            {
                arvValues = StreamlinedInputManager_LR.udpReceiver.getUdpObjects(UDPCategory, UDPSubCategory, true); // receive ARVs from SIM
                changeLight(arvValues); // change color
            }
            if (arvValues == null)
            {
                arvValues = StreamlinedInputManager_LR.udpReceiver.getUdpObjects(UDPCategory, UDPSubCategory, true); //empty buffer 
            }
        }

    }

    public GameObject[] GetChildren(GameObject parent, int type)
    {
        // type: 1 for Child, 2 for GrandChild
        child = new GameObject[parent.transform.childCount];
        if (type == 1)
        {
            for (int i = 0; i < parent.transform.childCount; i++)
            {
                // get all childs/grandchilds of the parent that will be changed later
                child[i] = parent.transform.GetChild(i).gameObject;
            }
        }
        else if (type == 2)
        {
            for (int i = 0; i < parent.transform.childCount; i++)
            {
                // get all childs/grandchilds of the parent that will be changed later
                child[i] = parent.transform.GetChild(i).GetChild(1).gameObject;
            }
        }
        return child;
    }


    public void changeLight(List<StreamlinedInputManager_LR.UdpObject> arvValues)
    {
        foreach (var obj in arvValues) // use all data between "update calls", independent from framerate/sampling rate
        {
            if (n_features == emgValuesGrouped.Length) // case 8
            {
                for (int i = 0; i < obj.Data.Length; i++)
                {
                    double currentVal = (double)obj.Data[i]; //ARV value from SIM

                    if (currentVal > activationThreshold && i < 8) // active!, increase intensity of the light bulbs
                    {
                        // activate spot
                        spotLights[i].GetComponent<MeshRenderer>().material.EnableKeyword("_EMISSION");

                        // Set the corresponding light bulb to emmissive
                        childLights[i].GetComponent<Renderer>().material = emissiveMaterial[i];


                        if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && !hasReachedThreshold[i]) //reached for first time -> explode & and check for end of training
                        {
                            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                            // DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination
                            hasReachedThreshold[i] = true;
                            particlesLightBulbs[i].Play();
                            childLights[i].SetActive(false);

                            // log explosion of light bulb
                            AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"Explosion", i.ToString(), activationRound[i].ToString());


                            bool allBulbsCompleted = true;
                            for (int j = 0; j < activationRound.Length; j++)
                            {
                                if (activationRound[j] < numberOfRounds || !hasReachedThreshold[j]) // check round and explosion of each light bulb
                                {
                                    allBulbsCompleted = false;
                                    break;
                                }
                            }

                            if (allBulbsCompleted) // end of training, when all reached
                            {
                                textElement.text = "Well done :)";

                                if (!finalLog)
                                {
                                    // log end of training
                                    AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"EndTraining", i.ToString(), activationRound[i].ToString());
                                    finalLog = true;
                                }

                                if (board != null)
                                {
                                    board.SetActive(false);
                                }

                            }
                        }
                        else if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && hasReachedThreshold[i]) // already reached for this one -> next round
                        {
                            // inactivate spot
                            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination

                            if (activationRound[i] < numberOfRounds)
                            {
                                hueValue[i] = 0;
                                intensityValue[i] = 0;
                                hasReachedThreshold[i] = false;
                                // start again
                                StartCoroutine(WaitAndStartAgain(i, timeThreshold));

                                AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"NextRound", i.ToString(), activationRound[i].ToString());
                            }

                        }
                        else // increase values
                        {

                            hueValue[i] = Mathf.Clamp(hueValue[i] + colorChangeSpeed * Time.deltaTime, min, maxHue); // increase color_hue
                            intensityValue[i] = Mathf.Clamp(intensityValue[i] + intensityChangeSpeed * Time.deltaTime, min, maxIntensity); // increase intensity

                            color[i] = Color.HSVToRGB(hueValue[i], colorSaturation, colorValue); // update color
                            emissiveMaterial[i].SetColor("_EmissionColor", color[i] * intensityValue[i]); // update material

                            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * intensityValue[i]); // Update Global Illumination

                        }
                    }
                    else if (currentVal < activationThreshold) // not active
                    {
                        spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    }
                }
            }
            else // case 16 or 32
            {
                // sum values to create vector of 8 entries
                int outputLength = emgValuesGrouped.Length; //corresponds to number of light bulbs

                int groupFactor = n_features / outputLength; // calculate how many values need to be grouped in one value, e.g. 16 to 8 = 2

                for (int i = 0; i < emgValuesGrouped.Length; i++)
                {
                    double sum = 0.0;
                    for (int j = 0; j < groupFactor; j++)
                    {
                        sum += (double)obj.Data[i * groupFactor + j];
                    }
                    emgValuesGrouped[i] = sum / groupFactor; // mean of values from original array in grouped order (0+1, 2+3, 4+5,..., 14+15)
                                                             //emgValuesGrouped[i] = ((double)obj.Data[i * 2] + (double)obj.Data[i * 2 + 1]) / 2.0; // mean of 2 arv values in one entry

                    if (emgValuesGrouped[i] > activationThreshold) // active!, increase intensity of the light bulbs
                    {
                        // activate spot
                        spotLights[i].GetComponent<MeshRenderer>().material.EnableKeyword("_EMISSION");

                        // Set the corresponding light bulb to emmissive
                        childLights[i].GetComponent<Renderer>().material = emissiveMaterial[i];


                        if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && !hasReachedThreshold[i]) //reached for first time -> explode & and check for end of training
                        {
                            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                            // DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination
                            hasReachedThreshold[i] = true;
                            particlesLightBulbs[i].Play();
                            childLights[i].SetActive(false);

                            // log explosion of light bulb
                            AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"Explosion", i.ToString(), activationRound[i].ToString());


                            bool allBulbsCompleted = true;
                            for (int j = 0; j < activationRound.Length; j++)
                            {
                                if (activationRound[j] < numberOfRounds || !hasReachedThreshold[j]) // check round and explosion of each light bulb
                                {
                                    allBulbsCompleted = false;
                                    break;
                                }
                            }

                            if (allBulbsCompleted) // end of training, when all reached
                            {
                                textElement.text = "Well done :)";

                                if (!finalLog)
                                {
                                    // log end of training
                                    AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"EndTraining", i.ToString(), activationRound[i].ToString());
                                    finalLog = true;
                                }

                                if (board != null)
                                {
                                    board.SetActive(false);
                                }

                            }
                        }
                        else if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && hasReachedThreshold[i]) // already reached for this one -> next round
                        {
                            // inactivate spot
                            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination

                            if (activationRound[i] < numberOfRounds)
                            {
                                hueValue[i] = 0;
                                intensityValue[i] = 0;
                                hasReachedThreshold[i] = false;
                                // start again
                                StartCoroutine(WaitAndStartAgain(i, timeThreshold));

                                AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"NextRound", i.ToString(), activationRound[i].ToString());
                            }

                        }
                        else // increase values
                        {

                            hueValue[i] = Mathf.Clamp(hueValue[i] + colorChangeSpeed * Time.deltaTime, min, maxHue); // increase color_hue
                            intensityValue[i] = Mathf.Clamp(intensityValue[i] + intensityChangeSpeed * Time.deltaTime, min, maxIntensity); // increase intensity

                            color[i] = Color.HSVToRGB(hueValue[i], colorSaturation, colorValue); // update color
                            emissiveMaterial[i].SetColor("_EmissionColor", color[i] * intensityValue[i]); // update material

                            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * intensityValue[i]); // Update Global Illumination

                        }


                    }
                    else if (emgValuesGrouped[i] < activationThreshold) // not active
                    {
                        spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    }
                }


            





                    // muovi version -> 32 channels
                    // split array in 2 groups (0-15) (16-31)
                    //int half = obj.data.Length / 2;

                    //for(int i = 0; i < half/2; i++) // put groups like (0+1+16+17) or (2+3+18+19) in entries of the new grouped array (length 8)
                    //{
                    //    emgValuesGrouped[i] = (double) (obj.data[i * 2] + obj.data[i * 2 + 1] + obj.data[i * 2 + half] + obj.data[i * 2 + half + 1]) / 4; // mean of 4 arv values in one entry

                    //    if (emgValuesGrouped[i] > currentValTreshold) // active!, increase intensity of the light bulbs
                    //    {
                    //        // activate spot
                    //        spotLights[i].GetComponent<MeshRenderer>().material.EnableKeyword("_EMISSION");

                    //        // Set the corresponding light bulb to emmissive
                    //        childLights[i].GetComponent<Renderer>().material = emissiveMaterial[i];


                    //        if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && !hasReachedThreshold[i]) //reached for first time
                    //        {
                    //            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    //            // DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination
                    //            hasReachedThreshold[i] = true;
                    //            particlesLightBulbs[i].Play();
                    //            childLights[i].SetActive(false);

                    //        }
                    //        else if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && hasReachedThreshold[i]) // already reached for this one
                    //        {
                    //            // inactivate spot
                    //            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    //            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination

                    //            if (IsMissionCompleted(hasReachedThreshold) && activationRound[i] >= roundThreshold)// reached for all in the end
                    //            {
                    //                textElement.text = "Well done :)";
                    //                if (board != null)
                    //                {
                    //                    board.SetActive(false);
                    //                }

                    //            }

                    //            if (activationRound[i] < roundThreshold)
                    //            {
                    //                hueValue[i] = 0;
                    //                intensityValue[i] = 0;
                    //                hasReachedThreshold[i] = false;
                    //                activationRound[i]++; // next round
                    //                // start again
                    //                StartCoroutine(WaitAndStartAgain(i, timeThreshold));

                    //            }
                    //        }
                    //        else
                    //        {

                    //            hueValue[i] = Mathf.Clamp(hueValue[i] + colorChangeSpeed * Time.deltaTime, min, maxHue); // increase color_hue
                    //            intensityValue[i] = Mathf.Clamp(intensityValue[i] + intensityChangeSpeed * Time.deltaTime, min, maxIntensity); // increase intensity

                    //            color[i] = Color.HSVToRGB(hueValue[i], colorSaturation, colorValue); // update color
                    //            emissiveMaterial[i].SetColor("_EmissionColor", color[i] * intensityValue[i]); // update material

                    //            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * intensityValue[i]); // Update Global Illumination
                    //        }

                    //    }
                    //    else if (emgValuesGrouped[i] < currentValTreshold) // not active
                    //    {
                    //        spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    //    }



                    // myo version:
                    //for (int i = 0; i < obj.Data.Length; i++)
                    //{
                    //    double currentVal = (double)obj.Data[i]; //ARV value from SIM

                    //    if (currentVal > currentValTreshold && i < 8) // active, increase intesity for first 8 channels
                    //    {
                    //        // activate spot
                    //        spotLights[i].GetComponent<MeshRenderer>().material.EnableKeyword("_EMISSION");

                    //        // Set the corresponding light bulb to emmissive
                    //        childLights[i].GetComponent<Renderer>().material = emissiveMaterial[i];


                    //        if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && !hasReachedThreshold[i]) //reached for first time
                    //        {
                    //            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    //           // DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination
                    //            hasReachedThreshold[i] = true;
                    //            particlesLightBulbs[i].Play();
                    //            childLights[i].SetActive(false);

                    //        }
                    //        else if (hueValue[i] >= maxHue && intensityValue[i] >= maxIntensity && hasReachedThreshold[i]) // already reached for this one
                    //        {
                    //            // inactivate spot
                    //            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    //            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * 0); // Update Global Illumination

                    //            if (IsMissionCompleted(hasReachedThreshold) && activationRound[i] >= roundThreshold)// reached for all in the end
                    //            {
                    //                textElement.text = "Well done :)";
                    //                if (board != null)
                    //                {
                    //                    board.SetActive(false);
                    //                }

                    //            }

                    //            if (activationRound[i] < roundThreshold)
                    //            {
                    //                hueValue[i] = 0;
                    //                intensityValue[i] = 0;
                    //                hasReachedThreshold[i] = false;
                    //                activationRound[i]++; // next round
                    //                // start again
                    //                StartCoroutine(WaitAndStartAgain(i, timeThreshold));


                    //            }
                    //        }
                    //        else
                    //        {

                    //            hueValue[i] = Mathf.Clamp(hueValue[i] + colorChangeSpeed * Time.deltaTime, min, maxHue); // increase color_hue
                    //            intensityValue[i] = Mathf.Clamp(intensityValue[i] + intensityChangeSpeed * Time.deltaTime, min, maxIntensity); // increase intensity

                    //            color[i] = Color.HSVToRGB(hueValue[i], colorSaturation, colorValue); // update color
                    //            emissiveMaterial[i].SetColor("_EmissionColor", color[i] * intensityValue[i]); // update material

                    //            //DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * intensityValue[i]); // Update Global Illumination
                    //        }


                    //    }
                    //    else if (currentVal < currentValTreshold && i < 8) // not active
                    //    {
                    //        spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                    //    }
                    //}

                
            }
            // log rms values 
            AddToValueBuffer("Stage1RMS", StreamlinedInputManager_LR.Now, string.Join(",", obj.Data));
            // log emg values for 8 channels
            AddToValueBuffer("Stage1EMGgrouped", StreamlinedInputManager_LR.Now, string.Join(",", emgValuesGrouped));
            // log hueValues and intensityValues
            AddToValueBuffer("Stage1ColorValue", StreamlinedInputManager_LR.Now, string.Join(",", hueValue));
            AddToValueBuffer("Stage1IntensityValue", StreamlinedInputManager_LR.Now, string.Join(",", intensityValue));
        }
    }


    private IEnumerator WaitAndStartAgain(int i, float waitTime)
    {
        hasReachedThreshold[i] = false;

        yield return new WaitForSeconds(waitTime);

        // reset light
        hueValue[i] = 0;
        intensityValue[i] = 0;
        activationRound[i]++; // next round
        childLights[i].SetActive(true);


        // DynamicGI.SetEmissive(childLights[i].GetComponent<Renderer>(), color[i] * intensityValue[i]); // Update Global Illumination
    }



    public void AddToEventBuffer(double now, Events ev, string name, string number, string round)
    {
        //"time_stamp_s" + "," + "participant" +  "," + "event" + "," + "name" + "," + light_bulb_number" + "," + "round" + Environment.NewLine;
        string addBuffer = $"{now},{participantID},{ev.ToString()},{name},{number},{round},{Environment.NewLine}";
        LoggingManager.AddToBuffer("Stage1StudyEvents", addBuffer);
    }
    public void AddToValueBuffer(string fileName, double now, string values)
    {
        //"time_stamp_s" + "," + "participant" + "," + "values" + Environment.NewLine;
        string addBuffer = $"{now},{participantID},{values},{Environment.NewLine}";
        LoggingManager.AddToBuffer(fileName, addBuffer);
    }


    #region UI
    public void LoadScene()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;

        SceneManager.LoadScene(currentSceneName);
    }

    public void StartTraining()
    {
        if (running)
        {
            StopTraining();
            return;
        }

        // change button color
        GameObject goBtn = GameObject.Find("StartTraining");
        if(goBtn != null)
        {
            goBtn.GetComponent<Image>().color = Color.red;
        }
       
        running = true;

        //log start
        AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"StartBtn", $"-1", $"-1");
    }   
    
    public void StopTraining() // = End of Training
    {
        running = false;
        //log stop
        AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"StopBtn", $"-1", $"-1");

        arvValues = null; // to allow no color increasing 

        for(int i = 0; i < childLights.Length; i++) // reset everything
        {
            hueValue[i] = 0;
            intensityValue[i] = 0;
            activationRound[i] = 0;
            hasReachedThreshold[i] = false;
            color[i] = Color.HSVToRGB(hueValue[i], colorSaturation, colorValue); // update color
            emissiveMaterial[i].SetColor("_EmissionColor", color[i] * intensityValue[i]); // update material
            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
        }
        

        // change button color back
        GameObject goBtn = GameObject.Find("StartTraining");
        if (goBtn != null)
        {
            goBtn.GetComponent<Image>().color = new Color(r: 0.6536134f, g: 0.9056604f, b: 0.6730896f);
        }

        
    }

    public void PauseTraining()
    {
        if (running)
        {
            running = false;

            // change button color
            GameObject goBtn = GameObject.Find("PauseTraining");
            if (goBtn != null)
            {
                goBtn.GetComponent<Image>().color = Color.red;
            }

            //log pause
            AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"PauseBtn:pause", $"-1", $"-1");
            return;
        }
        else if (!running)
        {
            running = true;

            // change button color back
            GameObject goBtn = GameObject.Find("PauseTraining");
            if (goBtn != null)
            {
                goBtn.GetComponent<Image>().color = new Color(r: 1f, g: 0.9813771f , b: 0.7911051f); 
            }
            //log pause
            AddToEventBuffer(StreamlinedInputManager_LR.Now, Events.StudyEvent, $"PauseBtn:start", $"-1", $"-1");
           
            
        }

        

    }
    public void StartAgain()
    {
        for (int i = 0; i < childLights.Length; i++) // reset everything
        {
            childLights[i].SetActive(true);
            hueValue[i] = 0;
            intensityValue[i] = 0;
            activationRound[i] = 0;
            hasReachedThreshold[i] = false;
            color[i] = Color.HSVToRGB(hueValue[i], colorSaturation, colorValue); // update color
            emissiveMaterial[i].SetColor("_EmissionColor", color[i] * intensityValue[i]); // update material
            spotLights[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
        }

        if (board != null)
        {
            board.SetActive(true);
        }

        textElement.text = "EXPLORE YOUR\nMUSCLE SPACE!";

        //log pause
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"StartAgainBtn", $"-1", $"-1");

    }
    #endregion


}
