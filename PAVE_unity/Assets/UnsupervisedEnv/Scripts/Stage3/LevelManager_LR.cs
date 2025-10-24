using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;
using System.Linq;

public enum Levels
{
    None, // #0 not running
    CollectIngredients, // #1
    PourIngredients, // #2
    HeatUpStove, // #3
    FlipPancake1, // #4a
    PlacePancake1, // #5a
    FlipPancake2, // #4b
    PlacePancake2, // #5b
    SqueezeSyrup, // #6
    Final // End of kitchen tasks
}

public enum IngredientNames
{
    milk,
    sugar,
    flour,
    egg1, 
    egg2
}

public enum LevelNames
{
    CollectIngredients,
    PourIngredients,
    HeatUpStove,
    FlipPancake,
    PlacePancake,
    SqueezeSyrup
}

[System.Serializable]
public class LevelTime // add individual time for each level in the inspector
{
    public LevelNames levelName;
    public float levelDuration;
}

[System.Serializable]
public class Ingredient
{
    public IngredientNames name;
    public GameObject gameObj;
    public GameObject startPosition;
    public GameObject targetPosition;
    public GameObject outsidePosition;
    public GameObject colliderGO;
    public MjFreeJoint freeJoint;
}


public class LevelManager : MonoBehaviour
{
    #region VARIABLES
    public enum Events
    {
        StudyEvent,
        LevelChange,
        CollisionEvent
    }

    public static Levels level;

    // general
    [HideInInspector] public Levels levelPause; // to save the current level during pause 
    [Header("General")]
    public int participantID;
    public TextMeshProUGUI elapsedTimeText;
    public TextMeshProUGUI totalElapsedTimeText;
    public AudioSource startSound;
    public AudioSource successSound;
    public AudioSource failureSound;

    private float elapsedTime = 0f; // time during one level
    private float totalElapsedTime = 0f; // running time 

    public GameObject[] instructionText;

    //public float timeOver = 180f; // seconds to perform a level
    private bool timeLimit = true; // boolean to enable level time limit
    public List<LevelTime> levelTime = new List<LevelTime>(); // List of each level with its individual time over time [in seconds]
    


    [HideInInspector] public GameObject bowl;
    [HideInInspector] public GameObject syrup;
    [HideInInspector] public GameObject spatula;
    [HideInInspector] public GameObject plate;
    //[HideInInspector] public GameObject pancakeCooked;
    [HideInInspector] public GameObject pan;
    private GameObject dough1;
    private GameObject dough2;
    private GameObject dough3;
    private GameObject dough4;
    private GameObject dough5;

    private bool running = false;

    [Header("Ingredients")]
    public List<Ingredient> ingredients = new List<Ingredient>();
    public static Ingredient milkBottle;
    public static Ingredient sugarBox;
    public static Ingredient flourBox;
    public static Ingredient egg1;
    public static Ingredient egg2;




    // level 1 (COLLECT)
    public static List<GameObject> collectedObjects = new List<GameObject>(); // List for collected ingredients
    public static List<String> fallenObjects = new List<String>(); // List of objects fallen on the floor
    [Header("Level 1")] 
    public GameObject collectionArea;
    public int numberToCollect = 5; // # ingredients to collect (milk, sugar, flour, 2 eggs)
    private bool placedStart = false;

    // level 2 (POUR)
    [Header("Level 2")]
    public FillingBar fillingBar;
    public GameObject fillingObject;
    public float fillingTime = 3f; // time to hold in position adjusts the maxValue of the filling scale
    private float timeToFill1 = 0f;
    private float timeToFill2 = 0f;
    private float timeToFill3 = 0f;
    // public MjActuator wristPRO; // wrist pronation supination actuator
    public static List<GameObject> pouredObjects = new List<GameObject>(); // List of grabed ingredients to pour in mixing bowl
    private GameObject grabbedObject = null; // object send per delegate
    public MjSiteScalarSensor[] forceSensors; // measured force sensors from MPL
    public double currentTotalForce;
    public double maxBreakingForce = 10.0d;
    public AudioSource eggSound;
    public static bool placed = false;
    public GameObject targetPos;
    public GameObject positionEgg1;
    public GameObject positionEgg2;
    public GameObject rendererGOEgg1;
    public GameObject rendererGOEgg2;
    public Material transparentMat;
    public Material egg1Mat;
    public Material egg2Mat;
    public ParticleSystem psEgg1;
    public ParticleSystem psEgg2;
    private bool broke1 = false;
    private bool broke2 = false;
    private bool hasPlayedCrackSound1 = false;
    private bool hasPlayedCrackSound2 = false;


    // level 3 (HEAT UP)
    [Header("Level 3")]
    public MjHingeJoint knob;
    private float requiredKnobAngle = -80; // angle position of knob joint for activation of the stove
    private bool outsideArea = false;
    public GameObject posEggOutside1;
    public GameObject posEggOutside2;
    public GameObject posOutside;


    // level 4,6,8 (FLIP)
    public static bool flipped = false;
    [Header("Level 4, 6, and 8")]
    public GameObject pancakeCooked1;
    public GameObject pancakeCooked2;
    public GameObject pancakeRaw;
    public GameObject posPancakeInPan;
    public GameObject posPancakeOutside;
    public GameObject posPan;
    public GameObject posSpatula;

    // level 5,7,9 (PLACE)
    public static bool onPlate = false;
    [Header("Level 5, 7, and 9")]
    public GameObject posPancakePlate;

    //level 10
    [Header("Level 10")] 
    public FillingBar fillingBarSyrup;
    public GameObject fillingObjectSyrup;
    public GameObject posSyrup;
    private float timeToSqueeze = 0f;
    public static double currentTotalForceSyrup;
    public static double maxSqueezeForce = 9;



