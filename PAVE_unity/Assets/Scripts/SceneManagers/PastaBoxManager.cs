using Mujoco;
using System;
using System.Collections;
using TMPro;
//using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static PastaBoxClasses;
using Color = UnityEngine.Color;

public enum Failure
{
    off,
    low,
    medium,
    high
}

public class PastaBoxManager : MonoBehaviour
{
    #region additional ENUMS/CLASSES/STRUCTS
    public enum PastaBoxMovs
    {
        // before start and after finish
        None,

        // from A:Start to B:MidShelfTarget
        Mov0,

        // from B: MidShelfTarget to C:HighShelfTarget
        Mov1,

        // from C: HighShelfTarget to A:Start
        Mov2,
    }
    public enum PastaBoxPhases
    {
        // before start and and after finish
        None,

        /* Original:
         * Start: Hand leaves the home position
         * First occurrence of the hand exceeding the ‘Hand Velocity Threshold’ OR first occurrence of the hand exceeding 
         * the ‘Target Distance Threshold’, whichever happens first
         * 
         * Changed to:
         * Start: Hand leaves the home position
         * Trigger Exit (no hand tag collides with obj anymore)
         */
        Reach,

        /*Start: Closing of grip aperture 
         * Original:
         * First occurrence of the hand falling below the ‘Grasp Distance Threshold’
         * Changed to: First time hand collides with grasp collider box (70 mm larger than pastaBox)
         * Fails, if object was touched and afterwards hand goes outside of grasp collider box
         */     
        Grasp,

        /*Start: Start of object movement
         * Original:
         * First occurrence of the object exceeding the ‘Object Velocity Threshold’ 
         * OR first occurrence of the object exceeding the ‘Target Distance Threshold’, whichever happens first
         * 
         * Changed to: 
         * When the  grasp collider box leaves the table (not target as might be pushed away from it)
         */
        Transport,

        /*Start: End of object movement
         * Original:
         * First occurrence of the object falling below the ‘Object Velocity Threshold’ 
         * OR first occurrence of the object distance falling below the ‘Target Distance Threshold’, whichever happens last 
         * 
         * Changed to:
         * When the grasp collider box touches the next target
         * 
         * End: End of grip aperture opening 
         * Last occurrence of the hand before exceeding the ‘Release Distance Threshold’*/
        Release
    }

    public enum Events
    {
        GameEvent,
        StudyEvent,
        PhaseChange,
        MovChange
            // tbc
    }

    [System.Serializable]
    // ToDo: Load a list of PastaBoxStates from JSON, so that just the participant needs to be selected (should be possible to also select the round)
    public class PastaBoxState
    {
        public int participant_id;
        public int cell;
        public string cell_name;
        public int run; // being incremented
        public Failure failure = Failure.off;
        public bool delay = false;
        public Color color;
        public float completionPerc;
        public bool pbTouched = false;

        public FailRun[] failRuns;

        public PastaBoxState(int participant = -1, int cell = -1, string cell_name = "-1",int run = -1, Failure failure = Failure.off, bool delay = false, Color? color = null, FailRun[] failRuns = null)
        {
            this.participant_id = participant;
            this.cell = cell;
            this.cell_name = cell_name;
            this.run = run;
            this.failure = failure;
            this.delay = delay;
            this.failRuns = failRuns.CloneViaSerialization();

            // color cannot be preset in constructor => if null then set it to standard darkgrey
            if (color != null) this.color = new Color(0.1019608f, 0.1019608f, 0.1019608f, 1);
            else this.color = color.Value;
        }

        public PastaBoxMovs currentMov = PastaBoxMovs.None;
        public PastaBoxPhases currentPhase = PastaBoxPhases.None;
        public GameObject currentFrom = null;
        public GameObject currentTo = null;

        public PastaBoxMovs[] movsOrder = new PastaBoxMovs[3] { PastaBoxMovs.Mov0, PastaBoxMovs.Mov1, PastaBoxMovs.Mov2 };

        public bool? succeeded = null;

