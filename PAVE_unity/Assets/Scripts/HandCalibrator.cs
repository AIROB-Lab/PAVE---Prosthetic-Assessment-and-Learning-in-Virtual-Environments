using System;
using System.Collections;
using UnityEngine;

public class HandCalibrator : MonoBehaviour
{
    [Tooltip("Drag SceneManager here (needed for the DoF check and to know LimbPos / Interact)")]
    public HIL_Manager hil;

    [Header("Real (Hannes) hand")]
    [Tooltip("Real hand -> MocapTracker -> shifter (between tracker and hand)")]
    public Transform handAnchor;
    [Tooltip("Real hand -> hannes_forearm")]
    public Transform realForearm;
    [Tooltip("Real hand -> hannes_forearm -> CylinderArrow -> 3D RightArrow (optional)")]
    public Transform realArrow;

    [Header("Ghost for LimbPos (ghst_hannes_model_wrist829)")]
    [Tooltip("Ghost -> MocapTracker -> shifter (rough first alignment)")]
    public Transform limbPosGhostShifter;
    [Tooltip("Ghost -> hannes_forearm")]
    public Transform limbPosGhostForearm;
    [Tooltip("Ghost -> hannes_forearm -> CylinderArrow -> 3D RightArrow (optional)")]
    public Transform limbPosGhostArrow;

    [Header("Ghost for Interact (ghst_hannes_model_wrist829_with_box_HIL-IL)")]
    [Tooltip("Ghost with box -> hannes_forearm")]
    public Transform interactGhostForearm;
    [Tooltip("Ghost with box -> hannes_forearm -> CylinderArrow -> 3D RightArrow (optional)")]
    public Transform interactGhostArrow;

    [Header("Settings")]
    [Tooltip("Same tolerance as the green bars")]
    public float dofTolerance = 0.2f;
    [Tooltip("Correction rounds (each waits for the hand to follow)")]
    public int refineSteps = 3;

    [Header("Reuse a calibration (e.g. from LimbPos in Interact)")]
    [Tooltip("Save the offset after each calibration")]
    public bool saveAfterCalibrate = true;
    [Tooltip("Load the last saved offset when Play starts")]
    public bool loadOnStart = true;
    [Tooltip("Only load a saved calibration younger than this (hours), so an old participant's one is never used")]
    public float maxAgeHours = 3f;

    const string PrefKey = "HandCalibrator.saved";

    [Serializable]
    class SavedCalibration
    {
        public Vector3 shifterLocalPos;
        public Quaternion shifterLocalRot;
        public bool hasArrow;
        public Quaternion arrowLocalRot;
        public string savedAt;          // ISO time
        public string scene;            // where it was made (LimbPos / Interact)
    }

    Vector3 startLocalPos;
    Quaternion startLocalRot;
    Quaternion realArrowStartLocalRot;

    void Start()
    {
        // remember the original offsets, for Reset
        startLocalPos = handAnchor.localPosition;
        startLocalRot = handAnchor.localRotation;
        if (realArrow != null) realArrowStartLocalRot = realArrow.localRotation;

        if (loadOnStart) LoadCalibration();
    }

    // DEBUG: tells you if this object/script gets switched off (that silently stops the calibration)
    void OnDisable()
    {
        Debug.Log($"[Calibrate] STOPPED: object '{gameObject.name}' or the script was switched off");
    }

    // Button: participant matches the ghost (arm pose + all DoF bars green), then press
    public void BtnCalibrate()
    {
        // DEBUG
        Debug.Log($"[Calibrate] clicked - timeScale {Time.timeScale}, object '{gameObject.name}' active {gameObject.activeInHierarchy}, script enabled {enabled}");

        // all DoFs must match the ghost (bars green) before calibrating
        if (hil != null && hil.currentStats != null)
        {
            foreach (var d in hil.currentStats.activeDiffDOAs.Values)
            {
                if (Mathf.Abs(d.diff) > dofTolerance)
                {
                    Debug.Log($"[Calibrate] not yet: {d.doa} is off by {d.diff:0.00} - match the ghost until all bars are green");
                    return;
                }
            }
        }

        StopAllCoroutines();
        StartCoroutine(Calibrate());
    }

