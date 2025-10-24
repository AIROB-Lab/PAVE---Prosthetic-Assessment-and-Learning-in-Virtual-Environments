using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;
using UnityEngine.Events;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;

public enum TaskState
{
    NotStarted,     // Training not started
    Started,        // Training started over UI button 
    Running,        // Task is shown, Training runs
    Successful,     // Task completed succsessfully
    TimeOver,       // Timer ended
    Skip            // Pressed Next Task UI btn
}      


public class TaskManager : MonoBehaviour
{
    #region public variables
    public int participantID;

    public enum Events
    {
        StudyEvent,
        TaskOrder
    }

    [Header("Game Objects")]
    public GameObject[] tasks; // Array of Tasks
    public GameObject[] buttons; // array of buttons for button task
    public GameObject foreground; // foreground position selected in Inspector

    [Header("Audio Sources")]
    public AudioSource startSound;
    public AudioSource successSound;
    public AudioSource timeOverSound;

    [Header("Thresholds")]
    [Range(1, 10)] public int numberOfRounds = 2;
    public float taskTime = 10.0f; // time to cmplete the task
    public float nextTaskDelay = 3; // time between tasks

    [HideInInspector] public static TaskState taskState = TaskState.NotStarted;
    public static bool wpsTask = false;

    [Header("Text")]
    public TextMeshProUGUI countdownText;
    #endregion



    #region private variables
    private int currentTaskIndex = 0;
    private GameObject[] shuffledTasks;
    [SerializeField] private GameObject completionText;
    private GameObject[] background; // background position
    private GameObject[] objects; // array for game objects
    private int currentRound = 0;
    private float timeToComplete;
    private bool ballTask = false;


    // log header
    private string eventHeader = "time_stamp_s" + "," + "participant" + "," + "event" + "," + "name" + "," + "task_name" + "," + "task_number" + "," + "training_round" + Environment.NewLine;
    //private string valueHeader = "time_stamp_s" + "," + "participant" + "," + "values" + Environment.NewLine;
    //private string taskHeader = "time_stamp_s" + "," + "participant" + "," + "Task" + "," + "number" + "," + "round" + "," + "state" + Environment.NewLine;

    #endregion

    // Start is called before the first frame update
    void Start()
    {

        completionText.SetActive(false);
        timeToComplete = taskTime;
        currentTaskIndex = 0;
        currentRound = 0;
        
        // Logging
        LoggingManager.CreateNewLog("Stage2StudyEvent", eventHeader, 5f);
        LoggingManager.CreateNewLog("Stage2TaskOrder", eventHeader, 5f);
        //LoggingManager.CreateNewLog("Stage2RMS", valueHeader, 5f);


    }

    // Update is called once per frame
    void Update()
    {

        if (taskState == TaskState.Started) // inital start of tasks when click "Start Training"
        {
            if (tasks.Length >= 1)
            {
                shuffledTasks = ShuffleTasks(tasks);

            }

            // log Start of Training
            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Start" , shuffledTasks[currentTaskIndex].name.ToString(), currentTaskIndex.ToString(), currentRound.ToString());
            AddToEventBuffer(StreamlinedInputManager.Now, Events.TaskOrder, $"{taskState}", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");

            ShowNextTask();
        }
        else if (taskState == TaskState.Running) // countdown TaskTime while running
        {
           

            timeToComplete -= Time.deltaTime;

            if (countdownText != null)
            {
                countdownText.text = $"Time to complete: {timeToComplete:F2}"; // Show time left on UI
            }

            if (timeToComplete <= 0f) // end task if time over
            {
                taskState = TaskState.TimeOver;
                EndCurrentTask();
            }
        }



        if (currentRound == numberOfRounds)
        {
            // end of training
            completionText.SetActive(true);

        }

    }
    

    // randomize/shuffle tasks by Fisher-Yates algorithm
    private GameObject[] ShuffleTasks(GameObject[] originalTasks)
    {
        GameObject[] shuffleTasks = (GameObject[])originalTasks.Clone();

        for (int i = shuffleTasks.Length - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1); // pick random index from 0 to i

            // swap current task with element at random index
            GameObject temp = shuffleTasks[i];
            shuffleTasks[i] = shuffleTasks[randomIndex];
            shuffleTasks[randomIndex] = temp;

        }
        return shuffleTasks;
    }