        public bool running = false;
        public bool benchmarkRunning = false;
    }

    public GameObject HandController;

    [System.Serializable]
    public struct SinParameters
    {
        public float duration;
        public float sinFreq_max;
        public float sinFreq_min;
        public float amplitude_min;
        public float amplitude_max;
        public float samplingFreq;

        public float startRampRatio;
        public float endRampRatio;
    }
    #endregion

    #region public
    // public
    public int SET_PARTICIPANT;
    public int SET_CELL;
    public int SET_RUN;
    public int NUM_RUNS_PER_CELL;
    public ParticipantList PARTICIPANT_LIST { get; private set; }

    // equally distant colors H with same S and V 
    public Color[] prosthesisColors;
    public int delaySamples = 0;
    public float delayInSeconds = 0;
    public SinParameters sinParameters = new SinParameters();
    public PastaBoxState pastaBoxState;
    public float minDelayPhases = 1.0f;
    public GameObject targetMov0;
    public GameObject targetMov1;
    public GameObject targetMov2;
    public GameObject home;
    public GameObject PastaBoxObj;
    public SpriteRenderer[] pbSpriteRenderer;
    public GameObject ShadowHand;
    public GameObject MocapTracker;
    public GameObject PastaBox_table2;
    public GameObject PastaBox_table11_R;
    public GameObject PastaBox_table11_L;

    public TextMeshProUGUI text_DistanceFrom;
    public TextMeshProUGUI text_DistanceTo;

    public MjFreeJoint PastaBoxFreeJoint;

    public Material[] sh_materials; // these are the materials being changed
    public Color[] sh_Colors; // these are the colors the hand should have this round
    public float invalidTransparency = 0.1f;

    public Material pb_material;
    public Sprite[] pastabox_sprites;
    #endregion

    #region private

    // private
    private float targetDistance;
    private float startDistance;
    private float nextPhaseSwitch = 0;

    private Color pink = new Color(0.9339623f, 0.5594963f, 0.8469427f);
    private Color yellow = new Color(0.9921569f, 1, 0.2078432f);
    private Color grey = new Color(0.6603774f, 0.6603774f, 0.6603774f);
    private Color pb_grey = new Color(0.2f, 0.2f, 0.2f);

    private Color pb_original = new Color(0.1294118f, 0.1686275f, 0.509804f);

    // event log 
    private string eventHeader = "time_stamp_s" + "," + "participant" + "," + "cell" + "," + "cell_name" + "," + "run" + "," + "mov" + "," + "phase" + "," + "event" + "," +  "value" + Environment.NewLine;

    // hand transperancy flag
    private bool handTransparent = false;

    #endregion

    #region UnityFunctions
    // Start is called before the first frame update
    void Start()
    {
        LoggingManager.CreateNewLog("StudyEvents", eventHeader, 5f);
        // InvokeRepeating("logWrapper", 8f, 5f);

        // load json
        string ps_str = Utils.LoadJsonFile("study");
        // get list with all info
        PARTICIPANT_LIST = JsonUtility.FromJson<ParticipantList>(ps_str);
    }