    IEnumerator Calibrate()
    {
        // pick the ghost of the current scene
        bool interact = hil != null && hil.currentEnv == HIL_Manager.HIL_env.Interact;
        Transform ghostForearm = interact ? interactGhostForearm : limbPosGhostForearm;
        Transform ghostArrow = interact ? interactGhostArrow : limbPosGhostArrow;
        Transform ghostShifter = interact ? null : limbPosGhostShifter;   // Interact: no usable shifter
        string sceneName = interact ? "Interact" : "LimbPos";
        Debug.Log($"[Calibrate] scene: {sceneName}");

        // DEBUG: show what each field points to
        Debug.Log($"[Calibrate] fields: handAnchor={Name(handAnchor)}, realForearm={Name(realForearm)}, " +
                  $"ghostForearm={Name(ghostForearm)}, ghostShifter={Name(ghostShifter)}, " +
                  $"realArrow={Name(realArrow)}, ghostArrow={Name(ghostArrow)}");

        if (realForearm == null || ghostForearm == null)
        {
            Debug.Log("[Calibrate] STOP: a forearm field is empty or Missing (see 'fields' line above)");
            yield break;
        }

        // 1) rough alignment (LimbPos only): real shifter onto the ghost shifter
        if (ghostShifter != null)
            handAnchor.SetPositionAndRotation(ghostShifter.position, ghostShifter.rotation);
        Debug.Log("[Calibrate] step 1 done (rough alignment)");

        // DEBUG
        Debug.Log("[Calibrate] waiting for the hand to follow (physics steps) ...");

        // 2) exact alignment on the forearms: remove the remaining rotation and position difference
        for (int step = 0; step < refineSteps; step++)
        {
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();   // let the hand follow

            Quaternion dRot = ghostForearm.rotation * Quaternion.Inverse(realForearm.rotation);
            handAnchor.rotation = dRot * handAnchor.rotation;                     // rotation difference -> 0
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            handAnchor.position += ghostForearm.position - realForearm.position;  // position difference -> 0

            Debug.Log($"[Calibrate] round {step + 1}: forearm was {Quaternion.Angle(Quaternion.identity, dRot):0.0} deg off");
        }
        for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

        // 3) align the arrow
        if (realArrow != null && ghostArrow != null)
        {
            realArrow.rotation = ghostArrow.rotation;
            Debug.Log("[Calibrate] arrow aligned to the ghost arrow - now fixed on the forearm");
        }

        // 4) result
        Debug.Log($"[Calibrate] shifter offset pos {handAnchor.localPosition}, rot {handAnchor.localEulerAngles}");
        Debug.Log("[Calibrate] " + RotDiff("forearm ", realForearm, ghostForearm));
        Debug.Log($"[Calibrate] forearm position difference: {Vector3.Distance(realForearm.position, ghostForearm.position) * 100:0.0} cm");
        if (hil != null && hil.currentStats != null)
            foreach (var d in hil.currentStats.activeDiffDOAs.Values)
                Debug.Log($"[Calibrate] {d.doa}: real {d.actual:0.00}, ghost {d.should:0.00}, diff {d.diff:0.00}");

        // 5) save it, so the next scene (e.g. Interact) can use it
        if (saveAfterCalibrate) SaveCalibration(sceneName);
        Debug.Log("[Calibrate] done");
    }

    void SaveCalibration(string sceneName)
    {
        var c = new SavedCalibration
        {
            shifterLocalPos = handAnchor.localPosition,
            shifterLocalRot = handAnchor.localRotation,
            hasArrow = realArrow != null,
            arrowLocalRot = realArrow != null ? realArrow.localRotation : Quaternion.identity,
            savedAt = DateTime.Now.ToString("o"),
            scene = sceneName
        };
        PlayerPrefs.SetString(PrefKey, JsonUtility.ToJson(c));
        PlayerPrefs.Save();
        Debug.Log($"[Calibrate] saved (from {sceneName}, {DateTime.Now:HH:mm})");
    }

    // Button (optional): load the last saved calibration again
    public void BtnLoadCalibration() => LoadCalibration();

    void LoadCalibration()
    {
        if (!PlayerPrefs.HasKey(PrefKey)) { Debug.Log("[Calibrate] no saved calibration - calibrate in LimbPos first"); return; }
        var c = JsonUtility.FromJson<SavedCalibration>(PlayerPrefs.GetString(PrefKey));
        if (!DateTime.TryParse(c.savedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t))
        {
            Debug.LogWarning("[Calibrate] saved calibration unreadable - calibrate again");
            return;
        }
        double ageH = (DateTime.Now - t).TotalHours;
        if (ageH > maxAgeHours)
        {
            Debug.LogWarning($"[Calibrate] saved calibration is {ageH:0.0} h old (from {t:dd.MM HH:mm}) - NOT loaded, calibrate again");
            return;
        }
        handAnchor.localPosition = c.shifterLocalPos;
        handAnchor.localRotation = c.shifterLocalRot;
        if (c.hasArrow && realArrow != null) realArrow.localRotation = c.arrowLocalRot;
        Debug.Log($"[Calibrate] loaded calibration from {c.scene} ({t:HH:mm}, {ageH * 60:0} min ago)");
    }

    // Button: back to the original offsets (old behaviour) and forget the saved calibration
    public void BtnResetCalibration()
    {
        StopAllCoroutines();
        handAnchor.localPosition = startLocalPos;
        handAnchor.localRotation = startLocalRot;
        if (realArrow != null) realArrow.localRotation = realArrowStartLocalRot;
        PlayerPrefs.DeleteKey(PrefKey);
        Debug.Log("[Calibrate] reset to original offsets, saved calibration deleted");
    }

    // rotation difference between two objects: total angle + per axis (in the ghost's own axes)
    static string RotDiff(string name, Transform real, Transform ghost)
    {
        if (real == null || ghost == null) return $"{name}: (not found)";
        Quaternion rel = Quaternion.Inverse(ghost.rotation) * real.rotation;
        Vector3 e = rel.eulerAngles;
        e = new Vector3(Mathf.DeltaAngle(0, e.x), Mathf.DeltaAngle(0, e.y), Mathf.DeltaAngle(0, e.z));
        return $"{name}: {Quaternion.Angle(real.rotation, ghost.rotation):0.0} deg  (x {e.x:0.0}, y {e.y:0.0}, z {e.z:0.0})";
    }

    // DEBUG: object name + parent, or EMPTY/MISSING
    static string Name(Transform t) => t == null ? "EMPTY/MISSING" : $"'{t.name}' (parent '{(t.parent ? t.parent.name : "-")}')";
}