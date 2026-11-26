using System;
using System.Collections.Generic;
using System.Linq;
using Mujoco;
using Pave.Assessments;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates explicit, editable assessment scenes using the existing TAC rig.
/// Existing scenes, prefabs and HTC gaze references are never saved or modified.</summary>
public static class PaveAssessmentSceneBuilder
{
    const string SourceScene = "Assets/Scenes/HIL-IL_TAC_VR.unity";
    const string MaterialFolder = "Assets/Assessments/Materials";
    static readonly Color wood = new Color(0.64f, 0.46f, 0.28f);
    static readonly Color dark = new Color(0.16f, 0.19f, 0.23f);
    [MenuItem("Tools/PAVE/Assessments/Create assessment scenes")]
    static void CreateFromMenu() => BuildAll();
    public static string[] BuildAll(bool overwrite = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Leave Play Mode before creating scenes.");
        }

        foreach (string path in AssessmentOperatorPanel.ScenePaths)
        {
            if (!overwrite && AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            {
                throw new InvalidOperationException("Assessment scene already exists: " + path);
            }
        }

        System.IO.Directory.CreateDirectory("Assets/Scenes/Assessments");
        System.IO.Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();
        Scene previous = SceneManager.GetActiveScene();
        try
        {
            Build(AssessmentKind.TargetedBoxAndBlock, AssessmentOperatorPanel.ScenePaths[0]);
            Build(AssessmentKind.BoxAndBlock, AssessmentOperatorPanel.ScenePaths[1]);
            Build(AssessmentKind.ClothespinUp, AssessmentOperatorPanel.ScenePaths[2]);
            Build(AssessmentKind.ClothespinDown, AssessmentOperatorPanel.ScenePaths[3]);
            var builds = EditorBuildSettings.scenes.ToList();
            foreach (string path in AssessmentOperatorPanel.ScenePaths)
            {
                if (!builds.Any(scene => scene.path == path))
                {
                    builds.Add(new EditorBuildSettingsScene(path, true));
                }
            }

            EditorBuildSettings.scenes = builds.ToArray();
            AssetDatabase.SaveAssets();
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
        }

        return AssessmentOperatorPanel.ScenePaths;
    }