    // move current task to foreground
    public void ShowNextTask()
    {
        taskState = TaskState.Running;

        // log running state
        AddToEventBuffer(StreamlinedInputManager.Now, Events.TaskOrder, $"{taskState}", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");

        PlaySound(startSound);

        // log next task
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextTask", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
        Debug.Log("currentTask: " + currentTaskIndex + " " + shuffledTasks[currentTaskIndex].name + ", currentRound: " + currentRound);


        MjFreeJoint[] currentFreejoint = null;


        if (currentTaskIndex < shuffledTasks.Length)
        {
            // get freejoint of current task
            currentFreejoint = shuffledTasks[currentTaskIndex].GetComponentsInChildren<MjFreeJoint>();

            if (currentFreejoint != null)
            {
                // move current task to foreground
                if (shuffledTasks[currentTaskIndex].name == "CatchAndCollect Task")
                {
                    wpsTask = true;
                    MjState.TeleportMjRoot(currentFreejoint[0], foreground.transform.position + new Vector3(-0.15f,0,0), foreground.transform.rotation);
                    MjState.TeleportMjRoot(currentFreejoint[1], foreground.transform.position + new Vector3(0.15f, 0, 0), foreground.transform.rotation);

                    

                }
                else if(shuffledTasks[currentTaskIndex].name == "Ball Task")
                {
                    MjState.TeleportMjRoot(currentFreejoint[0], foreground.transform.position + new Vector3(0, 0.1f, 0), foreground.transform.rotation);
                    MjState.TeleportMjRoot(currentFreejoint[1], foreground.transform.position + new Vector3(0, 0, 0), foreground.transform.rotation);
                }
                else
                {
                    for (int i = 0; i < currentFreejoint.Length; i++)
                    {

                        MjState.TeleportMjRoot(currentFreejoint[i], foreground.transform.position, foreground.transform.rotation);
                    }
                }
                


            }

            if (shuffledTasks[currentTaskIndex].name == "Button Task")
            {
                ButtonToPress();
            }



            if (shuffledTasks[currentTaskIndex].name == "Ball Task")
            {
                GameObject.Find("Ball_geom").GetComponent<MeshRenderer>().material.EnableKeyword("_EMISSION");
                ballTask = true;

            }

           
            timeToComplete = taskTime; // reset task countdown  

        }
        

    }


    // move current task to background if task completed
    public void EndCurrentTask()
    {
        MjFreeJoint[] currentFreejoint = null;

        if (wpsTask)
        {
            wpsTask = false;
            //Todo: remove bowl from MPL hand
        }

        if (ballTask)
        {
            ballTask = false;
            CollisionBall.dynamicMaxForce = 0.1f; // reset dynamic max force
        }

        if (currentTaskIndex <= shuffledTasks.Length)
        {
            if (shuffledTasks[currentTaskIndex].name == "Ball Task")
            {
                GameObject.Find("Ball_geom").GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");

            }


            // TASK COMPLETION?
            if (taskState == TaskState.Successful)
            {
                // successful task
                PlaySound(successSound);

                // log success
                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Success", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
                AddToEventBuffer(StreamlinedInputManager.Now, Events.TaskOrder, $"{taskState}", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
            }
            else if (taskState == TaskState.TimeOver)
            {
                // time over
                PlaySound(timeOverSound);

                // log fail
                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:TimeOver", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
                AddToEventBuffer(StreamlinedInputManager.Now, Events.TaskOrder, $"{taskState}", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
            }
            else if (taskState == TaskState.Skip)
            {
                // time over
                PlaySound(timeOverSound);

                // log fail
                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Fail:Skip", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
                AddToEventBuffer(StreamlinedInputManager.Now, Events.TaskOrder, $"{taskState}", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
            }




                // MOVE TO BACKGROUND
                if (currentTaskIndex < shuffledTasks.Length)
            {
                /// get freejoint of current task
                currentFreejoint = shuffledTasks[currentTaskIndex].GetComponentsInChildren<MjFreeJoint>();

                /// get Background of current Task
                if (currentFreejoint != null)
                {
                    background = GetBackground(shuffledTasks, currentTaskIndex);
                }

                if (background != null)
                {
                    /// move current task to background

                    for (int i = 0; i < currentFreejoint.Length; i++)
                    {
                        MjState.TeleportMjRoot(currentFreejoint[i], background[i].transform.position, background[i].transform.rotation);
                    }



                }
            }



            currentTaskIndex++; //switch to next task 

            // NEXT ROUND ?
            if (currentTaskIndex < shuffledTasks.Length)
            {
                // next Task
                StartCoroutine(WaitForNextTask());
            }
            else if (currentRound < numberOfRounds)
            {
                //next Round
                currentRound++;
                currentTaskIndex = 0;

                if (currentRound < numberOfRounds) //still in next round
                {
                    shuffledTasks = ShuffleTasks(shuffledTasks);
                    StartCoroutine(WaitForNextTask());

                    // log next round
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextRound", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
                }

            }
            else
            {
                // end of training
                completionText.SetActive(true);

                //log end of training
                AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"EndTraining", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");
            }

        }




    }


    public void PlaySound(AudioSource sound)
    {
        if (sound != null)
        {
            sound.Play();
        }
    }


    // select random button to click
    public void ButtonToPress()
    {
        int randomIndex;

        if (buttons != null)
        {
            randomIndex = UnityEngine.Random.Range(0, buttons.Length);  // [min.Inclusive, max.Exclusive)

            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].GetComponent<MeshRenderer>().material.DisableKeyword("_EMISSION");
                buttons[i].GetComponent<Collider>().isTrigger = false;
            }

            // set random button emissive
            buttons[randomIndex].GetComponent<MeshRenderer>().material.EnableKeyword("_EMISSION");
            buttons[randomIndex].GetComponent<Collider>().isTrigger = true;
        }


    }


 

    public void AddToEventBuffer(double now, Events ev, string name, string task, string number, string round)
    {
        // "time_stamp_s" + "," + "participant" + "," + "event" + "," + "name" + "," + "task_name" + "," + "task_number" + "," + "training_round" + Environment.NewLine;
        string addBuffer = $"{now},{participantID},{ev.ToString()},{name},{task},{number},{round},{Environment.NewLine}";
        LoggingManager.AddToBuffer("Stage2" + ev.ToString(), addBuffer);
    }
    public void AddToValueBuffer(string fileName, double now, string values)
    {
        //"time_stamp_s" + "," + "participant" + "," + "values" + Environment.NewLine;
        string addBuffer = $"{now},{participantID},{values},{Environment.NewLine}";
        LoggingManager.AddToBuffer(fileName, addBuffer);
    }




    // get background position of certain task
    public GameObject[] GetBackground(GameObject[] tasks, int index)
    {
        MjFreeJoint[] freeJoint = null;
        // get freejoint of current task
        freeJoint = tasks[index].GetComponentsInChildren<MjFreeJoint>();


        GameObject[] position = new GameObject[freeJoint.Length];
        objects = new GameObject[tasks[index].transform.childCount];
        int j = 0;

        for (int i = 0; i < tasks[index].transform.childCount; i++)
        {
            // get background position of current task
            objects[i] = tasks[index].transform.GetChild(i).gameObject;
            if (objects[i] != null && objects[i].tag == "BackgroundPosition")
            {
                position[j] = objects[i];
                j++;
            }
        }
        return position;
    }


    // Wait before nex task
    IEnumerator WaitForNextTask()
    {
        yield return new WaitForSeconds(nextTaskDelay);
        ShowNextTask();
    }




    #region UI
    public void LoadScene()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;

        SceneManager.LoadScene(currentSceneName);
    }

    public void SkipTask()
    {
        taskState = TaskState.Skip;

        // log next btn
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"NextBtn", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");

        EndCurrentTask();
    }

    public void StartTraining()
    {
        if (taskState == TaskState.NotStarted)
        {
            // change button color
            GameObject startBtn = GameObject.Find("StartTraining");
            if (startBtn != null)
            {
                startBtn.GetComponent<Image>().color = Color.red;
            }


            taskState = TaskState.Started;

            //TODO: log start .....
        }
        else
        {
            StopTraining();
            return;
        }

    }

    public void StopTraining()
    {
        // End of Training = move task back & reset everything 
        taskState = TaskState.NotStarted;

        //log manual stop 
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"StopBtn", $"{shuffledTasks[currentTaskIndex].name}", $"{currentTaskIndex}", $"{currentRound}");

        // MOVE TO BACKGROUND
        MjFreeJoint[] currentFreejoint = null;
        if (currentTaskIndex < shuffledTasks.Length)
        {
            /// get freejoint of current task
            currentFreejoint = shuffledTasks[currentTaskIndex].GetComponentsInChildren<MjFreeJoint>();

            /// get Background of current Task
            if (currentFreejoint != null)
            {
                background = GetBackground(shuffledTasks, currentTaskIndex);
            }

            if (background != null)
            {
                /// move current task to background

                for (int i = 0; i < currentFreejoint.Length; i++)
                {
                    MjState.TeleportMjRoot(currentFreejoint[i], background[i].transform.position, background[i].transform.rotation);
                }



            }
        }




        currentRound = 0;
        currentTaskIndex = 0;
        completionText.SetActive(false);


        // change button color back
        GameObject goBtn = GameObject.Find("StartTraining");
        if (goBtn != null)
        {
            goBtn.GetComponent<Image>().color = new Color(r: 0.6536134f, g: 0.9056604f, b: 0.6730896f);
        }

        
    }



    #endregion














}