    // log header
    private string eventHeader = "time_stamp_s" + "," + "participant" + "," + "event" + "," + "name" + "," + "level_number" + "," + "level_name" + Environment.NewLine;
    private string levelHeader = "time_stamp_s" + "," + "participant" + "," + "event" + "," + "name" + "," + "level_number" + "," + "level_name" + "elapsed_time_s" + "," + "total_elapsed_time_s" + Environment.NewLine;
    private string valueHeader = "time_stamp_s" + "," + "participant" + "," + "total_force" + "," + "level_number" + "," + "level_name" + Environment.NewLine;

    #endregion


    // Start is called before the first frame update
    void Start()
    {
        // logging
        LoggingManager.CreateNewLog("Stage3StudyEvent", eventHeader);
        LoggingManager.CreateNewLog("Stage3TotalForce", valueHeader);
        LoggingManager.CreateNewLog("Stage3LevelChange", levelHeader);


        levelPause = Levels.CollectIngredients;
        //currentLevel = level;

        collectionArea?.SetActive(false);

        // get bowl & dough
        bowl = GameObject.Find("bowl");
        dough1 = bowl.transform.Find("Dough1").gameObject;
        dough2 = bowl.transform.Find("Dough2").gameObject;
        dough3 = bowl.transform.Find("Dough3").gameObject;
        dough4 = bowl.transform.Find("Dough4").gameObject;
        dough5 = bowl.transform.Find("Dough5").gameObject;
        
        dough1?.SetActive(false);
        dough2?.SetActive(false);
        dough3?.SetActive(false);
        dough4?.SetActive(false);
        dough5?.SetActive(false);

        // get variables of ingredients
        milkBottle = ingredients.Find(i => i.name == IngredientNames.milk);
        sugarBox = ingredients.Find(j => j.name == IngredientNames.sugar);
        flourBox = ingredients.Find(k => k.name == IngredientNames.flour);
        egg1 = ingredients.Find(i => i.name == IngredientNames.egg1);
        egg2 = ingredients.Find(i => i.name == IngredientNames.egg2);

        // disable PourDetector script
        milkBottle.colliderGO.GetComponent<PourDetector>().enabled = false;
        sugarBox.colliderGO.GetComponent<PourDetector1>().enabled = false;
        flourBox.colliderGO.GetComponent<PourDetector2>().enabled = false;

        // disable filling bar
        fillingObject?.SetActive(false);
        fillingObjectSyrup?.SetActive(false);
        fillingBar.SetMaxFilling(fillingTime);
        fillingBarSyrup.SetMaxFilling(fillingTime);

        // get syrup
        syrup = GameObject.Find("syrup");
        if (syrup != null)
        {
           foreach (Renderer i in syrup.GetComponentsInChildren<Renderer>())
            { i.enabled = false; }

        }

        // get spatula
        spatula = GameObject.Find("spatula");
        if (spatula != null)
        {
            spatula.GetComponentInChildren<Renderer>().enabled = false;
        }

        // get plate
        plate = GameObject.Find("plate");
        if(plate!=null)
            plate.GetComponent<Renderer>().enabled=false;

        // disable pancake renderer
        if (pancakeRaw != null)
        {
            pancakeRaw.GetComponentInChildren<Renderer>().enabled = false;
        }
        if (pancakeCooked1 != null)
        {
            pancakeCooked1.GetComponentInChildren<Renderer>().enabled = false;
        }
        if (pancakeCooked2 != null)
        {
            pancakeCooked2.GetComponentInChildren<Renderer>().enabled = false;  
        }
       

        // get pan
        pan = GameObject.Find("pan2");
        if (pan != null)
        {
            foreach (Renderer i in pan.GetComponentsInChildren<Renderer>())
            { i.enabled = false; }

        }


        // disable instruction text
        for (int i = 0; i < instructionText.Length; i++)
        {
            // disable the instruction text for the recipe
            instructionText[i].gameObject.SetActive(false);
        }

    }


    private void OnEnable()
    {
        CollisionObject_LR.OnObjectGrabbed += HandleObjectGrabbed;
    }
    private void OnDisable()
    {
        CollisionObject_LR.OnObjectGrabbed -= HandleObjectGrabbed;
    }

    private void HandleObjectGrabbed(GameObject grabbedObj)
    {
        // get the current grabbed Object for level2 & 6 by delegate in CollisionObject.cs
        grabbedObject = grabbedObj;
              
    }


    