    static void Build(AssessmentKind kind, string destination)
    {
        // Clone the complete saved rig in one operation so cross-root references remap.
        // A preview scene preserves any unsaved edits in the user's open TAC scene.
        Scene preview = EditorSceneManager.OpenPreviewScene(SourceScene);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            GameObject template = new GameObject("Assessment template");
            SceneManager.MoveGameObjectToScene(template, preview);
            foreach (GameObject root in preview.GetRootGameObjects())
            {
                if (root != template)
                {
                    root.transform.SetParent(template.transform, true);
                }
            }

            GameObject copy = UnityEngine.Object.Instantiate(template);
            SceneManager.MoveGameObjectToScene(copy, scene);
            foreach (Transform child in copy.transform.Cast<Transform>().ToArray())
            {
                child.SetParent(null, true);
            }

            UnityEngine.Object.DestroyImmediate(copy);
            var keep = new HashSet<string>
            {
                "Global Settings",
                "SIM",
                "hannes_model_wrist829_HIL-IL_MG",
                "XR Origin (XR Rig)",
                "EventSystem",
                "Directional Light",
                "XR Interaction Manager",
                "SRanipal Eye Framework"
            };
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!keep.Contains(root.name))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            GameObject handRoot = scene.GetRootGameObjects().Single(root => root.name == "hannes_model_wrist829_HIL-IL_MG");
            HandController controller = handRoot.GetComponentsInChildren<HandController>(true).Single(component => component.enabled);
            FollowTracker follower = handRoot.GetComponentsInChildren<FollowTracker>(true).Single(component => component.enabled);
            // Retain native rig mechanics and active control/following, remove legacy task proxies.
            foreach (MonoBehaviour component in handRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(component is MjComponent) && !(component is MjMeshFilter) && component != controller && component != follower)
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }

            foreach (Collider component in handRoot.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }

            foreach (Rigidbody component in handRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }

            var sim = scene.GetRootGameObjects().Single(root => root.name == "SIM").GetComponent<StreamlinedInputManager>();
            UnityEngine.Object.DestroyImmediate(sim.GetComponent<PubSensorData>());
            UnityEngine.Object.DestroyImmediate(sim.GetComponent<SimUdpSender>()); // No task labels/rewards can be sent to a learner.
            sim.EnableLogging = true;
            sim.receiveUdp = true;
            sim.GetComponent<LoggingManager>().enabled = true;
            sim.GazeDebugObject = null;
            // Pose acquisition uses the tracked-camera parent. HTC's GazeCamera and
            // vendor head-local reference stay unchanged; generic OpenXR has its own origin.
            Camera trackedCamera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Single(camera => camera.gameObject.activeInHierarchy && camera.CompareTag("MainCamera"));
            sim.TrackingOrigin = trackedCamera.transform.parent;
            sim.TrackHead = sim.TrackLeftController = sim.TrackRightController = true;
            var elbow = sim.Trackers.Find(binding => binding.Role == PaveTracking.TrackerRole.TrackerRightElbow);
            if (elbow == null)
            {
                sim.Trackers.Add(new PaveTracking.TrackerBinding(PaveTracking.TrackerRole.TrackerRightElbow) { Enabled = true });
            }
            else
            {
                elbow.Enabled = true;
            }

            var genericGaze = sim.GetComponent<Sim.Tracking.SimOpenXrGazeSource>();
            if (genericGaze != null)
            {
                genericGaze.TrackingOrigin = sim.TrackingOrigin;
            }

            if (controller.CommandSource != null)
            {
                controller.CommandSource = null;
            }

            var host = new GameObject("Assessment");
            var session = host.AddComponent<AssessmentSession>();
            session.Kind = kind;
            controller.hand = handRoot.GetComponentsInChildren<Transform>().Single(bone => bone.name == "palm").gameObject;
            session.Hand = controller;
            session.HandFollower = follower;
            session.Fingertips = CreateFingertips(handRoot);
            var panel = host.AddComponent<AssessmentOperatorPanel>();
            panel.Session = session;
            var frame = new GameObject("Apparatus frame (meters)").transform;
            frame.position = new Vector3(0, 0.77f, 0.6f);
            session.ApparatusFrame = frame;
            Box("Table", frame, new Vector3(0, -0.035f, 0), new Vector3(1.1f, 0.05f, 0.7f), dark);
            Box("Floor", null, new Vector3(0, -0.025f, 0), new Vector3(5, 0.05f, 5), new Color(0.25f, 0.28f, 0.3f));
            if (session.IsPinTask)
            {
                BuildPins(session);
            }
            else
            {
                BuildBlocks(session);
            }

            Label("Instructions", frame, new Vector3(0, 0.48f, 0.08f), session.IsPinTask ? "ASSESSMENT\nMove all three clips to the other rail.\nBegin on the operator's GO cue." : kind == AssessmentKind.TargetedBoxAndBlock ? "ASSESSMENT\nMove blocks 1-9 to matching targets.\nBegin on the operator's GO cue." : "ASSESSMENT\nMove one block at a time over the divider.\nBegin on the operator's GO cue.", 0.008f, Quaternion.identity);
            if (!EditorSceneManager.SaveScene(scene, destination))
            {
                throw new InvalidOperationException("Cannot save " + destination);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    static Transform[] CreateFingertips(GameObject hand)
    {
        var result = new List<Transform>();
        foreach (string bone in new[]
        {
            "index_top",
            "middle_top",
            "ring_top",
            "little_top",
            "thumb_base"
        }

        )
        {
            Transform body = hand.GetComponentsInChildren<Transform>().Single(item => item.name == bone);
            // Use the farthest surface vertex from this distal body's hinge as an explicit marker.
            // Markers are saved in the scene and must be checked against the reference hand before a study.
            Vector3 tip = Vector3.zero;
            foreach (MjGeom geom in body.GetComponentsInChildren<MjGeom>())
            {
                if (geom.ShapeType != MjShapeComponent.ShapeTypes.Mesh || geom.Mesh.Mesh == null)
                {
                    continue;
                }

                foreach (Vector3 vertex in geom.Mesh.Mesh.vertices)
                {
                    Vector3 point = body.InverseTransformPoint(geom.transform.TransformPoint(vertex));
                    if (point.sqrMagnitude > tip.sqrMagnitude)
                    {
                        tip = point;
                    }
                }
            }

            var marker = new GameObject("Assessment fingertip " + bone).transform;
            marker.SetParent(body, false);
            marker.localPosition = tip;
            result.Add(marker);
        }

        return result.ToArray();
    }

    static void BuildBlocks(AssessmentSession session)
    {
        bool targeted = session.Kind == AssessmentKind.TargetedBoxAndBlock;
        Transform frame = session.ApparatusFrame;
        float depth = targeted ? 0.172f : 0.254f;
        session.BoxDepthMeters = depth;
        const float width = 0.537f, wall = 0.01f;
        MjGeom source = Box("Source base", frame, new Vector3(width / 4, -0.005f, 0), new Vector3(width / 2, 0.01f, depth), wood);
        MjGeom target = Box("Destination base", frame, new Vector3(-width / 4, -0.005f, 0), new Vector3(width / 2, 0.01f, depth), wood);
        session.SourceSupports = new[]
        {
            source
        };
        session.TargetSupports = new[]
        {
            target
        };
        Box("Partition 152 mm", frame, new Vector3(0, 0.076f, 0), new Vector3(wall, 0.152f, depth), wood);
        foreach (float side in new[]
        {
            -1f,
            1f
        }

        )
        {
            Box("Side wall", frame, new Vector3(side * (width / 2 + wall / 2), 0.0425f, 0), new Vector3(wall, 0.085f, depth + wall * 2), wood);
            Box("Front rear wall", frame, new Vector3(0, 0.0425f, side * (depth / 2 + wall / 2)), new Vector3(width, 0.085f, wall), wood);
        }

        var objects = new List<AssessmentObject>();
        var random = new System.Random(417);
        int count = targeted ? 9 : 150;
        for (int index = 0; index < count; index++)
        {
            Vector3 position = targeted ? new Vector3(0.045f + (index % 3) * 0.08f, 0.0126f, -0.052f + (index / 3) * 0.052f) : new Vector3(0.025f + (index % 8) * 0.03f, 0.0126f + (index / 56) * 0.0253f, -0.105f + ((index / 8) % 7) * 0.035f);
            var body = NewBody("Block " + (index + 1).ToString("D3"), frame, position);
            if (!targeted)
            {
                body.transform.localRotation = Quaternion.Euler(0, (float)(random.NextDouble() * 10 - 5), 0);
            }

            MjGeom geom = Box("Physical cube", body.transform, Vector3.zero, Vector3.one * 0.025f, new Color(0.25f + index % 3 * 0.22f, 0.43f, 0.7f - index % 3 * 0.15f), 0.01f);
            AssessmentObject prop = AddProp(body, index, new[] { geom });
            if (targeted)
            {
                var destination = new GameObject("Target " + (index + 1)).transform;
                destination.SetParent(frame, false);
                destination.localPosition = new Vector3(-position.x, 0, position.z);
                prop.Target = destination;
                TargetFrame(frame, new Vector3(position.x, 0, position.z));
                TargetFrame(frame, destination.localPosition);
                Label("Source number", frame, new Vector3(position.x, 0.008f, position.z - 0.024f), (index + 1).ToString(), 0.004f, Quaternion.Euler(90, 0, 0));
                Label("Target number", frame, destination.localPosition + new Vector3(0, 0.008f, -0.024f), (index + 1).ToString(), 0.004f, Quaternion.Euler(90, 0, 0));
            }

            objects.Add(prop);
        }

        session.Objects = objects.ToArray();
    }

    static void TargetFrame(Transform frame, Vector3 center)
    {
        // Prototype insert aperture: 36 mm square, 6.35 mm high. Logged as a virtual adaptation.
        foreach (float side in new[]
        {
            -1f,
            1f
        }

        )
        {
            Box("Insert x edge", frame, center + new Vector3(side * 0.0195f, 0.003175f, 0), new Vector3(0.003f, 0.00635f, 0.042f), dark);
            Box("Insert z edge", frame, center + new Vector3(0, 0.003175f, side * 0.0195f), new Vector3(0.036f, 0.00635f, 0.003f), dark);
        }
    }

    static void BuildPins(AssessmentSession session)
    {
        Transform frame = session.ApparatusFrame;
        MjGeom horizontal = Cylinder("Horizontal rail", frame, new Vector3(-0.1f, 0.24f, 0), 0.004f, 0.15f, Quaternion.Euler(90, 0, 0));
        MjGeom vertical = Cylinder("Vertical rail", frame, new Vector3(0.19f, 0.35f, 0.02f), 0.004f, 0.23f, Quaternion.identity);
        Box("Apparatus base", frame, new Vector3(0.02f, 0.012f, 0), new Vector3(0.55f, 0.024f, 0.4f), dark);
        session.SourceSupports = new[]
        {
            session.Kind == AssessmentKind.ClothespinUp ? horizontal : vertical
        };
        session.TargetSupports = new[]
        {
            session.Kind == AssessmentKind.ClothespinUp ? vertical : horizontal
        };
        var objects = new List<AssessmentObject>();
        for (int index = 0; index < 3; index++)
        {
            bool up = session.Kind == AssessmentKind.ClothespinUp;
            Vector3 position = up ? new Vector3(-0.1f, 0.21f, (index - 1) * 0.09f) : new Vector3(0.22f, 0.21f + index * 0.14f, 0.02f);
            MjBody root = NewBody("Clothespin " + (index + 1), frame, position);
            root.transform.localRotation = up ? Quaternion.Euler(0, 0, -90) : Quaternion.Euler(90, 0, 0);
            MjGeom[] lower = PinJaw("Lower", root.transform, -1);
            var upperObject = new GameObject("Upper jaw body");
            upperObject.transform.SetParent(root.transform, false);
            upperObject.AddComponent<MjBody>();
            var jointObject = new GameObject("Passive spring hinge");
            jointObject.transform.SetParent(upperObject.transform, false);
            // MuJoCo joint signs follow its right-handed frame: positive opens these jaws.
            // The spring closes against the rail before the lower angular stop is reached.
            jointObject.transform.localRotation = Quaternion.FromToRotation(Vector3.right, Vector3.forward);
            MjHingeJoint hinge = jointObject.AddComponent<MjHingeJoint>();
            hinge.RangeLower = -15;
            hinge.RangeUpper = 45;
            hinge.Configuration = 0;
            hinge.Settings.Solver.Limited = true;
            hinge.Settings.Spring.Stiffness = 0.3f;
            hinge.Settings.Spring.Damping = 0.01f;
            hinge.Settings.Spring.EquilibriumPose = -6;
            MjGeom[] upper = PinJaw("Upper", upperObject.transform, 1);
            AssessmentObject prop = AddProp(root, index, lower.Concat(upper).ToArray());
            prop.Jaw = hinge;
            prop.InitialJawDegrees = 0;
            objects.Add(prop);
        }

        session.Objects = objects.ToArray();

        // Small spring clips need a finer step and firmer contacts than the block tasks.
        // Rolling resistance models a finite contact patch; there are no rail attachments.
        session.PhysicsStepSeconds = 0.002f;
        MjGlobalSettings settings = session.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MjGlobalSettings>()).Single();
        settings.GlobalOptions.Cone = FrictionConeType.elliptic;
        settings.GlobalOptions.ImpRatio = 10;
        settings.GlobalOptions.NoSlipIterations = 0;
        foreach (MjGeom geom in objects.SelectMany(prop => prop.Geoms).Concat(session.SourceSupports).Concat(session.TargetSupports))
        {
            geom.Settings.Solver.SolRef.TimeConst = 0.004f;
            geom.Settings.Solver.ConDim = 6;
            geom.Settings.Friction.Rolling = 0.003f;
        }
    }

    // Two sloped pads form each jaw's open V groove. Four real contact surfaces retain
    // the round rail when closed; opening the passive hinge leaves a physical exit path.
    // This remains a prototype geometry, not a calibrated Rolyan pin model.
    static MjGeom[] PinJaw(string name, Transform parent, float side)
    {
        Color red = new Color(0.8f, 0.12f, 0.1f);
        var geoms = new List<MjGeom>();
        geoms.Add(Box(name + " handle", parent, new Vector3(-0.006f, side * 0.010f, 0), new Vector3(0.075f, 0.006f, 0.012f), red, 0.004f));
        foreach (float direction in new[]
        {
            -1f,
            1f
        }

        )
        {
            MjGeom pad = Box(name + " contact pad", parent, new Vector3(-0.03f + direction * 0.00389f, side * 0.00389f, 0), new Vector3(0.008f, 0.003f, 0.012f), red, 0.002f);
            pad.transform.localRotation = Quaternion.Euler(0, 0, -side * direction * 45);
            geoms.Add(pad);
        }

        return geoms.ToArray();
    }

    static MjBody NewBody(string name, Transform parent, Vector3 localPosition)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;
        MjBody body = obj.AddComponent<MjBody>();
        var joint = new GameObject("Free joint");
        joint.transform.SetParent(obj.transform, false);
        joint.AddComponent<MjFreeJoint>();
        return body;
    }

    static AssessmentObject AddProp(MjBody body, int index, MjGeom[] geoms)
    {
        var prop = body.gameObject.AddComponent<AssessmentObject>();
        prop.Index = index;
        prop.Body = body;
        prop.FreeJoint = body.GetComponentInChildren<MjFreeJoint>();
        prop.Geoms = geoms;
        prop.InitialPosition = body.transform.position;
        prop.InitialRotation = body.transform.rotation;
        return prop;
    }

    static MjGeom Box(string name, Transform parent, Vector3 position, Vector3 size, Color color, float mass = 0)
    {
        MjGeom geom = Geom(name, parent, position, color, mass);
        geom.ShapeType = MjShapeComponent.ShapeTypes.Box;
        geom.Box.Extents = size * 0.5f;
        return geom;
    }

    static MjGeom Cylinder(string name, Transform parent, Vector3 position, float radius, float halfHeight, Quaternion rotation)
    {
        MjGeom geom = Geom(name, parent, position, dark, 0);
        geom.ShapeType = MjShapeComponent.ShapeTypes.Cylinder;
        geom.Cylinder.Radius = radius;
        geom.Cylinder.HalfHeight = halfHeight;
        geom.transform.localRotation = rotation;
        return geom;
    }

    static MjGeom Geom(string name, Transform parent, Vector3 position, Color color, float mass)
    {
        var obj = new GameObject(name);
        if (parent != null)
        {
            obj.transform.SetParent(parent, false);
        }

        obj.transform.localPosition = position;
        MjGeom geom = obj.AddComponent<MjGeom>();
        geom.Mass = mass;
        geom.Density = mass > 0 ? 0 : 1000;
        geom.Settings.Solver.ConDim = 4;
        obj.AddComponent<MeshFilter>();
        obj.AddComponent<MjMeshFilter>();
        obj.AddComponent<MeshRenderer>().sharedMaterial = Material(color);
        return geom;
    }

    static Material Material(Color color)
    {
        string path = MaterialFolder + "/" + ColorUtility.ToHtmlStringRGB(color) + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void Label(string name, Transform parent, Vector3 position, string text, float characterSize, Quaternion rotation)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localRotation = rotation;
        var label = obj.AddComponent<TextMesh>();
        label.text = text;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = characterSize;
        label.fontSize = 48;
        label.color = Color.white;
    }
}