    // check if update is enough
    private void Update()
    {
        //change color of sh hand if invalid => bit overkill to do it every frame(=> ToDo: need to check if changed)
        if (!MocapTracker.GetComponent<FollowTracker>().HandFollowValid && !handTransparent)
        {
            ChangeShadowHandColor(sh_materials, sh_Colors, invalidTransparency);
            handTransparent = true;
        }
        // else if hand is valid
        else
        {
            // and hand is transparant => change back
            if (handTransparent)
            {
                ChangeShadowHandColor(sh_materials, sh_Colors);
                handTransparent = false;
            }
        }

        // START OF PASTABOX PART IN UPDATE FUNCTION
        if (!pastaBoxState.running) return;

        // Calculate current target and start distance
        if (PastaBoxObj != null && pastaBoxState.currentFrom != null && pastaBoxState.currentTo != null)
        {
            // calc - Substract half of PastaBox height to compensate for coordinate system lying in the middle of the box
            startDistance = Vector3.Distance(pastaBoxState.currentFrom.transform.position, PastaBoxObj.transform.position - PastaBoxObj.transform.rotation * new Vector3(0, 0.0889f, 0));
            targetDistance = Vector3.Distance(pastaBoxState.currentTo.transform.position, PastaBoxObj.transform.position - PastaBoxObj.transform.rotation * new Vector3(0, 0.0889f, 0));
            pastaBoxState.completionPerc = startDistance / (targetDistance + startDistance);

            // update UI
            this.text_DistanceFrom.text = "Distance from: " + Math.Round(startDistance, 3);
            this.text_DistanceTo.text = "Distance to: " + Math.Round(targetDistance, 3) + "   -    Complete %: " + Math.Round(startDistance / (targetDistance + startDistance), 3);
        }

        switch (pastaBoxState.currentPhase)
        {
            case PastaBoxPhases.None:
                // here we go after success or fail during Transport and release

                // change PastaBox color to grey / transparant
                ChangeMaterialColor(pb_material, pb_grey);
                foreach (SpriteRenderer sr in pbSpriteRenderer) sr.sprite = pastabox_sprites[1];

                // check if run is 20 and Mov is Mov2 => Cell is over
                if (pastaBoxState.currentMov == PastaBoxMovs.Mov2 && pastaBoxState.run == NUM_RUNS_PER_CELL)
                {
                    StopStudyCell();
                }

                // check if Home / pink was touched and switch to next / Relocate PastaBox
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "home").Count > 0)
                {
                    // give pasta box back its color
                    ChangeMaterialColor(pb_material, pb_original);
                    foreach (SpriteRenderer sr in pbSpriteRenderer) sr.sprite = pastabox_sprites[0];

                    // switch to next mov, if mov is none start with first mov
                    pastaBoxState.currentMov = NextMov(pastaBoxState.currentMov);
                    PrepareMove();

                    // Log event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.MovChange, pastaBoxState.currentMov.ToString());
                    // Relocate PastaBox at correct placement
                    StartCoroutine(MovePastaBox());

                    print("phase: Switching to Reach Phase...");
                    pastaBoxState.currentPhase = PastaBoxPhases.Reach;

                    // Log Event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.PhaseChange, pastaBoxState.currentPhase.ToString());

                    ChangeSpriteColor(home, grey);
                    ChangeSpriteColor(pastaBoxState.currentFrom, yellow);
                }
                break;