    private void FixedUpdate()
    {
       
        //level = currentLevel;

        if (level != Levels.None)
        {
            // increase time counter when level is running
            elapsedTime += Time.deltaTime;
            totalElapsedTime += Time.deltaTime;
        }
        

        if (elapsedTimeText != null)
        {
            // Show time in UI
            elapsedTimeText.text = $"Elapsed Time: {elapsedTime:F2}"; 
        }
        if (totalElapsedTimeText != null)
        {
            // Show time in UI
            totalElapsedTimeText.text = $"Total Elapsed Time: {totalElapsedTime:F2}"; 
        }

        switch (level)
        {
            /// <summary>
            /// level stat before starting and during pause
            /// </summary>
            case Levels.None: // #0
                // do nothing in here
                //Debug.Log("Wait for start");
                
                break;

            /// <summary>
            /// collect ingredients (milk bottle, sugar box, flour box, 2 eggs) in designated area
            /// continue with next level if everything is collected or time over 
            /// </summary>
            case Levels.CollectIngredients: // #1
               
                // show instruction text
                DisableText(instructionText);
                instructionText[0].gameObject.SetActive(true);

                // show collection area
                collectionArea?.SetActive(true);

                //place ingredients to starting position
                if (!placedStart)
                {
                    StartCoroutine(TeleportIngrToCorrectPosition(milkBottle,milkBottle.startPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(sugarBox, sugarBox.startPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(flourBox, flourBox.startPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(egg1, egg1.startPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(egg2, egg2.startPosition));

                    placedStart = true;
                }


                if (grabbedObject != null && collectedObjects.Count < numberToCollect)
                {

                    switch (grabbedObject.name)
                    {
                        case "egg1_collider":
                            //get current sensor readings for total force
                            for (int i = 0; i < forceSensors.Length; i++)
                            {
                                currentTotalForce += forceSensors[i].GetComponent<MjSiteScalarSensor>().SensorReading;
                            }
                            currentTotalForce /= forceSensors.Length; // calculate mean of force

                            //log total force
                            AddToValueBuffer("Stage3TotalForce", StreamlinedInputManager.Now, $"{currentTotalForce}", $"{(int)level}", $"{level}");

                            if (currentTotalForce >= maxBreakingForce && !broke1) // egg broke
                            {
                                Debug.Log("Egg1 broken");
                                currentTotalForce = 0;
                                broke1 = true;

                                fallenObjects.Add(egg1.colliderGO.name);

                                if (failureSound != null)
                                    PlaySound(failureSound);

                                //log broken egg
                                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"brokenEgg:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                //change material to transparent
                                rendererGOEgg1.GetComponent<Renderer>().material = transparentMat;

                                // play Particle System
                                psEgg1?.Play();

                                // teleportate egg 
                                StartCoroutine(WaitAndTeleportateEgg(egg1.gameObj));


                            }
                            else if (currentTotalForce < maxBreakingForce && !broke1) // cracking effect
                            {
                                BreakingEggEffect(rendererGOEgg1, currentTotalForce, ref hasPlayedCrackSound1);

                            }
                            break;

                        case "egg2_collider":
                            //get current sensor readings for total force
                            for (int i = 0; i < forceSensors.Length; i++)
                            {
                                currentTotalForce += forceSensors[i].GetComponent<MjSiteScalarSensor>().SensorReading;
                            }
                            currentTotalForce /= forceSensors.Length; // calculate mean of force

                            //log total force
                            AddToValueBuffer("Stage3TotalForce", StreamlinedInputManager.Now, $"{currentTotalForce}", $"{(int)level}", $"{level}");

                            if (currentTotalForce >= maxBreakingForce && !broke2) // egg broke
                            {
                                Debug.Log("Egg2 broken");
                                currentTotalForce = 0;
                                broke2 = true;
                                fallenObjects.Add(egg2.colliderGO.name);

                                if (failureSound != null)
                                    PlaySound(failureSound);

                                //log broken egg
                                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"brokenEgg:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                //change material to transparent
                                rendererGOEgg2.GetComponent<Renderer>().material = transparentMat;

                                // play Particle System
                                psEgg2?.Play();

                                // teleportate egg 
                                StartCoroutine(WaitAndTeleportateEgg(egg2.gameObj));

                            }
                            else if (currentTotalForce < maxBreakingForce && !broke2) // cracking effect
                            {
                                BreakingEggEffect(rendererGOEgg2, currentTotalForce, ref hasPlayedCrackSound2);

                            }
                            break;
                    }
                }
         
                // End of level
                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[0].levelDuration)
                {
                    // log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver(collectedItems={collectedObjects.Count})", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.PourIngredients;
                    Debug.Log("Level2");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear(); // clear the list for next level

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }
                // SUCCESS
                else if (collectedObjects.Count >= numberToCollect) // count collected objects in CollisionObject.cs, switch to next level if all ingredients collected
                {
                    // log success
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:collectedItems={collectedObjects.Count}", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.PourIngredients;
                    Debug.Log("Level2");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear(); // clear the list for next level

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                    break;

                }
                // FAIL
                else if ((fallenObjects.Count + collectedObjects.Count) >= numberToCollect)
                {
                    // log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:fallenObjects={fallenObjects.Count}", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:fallenObjects", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.PourIngredients;
                    Debug.Log("Level2");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear(); // clear the list for next level

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

              

                break;

            /// <summary>
            /// pour ingredients into mixing bowl when actuator in wrist pronation (milk, flour, sugar, 1 egg into mixing bowl)
            /// as visualization: show filling scale for flour, sugar and milk
            /// PourDetector.cs for pouring effect
            /// egg crashing effect after certain force value
            /// </summary>
            case Levels.PourIngredients: // #2
                // instruction text
                DisableText(instructionText);
                instructionText[1].gameObject.SetActive(true); // enable the instruction text

                // show mixing bowl
                //bowl?.SetActive(true);

                // disable collecting area
                collectionArea?.SetActive(false);


                // place the ingredients behind the bowl (just once)
                if (!placed)
                {
                    StartCoroutine(TeleportIngrToCorrectPosition(milkBottle, milkBottle.targetPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(sugarBox, sugarBox.targetPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(flourBox, flourBox.targetPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(egg1, egg1.targetPosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(egg2, egg2.targetPosition));

                    //change material to eggMaterial
                    rendererGOEgg1.GetComponent<Renderer>().material = egg1Mat;
                    rendererGOEgg2.GetComponent<Renderer>().material = egg2Mat;

                    hasPlayedCrackSound1 = false;
                    hasPlayedCrackSound2 = false;

                    placed = true;
                }



                if (grabbedObject != null && pouredObjects.Count != numberToCollect)
                {
                    
                    switch (grabbedObject.name)
                    {

                        case "milk_collider":

                            if (!pouredObjects.Contains(grabbedObject)) // not poured so far
                            {
                                //log grabbed object
                                AddToEventBuffer(StreamlinedInputManager.Now, Events.CollisionEvent, $"grabbedObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                // show filling bar
                                fillingObject?.SetActive(true);
                                fillingBar.SetFilling(timeToFill3);

                                // enable pouring script
                                grabbedObject.GetComponent<PourDetector>().enabled = true;

                                string hitName1 = null;
                                if (Stream.hit.collider != null)
                                {
                                    hitName1 = Stream.hit.collider.gameObject.name;
                                }

                                if (hitName1 != null && (hitName1 == "bowl_collider" || hitName1 == "Dough1" || hitName1 == "Dough2" || hitName1 == "Dough3" || hitName1 == "Dough4" || hitName1 == "Dough5"))
                                {
                                        //increase time during filling position
                                        timeToFill3 += Time.deltaTime;

                                        // adjust filling scale
                                        fillingBar.SetFilling(timeToFill3);

                                        if (timeToFill3 >= fillingTime) // filling completed
                                        {
                                            //log poured object
                                            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"pouredObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                            PlaySound(successSound);
                                            timeToFill3 = 0;
                                            // Add object to list
                                            pouredObjects.Add(grabbedObject);


                                            // disable filling bar
                                            fillingObject?.SetActive(false);

                                            // teleportate outside kitchen
                                            StartCoroutine(TeleportIngrToCorrectPosition(milkBottle, milkBottle.outsidePosition));
                                        }
                                }
                                

                            }

                        break;


                            
                        case "sugar_collider":
                            if (!pouredObjects.Contains(grabbedObject)) // not poured so far
                            {
                                //log grabbed object
                                AddToEventBuffer(StreamlinedInputManager.Now, Events.CollisionEvent, $"grabbedObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                // show filling bar
                                fillingObject?.SetActive(true);
                                fillingBar.SetFilling(timeToFill2);


                                // enable pouring script
                                grabbedObject.GetComponent<PourDetector1>().enabled = true;

                                string hitName2 = null;
                                if (Stream1.hit.collider != null)
                                {
                                    hitName2 = Stream1.hit.collider.gameObject.name;
                                }

                                if (hitName2 != null && (hitName2 == "bowl_collider" || hitName2 == "Dough1" || hitName2 == "Dough2" || hitName2 == "Dough3" || hitName2 == "Dough4" || hitName2 == "Dough5"))
                                {
                                        //increase time during filling position
                                        timeToFill2 += Time.deltaTime;

                                        // adjust filling scale
                                        fillingBar.SetFilling(timeToFill2);

                                        if (timeToFill2 >= fillingTime) // filling completed
                                        {
                                            //log poured object
                                            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"pouredObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                            PlaySound(successSound);
                                            timeToFill2 = 0;
                                            // Add object to list
                                            pouredObjects.Add(grabbedObject);

                                            // disable filling bar
                                            fillingObject?.SetActive(false);

                                            // teleportate outside kitchen
                                            StartCoroutine(TeleportIngrToCorrectPosition(sugarBox, sugarBox.outsidePosition));
                                            

                                        }
                            
                                }
                                

                            }

                        break;

                        case "flour_collider":
                            if (!pouredObjects.Contains(grabbedObject)) // not poured so far
                            {
                                //log grabbed object
                                AddToEventBuffer(StreamlinedInputManager.Now, Events.CollisionEvent, $"grabbedObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                // show filling bar
                                fillingObject?.SetActive(true);
                                fillingBar.SetFilling(timeToFill1);

                                // enable pouring script
                                grabbedObject.GetComponent<PourDetector2>().enabled = true;
                                
                                string hitName3 = null;
                                if(Stream2.hit.collider != null)
                                {
                                    hitName3 = Stream2.hit.collider.gameObject.name;
                                }

                                if (hitName3 != null && (hitName3 == "bowl_collider" || hitName3 == "Dough1" || hitName3 == "Dough2" || hitName3 == "Dough3" || hitName3 == "Dough4" || hitName3 == "Dough5"))
                                {
                                        //increase time during filling position
                                        timeToFill1 += Time.deltaTime;

                                        // adjust filling scale
                                        fillingBar.SetFilling(timeToFill1);

                                        if (timeToFill1 >= fillingTime) // filling completed
                                        {
                                            //log poured object
                                            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"pouredObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                            PlaySound(successSound);
                                            timeToFill1 = 0;

                                            // Add object to list
                                            pouredObjects.Add(grabbedObject);

                                            // disable filling bar
                                            fillingObject?.SetActive(false);

                                            // teleportate outside kitchen
                                            StartCoroutine(TeleportIngrToCorrectPosition(flourBox, flourBox.outsidePosition));
                                        
                                        }

                                }
 
                            }

                        break;

                        case "egg1_collider":
                            //log grabbed object
                            AddToEventBuffer(StreamlinedInputManager.Now, Events.CollisionEvent, $"grabbedObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                            //get current sensor readings for total force
                            for (int i = 0; i < forceSensors.Length; i++)
                            {
                                currentTotalForce += forceSensors[i].GetComponent<MjSiteScalarSensor>().SensorReading;
                            }
                            currentTotalForce /= forceSensors.Length; // calculate mean of force

                            //log total force
                            AddToValueBuffer("Stage3TotalForce", StreamlinedInputManager.Now, $"{currentTotalForce}", $"{(int)level}", $"{level}");

                            if (currentTotalForce >= maxBreakingForce && !(pouredObjects.Contains(grabbedObject) || fallenObjects.Contains(grabbedObject.name))) // egg broke
                            {
                                Debug.Log("Egg1 broken");
                                currentTotalForce = 0;

                                //log broken egg
                                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"brokenEgg:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                //change material to transparent
                                rendererGOEgg1.GetComponent<Renderer>().material = transparentMat;

                                // play Particle System
                                psEgg1?.Play();

                                // teleportate egg 
                                StartCoroutine(WaitAndTeleportateEgg(egg1.gameObj));

                                // check broke over bowl by overlapSphere
                                if (IsEggOverBowl(egg1.gameObj))
                                {
                                    PlaySound(successSound);
                                }
                                else
                                {
                                    fallenObjects.Add("egg1_collider");
                                    PlaySound(failureSound);
                                }

                            }
                            else if (currentTotalForce < maxBreakingForce && !(pouredObjects.Contains(grabbedObject) || fallenObjects.Contains(grabbedObject.name))) // cracking effect
                            {
                                BreakingEggEffect(rendererGOEgg1, currentTotalForce, ref hasPlayedCrackSound1);

                            }


                            break;

                        case "egg2_collider":
                            //log grabbed object
                            AddToEventBuffer(StreamlinedInputManager.Now, Events.CollisionEvent, $"grabbedObject:{grabbedObject.name}", $"{(int)level}", $"{level}");

                            //get current sensor readings for total force
                            for (int i = 0; i < forceSensors.Length; i++)
                            {
                                currentTotalForce += forceSensors[i].GetComponent<MjSiteScalarSensor>().SensorReading;
                            }
                            currentTotalForce /= forceSensors.Length; // calculate mean of force

                            //log total force
                            AddToValueBuffer("Stage3TotalForce", StreamlinedInputManager.Now, $"{currentTotalForce}", $"{(int)level}", $"{level}");

                            if (currentTotalForce >= maxBreakingForce && !(pouredObjects.Contains(grabbedObject) || fallenObjects.Contains(grabbedObject.name))) // egg broke
                            {
                                Debug.Log("Egg2 broken");
                                currentTotalForce = 0;

                                //log broken egg
                                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"brokenEgg:{grabbedObject.name}", $"{(int)level}", $"{level}");

                                //change material to transparent
                                rendererGOEgg2.GetComponent<Renderer>().material = transparentMat;

                                // play Particle System
                                psEgg2?.Play();


                                // teleportate egg 
                                StartCoroutine(WaitAndTeleportateEgg(egg2.gameObj));

                                // check broke over bowl by overlapSphere
                                if (IsEggOverBowl(egg2.gameObj))
                                {
                                    PlaySound(successSound);
                                }
                                else
                                {
                                    PlaySound(failureSound);
                                    fallenObjects.Add("egg2_collider");
                                }

                            }
                            else if (currentTotalForce < maxBreakingForce && !(pouredObjects.Contains(grabbedObject) || fallenObjects.Contains(grabbedObject.name))) // cracking effect
                            {
                                BreakingEggEffect(rendererGOEgg2, currentTotalForce, ref hasPlayedCrackSound2);
   
                            }

                            break ;
                    }


                }


                // Visualize filling in mixing bowl
                switch (pouredObjects.Count)
                {
                    case 1:
                        dough1?.SetActive(true);
                        break;
                    case 2:
                        dough2?.SetActive(true);
                        break;
                    case 3:
                        dough3?.SetActive(true);
                        break;
                    case 4: 
                        dough4?.SetActive(true);
                        break;
                    case 5:
                        dough5?.SetActive(true);
                        break;
                }


                // End of level
                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[1].levelDuration)
                {
                    // log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver(pouredObjects={pouredObjects.Count})", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Faile:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    //bowl?.SetActive(false);
                    level = Levels.HeatUpStove;
                    Debug.Log("Level3");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                //SUCCESS
                if (pouredObjects.Count >= numberToCollect) // success if 5 objects poured
                {
                    //log success
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:pouredObjects={pouredObjects.Count}", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    //bowl?.SetActive(false);

                    level = Levels.HeatUpStove;
                    Debug.Log("Level3");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                    break;
                }
                // FAIL
                else if((fallenObjects.Count >= numberToCollect - pouredObjects.Count)) // fail if too much objects fallen or broken
                {
                    //log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:fallenObjects={fallenObjects.Count}", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:fallenObjects", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    //bowl?.SetActive(false);

                    level = Levels.HeatUpStove;
                    Debug.Log("Level3");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

               
                break;

            /// <summary>
            /// heat up the stove top by rotating the knob
            /// access to jnob angle for success criteria
            /// 
            /// </summary>
            case Levels.HeatUpStove: // #3
                // reset bool for object placing from level 2 and for level 4
                placed = false;

                // instruction text
                DisableText(instructionText);
                instructionText[2].gameObject.SetActive(true); // enable the instruction text

                // teleport the ingredients
                if (!outsideArea)
                {
                    
                    StartCoroutine(TeleportIngrToCorrectPosition(flourBox, flourBox.outsidePosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(sugarBox, sugarBox.outsidePosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(milkBottle, milkBottle.outsidePosition));
                    StartCoroutine (TeleportIngrToCorrectPosition(egg1, egg1.outsidePosition));
                    StartCoroutine(TeleportIngrToCorrectPosition(egg2, egg2.outsidePosition));

                    outsideArea = true;
                }
                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[2].levelDuration)
                {
                    //log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver(knobAngle={knob.GetComponent<MjHingeJoint>().Configuration})", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.FlipPancake1;
                    Debug.Log("Level4");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                // compare current joint angle in degree
                if (knob != null && knob.GetComponent<MjHingeJoint>().Configuration <= requiredKnobAngle)  
                {
                    //SUCCESSFUL
                    //log success
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:knobAngle={knob.GetComponent<MjHingeJoint>().Configuration}", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.FlipPancake1;
                    Debug.Log("Level4");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                break;

            case Levels.FlipPancake1: // #4a
                // reset bool for outside area
                outsideArea = false;

                // instruction text
                DisableText(instructionText);
                instructionText[3].gameObject.SetActive(true); // enable the instruction text

                // show pan with pancake and spatula (execute just once)
                if (!placed)
                {
                    if(pan != null)
                    {
                        foreach (Renderer i in pan.GetComponentsInChildren<Renderer>())
                        { i.enabled = true; }
                        StartCoroutine(TeleportGOToCorrectPosition(pan, posPan));
                        
                    }
                    
                    if (pancakeRaw!=null && posPancakeInPan!=null)
                    {
                        // teleport raw pancake into pan
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeRaw,posPancakeInPan));
                        pancakeRaw.GetComponentInChildren<Renderer>().enabled = true;                        
                    }
                    if (spatula != null)
                    {
                        spatula.GetComponentInChildren<Renderer>().enabled = true;
                    }
                    placed = true;
                }

                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[3].levelDuration)
                {
                    //log Fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    flipped = false;
                    level = Levels.PlacePancake1;
                    Debug.Log("Level5");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear();

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                // SUCCESS
                if (flipped)
                {
                    //log Success
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:flipped", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    flipped = false;
                    level = Levels.PlacePancake1;
                    Debug.Log("Level5");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear();

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                    break;
                }
                
                if (fallenObjects.Contains("pancake_Raw_collider") || fallenObjects.Contains("spatula_collider"))
                {
                    //log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:fallenObjects", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:fallenObjects", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    flipped = false;
                    level = Levels.PlacePancake1;
                    Debug.Log("Level5");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear();

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                }

            

                break;

            case Levels.PlacePancake1: // #5a
                // reset bool for object placing from level 2 and for level 4
                placed = false;

                // instruction text
                DisableText(instructionText);
                instructionText[4].gameObject.SetActive(true); // enable the instruction text

                if (plate!=null) 
                    plate.GetComponent<Renderer>().enabled=true;

                if (!outsideArea)
                {
                    // show plate, teleport cooked pancake in pan, raw pancake outside and spatular to position (execute just once)
                    if (pan != null)
                    {
                        StartCoroutine(TeleportGOToCorrectPosition(pan, posPan));
                    }
                    if (pancakeRaw != null && posPancakeOutside != null)
                    {
                        //teleport raw pancake outside
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeRaw,posPancakeOutside));
                        pancakeRaw.GetComponentInChildren<Renderer>().enabled = false;
                    }
                    if (pancakeCooked1 != null && posPancakeInPan != null)
                    {
                        //teleport cooked pancake to pan
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeCooked1, posPancakeInPan));
                        pancakeCooked1.GetComponentInChildren<Renderer>().enabled = true;
                    }

                    if (spatula != null)
                    {
                        // teleport spatula
                        StartCoroutine(TeleportGOToCorrectPosition(spatula,posSpatula));
                        
                    }

                    outsideArea = true;
                }

                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[4].levelDuration)
                {
                    //log Fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.FlipPancake2;
                    Debug.Log("Level6");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                // SUCCESS
                if (onPlate)
                {
                    //log Success
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:onPlate", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    onPlate = false;
                    level = Levels.FlipPancake2;
                    Debug.Log("Level6");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }
                
                if (fallenObjects.Contains("pancake_Cooked1_collider") || fallenObjects.Contains("spatula_collider"))
                {
                    //log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:fallenObjects", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:fallenObjects", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    onPlate = false;
                    level = Levels.FlipPancake2;
                    Debug.Log("Level6");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }




                break;

            case Levels.FlipPancake2: // #4b
                // reset bool for outside area
                outsideArea = false;

                // instruction text
                DisableText(instructionText);
                instructionText[3].gameObject.SetActive(true); // enable the instruction text

                // show raw pancake in pan, cooked pancake on plate, teleport spatula to position (execute just once)
                if (!placed)
                {
                    if (pan != null)
                    {
                        StartCoroutine(TeleportGOToCorrectPosition(pan, posPan)); 
                    }
                    if (pancakeRaw != null && posPancakeInPan != null)
                    {
                        // teleport raw pancake inside pan
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeRaw, posPancakeInPan));
                        pancakeRaw.GetComponentInChildren<Renderer>().enabled = true;
                    }
                    if(pancakeCooked1!= null && posPancakePlate != null)
                    {
                        //teleport cooked pancake onto plate
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeCooked1,posPancakePlate));
                        pancakeCooked1.GetComponentInChildren<Renderer>().enabled = true;
                    }
                    if (spatula != null)
                    {
                        // teleport spatula to position
                        StartCoroutine(TeleportGOToCorrectPosition(spatula, posSpatula));
                        
                    }
                    placed = true;
                }


                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[3].levelDuration)
                {
                    //log Fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    flipped = false;
                    level = Levels.PlacePancake2;
                    Debug.Log("Level7");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear();

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                // SUCCESS
                if (flipped)
                {
                    //log Success
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:flipped", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    flipped = false;
                    level = Levels.PlacePancake2;
                    Debug.Log("Level7");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear();

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                    break;
                }
                
                if (fallenObjects.Contains("pancake_Raw_collider") || fallenObjects.Contains("spatula_collider"))
                {
                    //log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:fallenObjects", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:fallenObjects", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    flipped = false;
                    level = Levels.PlacePancake2;
                    Debug.Log("Level7");
                    PlaySound(startSound);
                    elapsedTime = 0;
                    fallenObjects.Clear();

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }




                break;

            case Levels.PlacePancake2: // #5b
                // reset bool for object placing from level 2 and for level 4
                placed = false;

                // instruction text
                DisableText(instructionText);
                instructionText[4].gameObject.SetActive(true); // enable the instruction text

                // teleport cooked pancake in pan, raw pancake outside and spatular to position (execute just once)
                if (!outsideArea)
                {
                    if (pan != null)
                    {
                        StartCoroutine(TeleportGOToCorrectPosition(pan, posPan));
                    }
                    if (pancakeRaw != null && posPancakeOutside != null)
                    {
                        //teleport raw pancake outside
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeRaw, posPancakeOutside));
                        pancakeRaw.GetComponentInChildren<Renderer>().enabled = false;
                    }
                    if (pancakeCooked2 != null && posPancakeInPan != null)
                    {
                        //teleport cooked pancake to pan
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeCooked2, posPancakeInPan));
                        pancakeCooked2.GetComponentInChildren<Renderer>().enabled = true;
                    }

                    if (spatula != null)
                    {
                        // teleport spatula
                        StartCoroutine(TeleportGOToCorrectPosition(spatula,posSpatula));
                        
                    }

                    outsideArea = true;
                }

                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[4].levelDuration)
                {
                    //log Fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.SqueezeSyrup;
                    Debug.Log("Level8");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                // SUCCESS
                if (onPlate)
                {
                    //log Success
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:onPlate", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    onPlate = false;
                    level = Levels.SqueezeSyrup;
                    Debug.Log("Level8");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                    break;
                }

                if (fallenObjects.Contains("pancake_Cooked2_collider") || fallenObjects.Contains("spatula_collider"))
                {
                    //log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:fallenObjects", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:fallenObjects", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    onPlate = false;
                    level = Levels.SqueezeSyrup;
                    Debug.Log("Level8");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }

                


                break;

            case Levels.SqueezeSyrup: // #6
                 // reset bool for outside area
                outsideArea = false;

                // instruction text
                DisableText(instructionText);
                instructionText[5].gameObject.SetActive(true); // enable the instruction text

                fillingObjectSyrup?.SetActive(true);
                fillingBarSyrup.SetFilling(timeToSqueeze);

                // teleport cooked pancake onto plate
                if (!placed)
                {
                    if (pancakeCooked2 != null && posPancakePlate != null)
                    {
                        //teleport cooked pancake onto plate
                        StartCoroutine(TeleportGOToCorrectPosition(pancakeCooked2,posPancakePlate));
                    }

                    if(syrup != null)
                    {
                        StartCoroutine(TeleportGOToCorrectPosition(syrup, posSyrup));
                        foreach (Renderer i in syrup.GetComponentsInChildren<Renderer>())
                        { i.enabled = true; }
                    }
                    placed = true;
                }

                // TIME OVER
                if (timeLimit && elapsedTime >= levelTime[5].levelDuration)
                {
                    //log Fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:TimeOver", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.Final;
                    Debug.Log("Done");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }




                //get current sensor readings for total force
                for (int i = 0; i < forceSensors.Length; i++)
                {
                    currentTotalForceSyrup += forceSensors[i].GetComponent<MjSiteScalarSensor>().SensorReading;
                }
                currentTotalForceSyrup /= forceSensors.Length; // calculate mean of force

                //log total force
                AddToValueBuffer("Stage3TotalForce", StreamlinedInputManager.Now, $"{currentTotalForceSyrup}", $"{(int)level}", $"{level}");


                // check hitted objects
                string hitName4 = null;
                if (StreamSyrup.hit.collider != null)
                {
                    hitName4 = StreamSyrup.hit.collider.gameObject.name;
                }

                
                if (hitName4 != null && (hitName4 == "plate" || hitName4 == "pancake_Cooked1_collider" || hitName4 == "pancake_Cooked2_collider"))
                {
                    //increase time during filling position
                    timeToSqueeze += Time.deltaTime;
                    
                    // adjust filling scale
                    fillingBarSyrup.SetFilling(timeToSqueeze);
                    if (timeToSqueeze >= fillingTime)
                    {
                        // SUCCESS
                        //log Success
                        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success:squeezedSyrup", $"{(int)level}", $"{level}");
                        AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Success", $"{(int)level}", $"{level}", $"{elapsedTime}");

                        PlaySound(successSound);
                        timeToSqueeze = 0;

                        // disable filling bar
                        fillingObjectSyrup?.SetActive(false);

                        level = Levels.Final;
                        Debug.Log("Done");
                        elapsedTime = 0;

                        //log next level
                        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                        AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    }

                }
                    


                if (fallenObjects.Contains("syrup_renderer"))
                {
                    //log fail
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:fallenObject=syrup", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Fail:fallenObject", $"{(int)level}", $"{level}", $"{elapsedTime}");

                    level = Levels.Final;
                    Debug.Log("Done");
                    PlaySound(startSound);
                    elapsedTime = 0;

                    //log next level
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextLevel", $"{(int)level}", $"{level}");
                    AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextLevel", $"{(int)level}", $"{level}", $"{elapsedTime}");
                }


                break;

            case Levels.Final:

                // teleport maple syrup outside
                if (!outsideArea)
                {
                    StartCoroutine(TeleportGOToCorrectPosition(syrup, posOutside));
                    outsideArea = true;
                }

                DisableText(instructionText);
                instructionText[6].gameObject.SetActive(true); // enable the instruction text


                break;
        }
    }

