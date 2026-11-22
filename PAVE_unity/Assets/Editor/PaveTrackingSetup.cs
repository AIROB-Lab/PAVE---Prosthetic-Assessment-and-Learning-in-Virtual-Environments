using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;
using ViveSR.anipal.Eye;

/// <summary>Explicit, repeatable setup for research acquisition. Never replaces pose drivers
/// or writes rig transforms. Use the menu after adding a new scene or changing its XR rig.</summary>
public static class PaveTrackingSetup
{
    public static readonly string[] ResearchScenes = {
        "Assets/Scenes/HIL-IL_TAC_VR.unity", "Assets/Scenes/HIL-IL_VR.unity", "Assets/Scenes/PastaBox.unity",
        "Assets/Scenes/Scenes_LR/Stage1.unity", "Assets/Scenes/Scenes_LR/Stage2.unity", "Assets/Scenes/Scenes_LR/Stage3.unity"
    };

    [MenuItem("Tools/PAVE/Configure research tracking in current scene")]
    public static void ConfigureCurrentScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PaveTracking>(true));
        foreach (PaveTracking tracking in components)
        {
            Camera camera = tracking.main_camera == null ? ResolveCamera(scene) : tracking.main_camera.GetComponent<Camera>();
            if (camera == null || camera.transform.parent == null)
                throw new InvalidOperationException("Assign a tracked main_camera and its tracking-space parent on " + tracking.name);
            Undo.RecordObject(tracking, "Configure research tracking");
            // Preserve the study's existing gaze camera binding. Pose recording uses a separate reference.
            tracking.trackingOrigin = camera.transform.parent;
            tracking.trackHead = tracking.trackLeftController = tracking.trackRightController = true;
            // Observe only roles already bound to scene pose drivers; never infer a study role from device order.
            foreach (PaveTracking.position role in BoundTrackerRoles(scene))
            {
                var binding = tracking.usedTrackers.Find(item => item.pos == role);
                if (binding == null) tracking.usedTrackers.Add(new PaveTracking.TrackerLog(role) { used = true });
                else binding.used = true;
            }
            // Generic OpenXR gaze has its own tracking-space reference. It never changes HTC's head-local reference.
            var openXr = tracking.GetComponent<SimOpenXrGazeSource>();
            if (openXr == null) openXr = Undo.AddComponent<SimOpenXrGazeSource>(tracking.gameObject);
            openXr.trackingOrigin = tracking.trackingOrigin;
            openXr.enabled = tracking.trackEyes;
            EditorUtility.SetDirty(openXr);
            // Preserve the existing SRanipal camera/calibration. Generic gaze is a separately identified fallback.
            if (tracking.trackEyes)
            {
                if (tracking.main_camera == null)
                    throw new InvalidOperationException("Preserve and assign the study's existing gaze camera before migration.");
                var vendor = tracking.GetComponent<PaveSranipalEyeSource>();
                if (vendor == null) vendor = Undo.AddComponent<PaveSranipalEyeSource>(tracking.gameObject);
                if (vendor.headset == null) vendor.headset = tracking.main_camera.transform;
                vendor.framework = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SRanipal_Eye_Framework>(true)).FirstOrDefault();
                vendor.enabled = true;
                tracking.gazeSource = vendor;
                tracking.fallbackGazeSource = openXr;
                EditorUtility.SetDirty(vendor);
            }
            else tracking.gazeSource = openXr;
            EditorUtility.SetDirty(tracking);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tracking);
        }
        foreach (var framework in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SRanipal_Eye_Framework>(true)))
        {
            // The provider owns startup after detecting compatible hardware. Meta/no-headset runs must not start HTC's SDK.
            framework.EnableEye = false;
            framework.enabled = false;
            EditorUtility.SetDirty(framework);
            PrefabUtility.RecordPrefabInstancePropertyModifications(framework);
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static Camera ResolveCamera(Scene scene)
    {
        Camera[] candidates = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
            .Where(camera => camera.gameObject.activeInHierarchy && camera.CompareTag("MainCamera") &&
                camera.GetComponents<MonoBehaviour>().Any(component => component != null && component.GetType().Name == "TrackedPoseDriver")).ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException("Expected one active tracked MainCamera in " + scene.path + "; found " + candidates.Length);
        return candidates[0];
    }

    static IEnumerable<PaveTracking.position> BoundTrackerRoles(Scene scene)
    {
        var roles = new HashSet<PaveTracking.position>();
        foreach (var component in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)))
        {
            if (component == null || component.GetType().Name != "TrackedPoseDriver") continue;
            var serialized = new SerializedObject(component);
            var reference = serialized.FindProperty("m_PositionInput.m_Reference")?.objectReferenceValue as InputActionReference;
            if (reference == null || reference.action == null) continue;
            foreach (var binding in reference.action.bindings)
            {
                foreach (PaveTracking.position role in Enum.GetValues(typeof(PaveTracking.position)))
                {
                    string usage = role.ToString().Substring("Tracker".Length);
                    string pathUsage = System.Text.RegularExpressions.Regex.Replace(usage, "([a-z])([A-Z])", "$1 $2");
                    if (binding.path != null && binding.path.Contains("<XRViveTracker>{" + pathUsage + "}")) roles.Add(role);
                }
            }
        }
        return roles;
    }

    [MenuItem("Tools/PAVE/Configure all existing research scenes")]
    public static void ConfigureResearchScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene changes before batch setup.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string path in ResearchScenes)
            {
                EditorSceneManager.OpenScene(path);
                ConfigureCurrentScene();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                Debug.Log("Configured read-only acquisition: " + path);
            }
            ConfigurePcProfiles();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    public static void ConfigurePcProfiles()
    {
        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        string[] names = { "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile", "MetaQuestTouchProControllerProfile",
            "HTCViveControllerProfile", "HTCViveTrackerProfile", "EyeGazeInteraction" };
        foreach (var feature in settings.GetFeatures())
        {
            if (feature == null || !names.Contains(feature.GetType().Name)) continue;
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