            case PastaBoxPhases.Reach:
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count > 0 && Time.time >= nextPhaseSwitch)
                {
                    // switch to next: Grasp
                    print("phase: Switching to Grasp Phase...");

                    pastaBoxState.currentPhase = PastaBoxPhases.Grasp;
                    // log event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.PhaseChange, pastaBoxState.currentPhase.ToString());

                    // ToDo check time
                    nextPhaseSwitch = Time.time + minDelayPhases;
                }
                break;

            case PastaBoxPhases.Grasp:
                // check switch to transport
                if (CollisionManager.FindCollisionByNames("Grasp_collider_box", pastaBoxState.currentFrom.name, contains: true).Count == 0 && Time.time >= nextPhaseSwitch)
                {
                    // switch to next: Transport
                    print("phase: Switching to Transport Phase...");
                    pastaBoxState.currentPhase = PastaBoxPhases.Transport;

                    // log event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.PhaseChange, pastaBoxState.currentPhase.ToString());

                    nextPhaseSwitch = Time.time + minDelayPhases;

                    // color target 
                    ChangeSpriteColor(pastaBoxState.currentFrom, grey);
                    ChangeSpriteColor(pastaBoxState.currentTo, yellow);
                    break;
                }

                // check if object was touched already 
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "ROI_collider_box").Count > 0)
                {
                    pastaBoxState.pbTouched = true;
                }

                // Check for fails (when Hand leaves Grasp_collider_box after initial contact e.g. the object falls over etc)
                if (pastaBoxState.pbTouched && CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    print("phase: FAIL - Object is not connected to hand anymore in Grasp phase");
                    pastaBoxState.succeeded = false;
                    // Log Event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"FAIL;reason:Object-Hand-Disconnect");
                    // Switch to phase none
                    pastaBoxState.currentPhase = PastaBoxPhases.None;
                    // log event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.PhaseChange, pastaBoxState.currentPhase.ToString());
                    // color target 
                    ChangeSpriteColor(pastaBoxState.currentFrom, grey);
                    ChangeSpriteColor(pastaBoxState.currentTo, grey);
                    ChangeSpriteColor(home, pink);
                    nextPhaseSwitch = Time.time + minDelayPhases;
                    break;

                }
                break;

            case PastaBoxPhases.Transport:
                // as soon as the box comes into close proximity of the target switch to Release
                if (CollisionManager.FindCollisionByNames("Grasp_collider_box", pastaBoxState.currentTo.name, contains: true).Count > 0 && Time.time >= nextPhaseSwitch)
                {
                    // switch to next: Release
                    print("phase: Switching to Release Phase...");
                    pastaBoxState.currentPhase = PastaBoxPhases.Release;

                    // log event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.PhaseChange, pastaBoxState.currentPhase.ToString());

                    nextPhaseSwitch = Time.time + minDelayPhases;

                    break;
                }

                // Check for fails
                // 1. If hand is not connected (check roi or grasp collidere) => Fail => Phase.None
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    print("phase: FAIL - Object is not connected to hand anymore in transport phase");
                    pastaBoxState.succeeded = false;
                    // Log Event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"FAIL;reason:Object-Hand-Disconnect");

                    // Switch to phase none
                    pastaBoxState.currentPhase = PastaBoxPhases.None;

                    // log event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.PhaseChange, pastaBoxState.currentPhase.ToString());

                    ChangeSpriteColor(pastaBoxState.currentTo, grey);
                    ChangeSpriteColor(home, pink);

                    nextPhaseSwitch = Time.time + minDelayPhases;
                    break;
                }

                // 2. Any touch of other objects with the box except the hand and target (or table and target) => Check for success => Phase.None
                var _collisionsTableROI = CollisionManager.FindCollisionByNames("Table", "ROI_collider_box", contains: true);
                if (_collisionsTableROI.Count > 0)
                {
                    print("phase: MISTAKE - Object touched table in transport phase");
                    // Log Event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"MISTAKE;reason:Object-Environment-Touch");
                }

                var _collisionsTableHand = CollisionManager.FindCollisionByNames("Table", "ROI_collider_box", contains: true);
                if (_collisionsTableHand.Count > 0)
                {
                    print("phase: MISTAKE - Hand touched table in transport phase");
                    // Log Event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"MISTAKE;reason:Hand-Environment-Touch");
                }

                // Activate failures
                foreach (FailRun failRun in pastaBoxState.failRuns)
                {
                    // hacky
                    if (pastaBoxState.run == failRun.run && (int)pastaBoxState.currentMov == failRun.mov + 1 && pastaBoxState.completionPerc >= failRun.perc && !failRun.started)
                    {
                        failRun.started = true;
                        StartCoroutine(FailSinSimple());
                    }
                }

                break;

            case PastaBoxPhases.Release:

                // letting go of object
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0 && Time.time >= nextPhaseSwitch)
                {
                    // calculate orientation of pastabox (is already logged through PB, but evaluation is easier this way)
                    float angle = Vector3.Angle(PastaBoxObj.transform.up, Vector3.up);
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"pastaBox angle is: {angle}");

                    // interpret angle for log
                    string angleInterpret = InterpretAngle(angle);

                    // check if it was success, otherwise failure // Success: Pasta Box touches correct target, but not boundaries, stands 
                    if (CollisionManager.FindCollisionByNames(pastaBoxState.currentTo.name, "ROI_collider_box").Count > 0)
                    {
                        if (CollisionManager.FindCollisionByNames("boundary", "ROI_collider_box").Count == 0)
                        {
                            pastaBoxState.succeeded = true;
                            print("phase: SUCCESS - Object touches target and not boundary");
                            // Log Event
                            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"SUCCESS;reason: NaN;ori:{angleInterpret}");
                        }
                        // does it also touch the boundaries? => light FAIL => to be decided
                        else
                        {
                            pastaBoxState.succeeded = false;
                            print("phase: FAIL - Object touches target and (at least) boundary");
                            // Log Event
                            AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"FAIL;reason:Object-Boundary-Touch;ori:{angleInterpret}");
                        }
                    }
                    // is let go but does not touch target
                    else
                    {
                        pastaBoxState.succeeded = false;
                        print("phase: FAIL - Object does not even touch target");
                        // Log Event
                        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"FAIL;reason:!Object-Target-Touch;ori:{angleInterpret}");
                    }

                    // switch to next: None
                    print("phase: Switching to None Phase...");
                    pastaBoxState.currentPhase = PastaBoxPhases.None;
                    nextPhaseSwitch = Time.time + minDelayPhases;

                    // log event
                    AddToEventBuffer(StreamlinedInputManager.Now, Events.PhaseChange, pastaBoxState.currentPhase.ToString());

                    ChangeSpriteColor(pastaBoxState.currentTo, grey);

                    ChangeSpriteColor(home, pink);
                }
                break;

            default:
                break;
        }
    }

    private void OnApplicationQuit()
    {
        //// log event
        //AddToEventBuffer(StreamlinedInputManager.Now, Events.GameEvent, "Application Quit");
        //// log a final time (out of cycle)
        //logWrapper();
    }
    #endregion

    #region publicFuntions
    public void toggleObject(GameObject GO)
    {
        if (GO.activeSelf) activateObject(GO, false);
        else activateObject(GO, true);
    }

    public void activateObject(GameObject GO, bool activate)
    {
        GO.SetActive(activate);
    }

    public void AddToEventBuffer(double now, Events ev, string value)
    {
        //"time_stamp(ms)" + "," + "participant" + "," + "cell" + "," + "run" + "," + "mov" + "," + "phase" + "," + "event" + "," +  "value" + Environment.NewLine;
        string addBuffer = $"{now},{pastaBoxState.participant_id},{pastaBoxState.cell},{pastaBoxState.cell_name},{pastaBoxState.run},{pastaBoxState.currentMov},{pastaBoxState.currentPhase},{ev.ToString()},{value}{Environment.NewLine}";
        LoggingManager.AddToBuffer("StudyEvents", addBuffer);
    }
    #endregion

    #region privateFunctions
    private PastaBoxMovs NextMov(PastaBoxMovs currentMov)
    {
        int i = Array.IndexOf(pastaBoxState.movsOrder, currentMov);

        // reset pb_touched for next move
        pastaBoxState.pbTouched = false;

        // not found => means we are currently in None (only as a start)
        if (i == -1)
        {
            // increment run id
            pastaBoxState.run++;
            return PastaBoxMovs.Mov0;
        }

        // if not none go to next move
        i++;
        if (i < pastaBoxState.movsOrder.Length)
        {
            return pastaBoxState.movsOrder[i];
        }

        // if round is done go back to Mov0
        else
        {
            // increment run id
            pastaBoxState.run++;
            return PastaBoxMovs.Mov0;
        }
    }

    private void PrepareMove()
    {
        // select new "to" and "from"
        switch (pastaBoxState.currentMov)
        {
            case PastaBoxMovs.None:
                break;

            case PastaBoxMovs.Mov0:
                pastaBoxState.currentTo = targetMov0;
                pastaBoxState.currentFrom = targetMov2;
                break;

            case PastaBoxMovs.Mov1:
                pastaBoxState.currentTo = targetMov1;
                pastaBoxState.currentFrom = targetMov0;
                break;

            case PastaBoxMovs.Mov2:
                pastaBoxState.currentTo = targetMov2;
                pastaBoxState.currentFrom = targetMov1;
                break;

            default:
                break;
        }
    }

    private IEnumerator MovePastaBox()
    {
        // load new pasta box at the correct place
        // select new "to" and "from"

        switch (pastaBoxState.currentMov)
        {
            case PastaBoxMovs.None:
                break;

            case PastaBoxMovs.Mov0:
                //newPB = Instantiate(PastaBox_table2, position: PastaBox_table2.transform.position, PastaBox_table2.transform.rotation);
                MjState.TeleportMjRoot(PastaBoxFreeJoint, PastaBox_table2.transform.position, PastaBox_table2.transform.rotation);
                break;

            case PastaBoxMovs.Mov1:
                //newPB = Instantiate(PastaBox_table11_R, position: PastaBox_table11_R.transform.position, PastaBox_table11_R.transform.rotation);
                MjState.TeleportMjRoot(PastaBoxFreeJoint, PastaBox_table11_R.transform.position, PastaBox_table11_R.transform.rotation);

                break;

            case PastaBoxMovs.Mov2:
                //newPB = Instantiate(PastaBox_table11_L, position: PastaBox_table11_L.transform.position, PastaBox_table11_L.transform.rotation);
                MjState.TeleportMjRoot(PastaBoxFreeJoint, PastaBox_table11_L.transform.position, PastaBox_table11_L.transform.rotation);
                break;

            default:
                break;
        }

        yield return null;
    }
    #endregion

    #region UI
    public void BtnFailure()
    {
        // invoke coroutine
        StartCoroutine(FailSinSimple());
    }

    public void TglDelay()
    {
        this.pastaBoxState.delay = !this.pastaBoxState.delay;
        if (this.pastaBoxState.delay == true)
        {
            HandController.GetComponent<HandController>().SetDelay(delaySamples, delayInSeconds);
        }
        else
        {
            HandController.GetComponent<HandController>().ResetDelay();
        }
    }

    public void BtnStartStudyCell()
    {
        if (pastaBoxState.running)
        {
            StopStudyCell();
            return;
        }
        // change button color
        GameObject goBtn = GameObject.Find("StartStudyCell");
        goBtn.GetComponent<Image>().color = Color.red;

        // disable eye circle
        DisableEyeCircle();

        // Initialize info for participants (in case none get set)
        int _participant = -1;
        int _cell_id = -1;
        bool _delay = false;
        string _cell_name = "-1";
        Failure _failure = Failure.off;
        Color _color = Color.white;
        FailRun[] _failRuns = null;

        // Get info from loaded participant list
        foreach  (Participant p in PARTICIPANT_LIST.participants)
        {
            if (p.id == SET_PARTICIPANT)
            {
                foreach (Cell c in p.cells)
                {
                    if (c.cell_id == SET_CELL)
                    {
                        // Parse the string and set the Failure value
                        _failure = (Failure)Enum.Parse(typeof(Failure), c.failure);

                        _participant = p.id;
                        _cell_id = c.cell_id;
                        _cell_name = c.cell_name;
                        _delay = c.delay;
                        _color = prosthesisColors[c.color_id];

                        _failRuns = c.failrun.ToArray();
                    }
                }
            }
        }

        // Change prosthesis color for this cell
        sh_Colors[0] = _color;

        // set prosthesis colors
        ChangeShadowHandColor(sh_materials, sh_Colors);

        // start new cell, by setting a new object pastaBoxState; SET_RUN -1 as it will increment on Home Press
        pastaBoxState = new PastaBoxState(participant:_participant, cell: _cell_id, cell_name:_cell_name, run:SET_RUN-1, failure:_failure, delay: _delay, color: _color, failRuns: _failRuns);
        
        // set delay
        if (pastaBoxState.delay == true)
        {
            HandController.GetComponent<HandController>().SetDelay(delaySamples, delayInSeconds);
        }
        else
        {
            HandController.GetComponent<HandController>().ResetDelay();
        }

        pastaBoxState.running = true;

        // log event
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Start");

        // set all to grey
        ChangeSpriteColor(home, pink);
        ChangeSpriteColor(targetMov0, grey);
        ChangeSpriteColor(targetMov1, grey);
        ChangeSpriteColor(targetMov2, grey);
    }

    /// <summary>
    /// To be called when all runs of that cell have finished
    /// </summary>
    private void StopStudyCell()
    {
        // stop pasta box running
        pastaBoxState.running = false;
        // log event
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, $"Stop");

        // set all to grey
        ChangeSpriteColor(home, pink);
        ChangeSpriteColor(targetMov0, yellow);
        ChangeSpriteColor(targetMov1, yellow);
        ChangeSpriteColor(targetMov2, yellow);

        // change button back color
        GameObject goBtn = GameObject.Find("StartStudyCell");
        goBtn.GetComponent<Image>().color = new Color(r: 0.7877358f, g: 1, b:0.8046386f);

    }

    /// <summary>
    /// Function to start benchmark eye measurements for a certain number of seconds (120)
    /// </summary>
    public void StartBenchmark()
    {
        // in case it is already running quit it.
        if (pastaBoxState.benchmarkRunning)
        {
            StopBenchmark();
            return;
        }

        // start flag
        pastaBoxState.benchmarkRunning = true;
        // disable eye circle
        DisableEyeCircle();

        // change button color
        GameObject go_SB = GameObject.Find("StartBenchmark");
        go_SB.GetComponent<Image>().color = Color.red;

        // log starting time
        print("Starting Benchmark");
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, "Benchmark started");

        // Start Coroutine to Stop Benchmark
        StartCoroutine(StopBenchmark_Co(120));
    }

    /// <summary>
    /// Coroutine to stop Benchmark eye time
    /// </summary>
    /// <param name="time">Number of seconds to stop coroutine</param>
    /// <returns></returns>
    private IEnumerator StopBenchmark_Co(float time)
    {
        // wait for number of seconds
        yield return new WaitForSeconds(time);
        StopBenchmark();
    }

    private void StopBenchmark()
    {
        // log stopping time and event
        print("Stopping Benchmark");
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, "Benchmark stopped");
        pastaBoxState.benchmarkRunning = false;

        // change button color
        GameObject go_SB = GameObject.Find("StartBenchmark");
        go_SB.GetComponent<Image>().color = Color.white;
    }

    private void DisableEyeCircle()
    {
        // Disable eye circle
        GameObject go_eyeCircle = GameObject.Find("EyeCircle");
        if (go_eyeCircle != null && go_eyeCircle.activeSelf)
        {
            go_eyeCircle.SetActive(false);
        }
    }

    public void LoadScene()
    {
        SceneManager.LoadScene("PastaBox");
    }
    #endregion

    #region failure/delay funcs
    private IEnumerator FailSinSimple()
    {
        Debug.Log("Starting Fail...");
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, "Failure Start");
        // use handcontroller to overvwrite HOC with zero (this disables EMG as well)
        float[] sinSignal = CreateRandomSin(sinParameters.samplingFreq, sinParameters.duration, sinParameters.sinFreq_min, sinParameters.sinFreq_max, sinParameters.amplitude_min, sinParameters.amplitude_max);

        float startTime = Time.time;


        int i = 0;
        while (Time.time - startTime < sinParameters.duration)
        {
            // get the actual value to combine it with sinus
            DOA_mj[] curr_doaMj = HandController.GetComponent<HandController>().DOA_mujoco;
            curr_doaMj = HandController.GetComponent<HandController>().GetUdpValues(curr_doaMj, delaySamples, delayInSeconds, ignoreEMGState:true);

            // this would be the current value
            float currValHOC = curr_doaMj[0].General.current_value;

            // check the passed time ratio to weight the contribution signal (sinus is just added on top)
            float passedTimeRatio = (Time.time - startTime) / sinParameters.duration;

            float setVal = 0;
            // if lerping to sinus
            if (passedTimeRatio <= sinParameters.startRampRatio)
            {
                setVal = sinSignal[i] +  (1 - (passedTimeRatio / sinParameters.startRampRatio)) * currValHOC;
            }
            // if lerping from signal
            else if (passedTimeRatio >= 1 - sinParameters.endRampRatio)
            {
                setVal = sinSignal[i] + (passedTimeRatio - (1 - sinParameters.endRampRatio))*(1/sinParameters.endRampRatio) * currValHOC;
            }
            // just sinus
            else
            {
                setVal = sinSignal[i];
            }

            HandController.GetComponent<HandController>().OverwriteCurrVal(DOA.HOC, setVal);

            yield return new WaitForSeconds(1 / sinParameters.samplingFreq); // should roughly fit, but as fixed update not exact => check again with passed time

            i++;
            if (i >= sinSignal.Length) i = 0;
        }

        // Activate EMG control again
        HandController.GetComponent<HandController>().ActivateEMGOverwrite(DOA.HOC);
        AddToEventBuffer(StreamlinedInputManager.Now, Events.StudyEvent, "Failure Ended");
    }

    private float[] CreateRandomSin(float fs, float duration, float sinFreq_min, float sinFreq_max, float amplitude_min, float amplitude_max, float phase_min = 0, float phase_max = (float)(2*Math.PI))
    {
        float sinFreq = UnityEngine.Random.Range(sinFreq_min, sinFreq_max);
        float amplitude= UnityEngine.Random.Range(amplitude_min, amplitude_max);
        float phase = UnityEngine.Random.Range(phase_min, phase_max);
        
        float[] signal = CreateSinSignal(fs, duration, sinFreq, amplitude, phase);

        return signal;
    }

    private float[] CreateSinSignal(float fs, float duration, float sinFreq, float amplitude, float phase=0.0f)
    {
        // create empty array of correct length
        float[] signal = new float[(int)(fs*duration)];

        for (int i = 0; i < signal.Length; i++)
        {
            double time = i / fs;
            signal[i] = (float)(amplitude * Math.Sin(2 * Math.PI * sinFreq * time + phase));
        }

        return signal;
    }
    #endregion

    #region staticFunctions
    static void ChangeSpriteColor(GameObject obj, Color color, float a = -1)
    {
        Color _color = color;

        // if we want to overwrite transperancy of color
        if (a != -1) _color.a = a;

        SpriteRenderer spriteRenderer = null;

        obj.TryGetComponent<SpriteRenderer>(out spriteRenderer);

        if (spriteRenderer != null)
        {
            spriteRenderer.color = color;
        }
    }

    static void ChangeShadowHandColor(Material[] sh_materials, Color[] sh_Colors, float alpha = 1f)
    {
        for (int i = 0; i < sh_materials.Length; i++)
        {
            float[] rgb = new float[3] { sh_Colors[i].r, sh_Colors[i].g, sh_Colors[i].b };
            ChangeMaterialColor(sh_materials[i], rgb, alpha);
        }
    }

    static void ChangeMaterialColor(Material matToChange, float[] rgb = null, float alpha = -1f)
    {
        UnityEngine.Color newColor = matToChange.GetColor("_Color");

        // change rgb? (if not null)
        if (rgb != null)
        {
            if (rgb.Length != 3) throw new Exception("rgb must have 3 values");

            newColor.r = rgb[0]; newColor.g = rgb[1]; newColor.b = rgb[2];
        }

        // change a? (if not -1f)
        if (alpha != -1f) newColor.a = alpha;
        matToChange.SetColor("_Color", newColor);
    }
    static void ChangeMaterialColor(Material matToChange, Color color)
    {
        matToChange.SetColor("_Color", color);
    }

    /// <summary>
    /// Interpret the given angle between to vectors as pastabox orientation up, sideways, upside down
    /// </summary>
    /// <param name="angle"></param>
    /// <returns>a string for the log file</returns>
    static string InterpretAngle(float angle)
    {
        string res;
        if (angle <= 10)
        {
            res = "up";
        }
        else if (angle >= 80 && angle <= 100)
        {
            res = "sideways";
        }
        else if (angle >= 170)
        {
            res = "upsideDown";
        }
        else
        {
            res = "N/A";
        }

        return res;
    }
    #endregion
}
