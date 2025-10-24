using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;

public class CatchAndCollect : MonoBehaviour
{
    public MjFreeJoint[] freeJoints; // freejoints of each cube
    public GameObject[] positions; // position cubes to teleport
    public float teleportDelay = 2f;
    public int cubesToCollect = 1;
    public TaskManager taskManager;
    public ParticleSystem confetti_PS;

    public GameObject bowl;
    public GameObject hannes;
    public Vector3 bowlPosition = new Vector3(-0.021f, -0.026f, -0.06f);
    private Vector3 posBowlInHand;
    private bool placed = false;
    private MjFreeJoint bowlFreejoint;
    

    private int currentIndex = 0;
    private float timer = 0;

    [HideInInspector] public bool[] stopTeleport;  // true, when cube is triggered with hand (stop teleportation)
    [HideInInspector] public bool[] cubeReachedGround; // true, when cube triggered the ground of box


    private void Awake()
    {
       
    }

    // Start is called before the first frame update
    void Start()
    {
        stopTeleport = new bool[freeJoints.Length]; //default: false
        cubeReachedGround = new bool[freeJoints.Length];
        posBowlInHand = bowl.transform.position;

        //if (bowl != null)
        //{
           
        //    bowlFreejoint = bowl.GetComponentInChildren<MjFreeJoint>();
        //    if (bowlFreejoint != null)
        //    {
        //        MjState.TeleportMjRoot(bowlFreejoint, new Vector3(100, 0, 100), new Quaternion(0, 0, 0, 0)); // teleport bowl outside
        //    }
        //    //bowl.transform.localPosition = new Vector3(100, -0.2f, 100); //MPL hand 
        //}
    }


    private void FixedUpdate()
    {
        if (TaskManager.wpsTask && TaskManager.taskState == TaskState.Running) // wps task is running
        {
            //if (hannes != null)
            //{
            //    if(bowlFreejoint != null && !placed)
            //    {
            //        MjState.TeleportMjRoot(bowlFreejoint, hannes.transform.position + posBowlInHand, new Quaternion(0, 0, 0.984807789f, -0.173648089f)); // teleport bowl to hand
            //        //bowl.transform.localPosition = bowlPosition;  // bowl teleported to hand
            //        placed = true;
            //    }

            //}
            // update timer
            timer += Time.deltaTime;

            if (timer >= teleportDelay && currentIndex < freeJoints.Length)
            {
                // jump to next object, if collision detected or cube in container
                while (currentIndex < freeJoints.Length && stopTeleport[currentIndex] && cubeReachedGround[currentIndex])
                {
                    currentIndex++;
                }

                if (currentIndex < freeJoints.Length)
                {
                    // move object to position
                    MjState.TeleportMjRoot(freeJoints[currentIndex], positions[currentIndex].transform.position, positions[currentIndex].transform.rotation);

                    //reset timer and next object
                    timer = 0;
                    currentIndex++;

                    //reset index
                    if (currentIndex > freeJoints.Length)
                    {
                        currentIndex = 0;
                    }
                }

            }

            // check if cubes to collect is fullfilled
            if (IsMissionCompleted(cubeReachedGround))
            {
                // End Task
                if (confetti_PS != null)
                    confetti_PS.Play();

                for (int i = 0; i < freeJoints.Length; i++) // reset boolean
                {
                    cubeReachedGround[i] = false;
                }


                TaskManager.taskState = TaskState.Successful;
                taskManager.EndCurrentTask();


            }

            //reset index
            if (currentIndex >= freeJoints.Length)
            {
                currentIndex = 0;
            }

        }
        else
        {
            //if(bowl!= null)
            //{
            //    if (bowlFreejoint != null)
            //    {
            //        MjState.TeleportMjRoot(bowlFreejoint, new Vector3(100, 0, 100), new Quaternion(0, 0, 0, 0)); // teleport bowl outside
            //        placed = false;
            //    }
            //    //bowl.transform.localPosition = new Vector3(100, -0.2f, 100);

            //}

            for (int i = 0; i < freeJoints.Length; i++) // reset boolean
            {
                cubeReachedGround[i] = false;
                stopTeleport[i] = false;
            }

        }


    }


    // Update is called once per frame
    void Update()
    {
        
       


    }


    public bool IsMissionCompleted(bool[] missionList)
    {
        int x = 0;
        for (int i = 0; i < missionList.Length; i++)
        {
            if (missionList[i] == true)
            {
                x++;
                
            }
        }

        if (x == cubesToCollect)
            return true;
        else
            return false;
        
    }



}