    #region CUSTOM FUNCTIONS

    public void PlaySound(AudioSource sound)
    {
        if (sound != null)
        {
            sound.Play();
        }
    }

    public void DisableText(GameObject[] text)
    {
        for(int i = 0; i < text.Length; i++)
        {
           text[i]?.gameObject.SetActive(false);
        }
    }


    IEnumerator TeleportIngrToCorrectPosition(Ingredient ingr, GameObject pos)
    {
        // teleport
        if (ingr.freeJoint != null)
        {
            MjState.TeleportMjRoot(ingr.freeJoint, pos.transform.position, pos.transform.rotation);
        }
              

        yield return new WaitForSeconds(0.05f);
            
        // check 
        bool positionCorrect = Vector3.Distance(ingr.gameObj.transform.position, pos.transform.position) < 0.05f;
        bool rotationCorrect = Quaternion.Angle(ingr.gameObj.transform.rotation, pos.transform.rotation) < 1f;

        if (!positionCorrect || !rotationCorrect)
        {
           // teleport again
            MjState.TeleportMjRoot(ingr.freeJoint, pos.transform.position, pos.transform.rotation);
        }
        else
        {
            yield break;
        }

    }

    IEnumerator TeleportGOToCorrectPosition(GameObject go, GameObject pos)
    {
        MjFreeJoint freeJoint = null;
        freeJoint = go.GetComponentInChildren<MjFreeJoint>();
        // teleport
        if (freeJoint != null)
        {
            MjState.TeleportMjRoot(freeJoint, pos.transform.position, pos.transform.rotation); 
        }
        else
        {
            yield break;
        }


        yield return new WaitForSeconds(0.3f);

        // check 
        bool positionCorrect = Vector3.Distance(go.transform.position, pos.transform.position) < 0.05f;
        bool rotationCorrect = Quaternion.Angle(go.transform.rotation, pos.transform.rotation) < 1f;

        if (!positionCorrect || !rotationCorrect)
        {
            // teleport again
            MjState.TeleportMjRoot(freeJoint, pos.transform.position, pos.transform.rotation);
        }
        else
        {
            yield break;
        }

    }

    IEnumerator WaitAndTeleportateEgg(GameObject obj)
    {
        yield return new WaitForSeconds(3);
        
        if (obj.name == "egg1")
        {
            StartCoroutine(TeleportIngrToCorrectPosition(egg1, egg1.outsidePosition));
           
        }
        else if (obj.name == "egg2")
        {
           StartCoroutine(TeleportIngrToCorrectPosition(egg2, egg2.outsidePosition));

        }

    }

    public bool IsEggOverBowl(GameObject egg, float radius = 0.05f)
    {
        // check by overlapping colliders for the position of the egg
        Collider[] hitColliders = Physics.OverlapSphere(egg.transform.position, radius);
        foreach (var hit in hitColliders)
        {
            if (hit.gameObject.name == "completeBowl_collider") // Successful broken over bowl
            {
                //log poured egg
                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"pouredObject:{grabbedObject.name}", $"{(int)level}", $"{level}");
                // Add object to list
                if (!pouredObjects.Contains(grabbedObject))
                    pouredObjects.Add(grabbedObject);
                return true;
            }
        }

        return false;
    }

    public void BreakingEggEffect(GameObject rendererObject, double currentForce, ref bool hasPlayedCrackSound, float crackSoundTime = 0.4f)
    {
        // change the material for cracks by 60% of max force and play crackingsound
        if (currentForce >= maxBreakingForce * crackSoundTime && !hasPlayedCrackSound)
        {
            // play cracking sound
            PlaySound(eggSound);
            hasPlayedCrackSound = true;
        }
        if (currentForce <= maxBreakingForce * crackSoundTime && hasPlayedCrackSound)
        {
            hasPlayedCrackSound = false;
        }
        // adjust material and show crack depending on force
        float crackAmount = Mathf.InverseLerp(0, (float)maxBreakingForce, (float)currentForce);
        rendererObject.GetComponent<Renderer>().material.SetFloat("_crackAmount", crackAmount);
        Debug.Log("Crackamount: " + crackAmount + " currentForce: " + currentForce);
    }


    #endregion

    #region LOGGING FUNCTIONS

    public void AddToEventBuffer(double now, Events ev, string name, string number, string level)
    {
        // "time_stamp_s" + "," + "participant" + "," + "event" + "," + "name" + "," + "level_number" + "," + "level_name" + Environment.NewLine;
        string addBuffer = $"{now},{participantID},{ev.ToString()},{name},{number},{level},{Environment.NewLine}";
        LoggingManager.AddToBuffer("Stage3StudyEvent" , addBuffer);
    }
    public void AddToValueBuffer(string fileName, double now, string values, string number, string level)
    {
        //"time_stamp_s" + "," + "participant" + "," + "total_force" + "," + "level_number" + "," + "level_name" + Environment.NewLine;
        string addBuffer = $"{now},{participantID},{values},{number},{level}{Environment.NewLine}";
        LoggingManager.AddToBuffer(fileName, addBuffer);
    }

    public void AddToLevelBuffer(double now, Events ev, string name, string number, string level, string time)
    {
        // "time_stamp_s" + "," + "participant" + "," + "event" + "," + "name" + "," + "level_number" + "," + "level_name" + "elapsed_time" + "," + "total_elapsed_time_s" + Environment.NewLine;
        string addBuffer = $"{now},{participantID},{ev.ToString()},{name},{number},{level},{time},{totalElapsedTime},{Environment.NewLine}";
        LoggingManager.AddToBuffer("Stage3LevelChange", addBuffer);
    }

    #endregion


    #region UI

    public void StartButton()
    {
        // works like a pause
        if (level == Levels.None)
        {
            // change button color
            GameObject startBtn = GameObject.Find("StartButton");
            if (startBtn != null)
            {
                startBtn.GetComponent<Image>().color = Color.red;  
            }

            level = levelPause;
            PlaySound(startSound);
            running = true;

            // log start
            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Start",$"{(int)level}", $"{level}");
            AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Start", $"{(int)level}", $"{level}", $"{elapsedTime}");
        }
        else
        {
            levelPause = level;

            level = Levels.None;

            // change button color back
            GameObject goBtn = GameObject.Find("StartButton");
            if (goBtn != null)
            {
                goBtn.GetComponent<Image>().color = new Color(r: 0.654902f, g: 0.9058824f, b: 0.6745098f);
            }
            running = false;

            // log pause
            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Pause", $"{(int)level}", $"{level}");
            AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"Pause", $"{(int)level}", $"{level}", $"{elapsedTime}");

            return;
        }

    }

    public void NextButton()
    {
        elapsedTime = 0;
        fallenObjects.Clear(); // clear the list for next level
        if (running)
        {
            switch (level)
            {
                case Levels.None:
                    level = Levels.CollectIngredients;
                    PlaySound(startSound);
                    break;
                case Levels.CollectIngredients:
                    fallenObjects.Clear();
                    level = Levels.PourIngredients;
                    PlaySound(startSound);
                    break;
                case Levels.PourIngredients:
                    fallenObjects.Clear();
                    level = Levels.HeatUpStove;
                    PlaySound(startSound);
                    break;
                case Levels.HeatUpStove:
                    level = Levels.FlipPancake1;
                    fallenObjects.Clear();
                    PlaySound(startSound);
                    break;
                case Levels.FlipPancake1:
                    level = Levels.PlacePancake1;
                    fallenObjects.Clear();
                    PlaySound(startSound);
                    break;
                case Levels.PlacePancake1:
                    level = Levels.FlipPancake2;
                    fallenObjects.Clear();
                    PlaySound(startSound);
                    break;
                case Levels.FlipPancake2:
                    fallenObjects.Clear();
                    level = Levels.PlacePancake2;
                    PlaySound(startSound);
                    break;
                case Levels.PlacePancake2:
                    level = Levels.SqueezeSyrup;
                    fallenObjects.Clear();
                    PlaySound(startSound);
                    break;
                case Levels.SqueezeSyrup:
                    level = Levels.Final;
                    PlaySound(startSound);
                    break;
                case Levels.Final:
                    level = Levels.None;
                    break;
            }
        }

        // log next button
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextBtn", $"{(int)level}", $"{level}");
        AddToLevelBuffer(StreamlinedInputManager.Now, Events.LevelChange, $"NextBtn", $"{(int)level}", $"{level}", $"{elapsedTime}");
    }

    public void OnToggleChange(bool toggle)
    {
        timeLimit = toggle; // pass UI interaction to boolean
    }
    
  
    #endregion
}
