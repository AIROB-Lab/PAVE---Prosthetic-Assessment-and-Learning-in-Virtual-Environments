# Research acquisition and scene movement

Unity's existing Tracked Pose Driver / XR Interaction Toolkit components own headset, controller and tracker movement. SIM observes devices; PAVE records samples and evaluates study-specific gaze regions. Acquisition never writes rig transforms. The optional gaze debug sphere is a visual aid only.

## Optional SIM module

`Dependencies/SIM/Packages/org.airob.sim.tracking` is a separate UPM package. The UDP package still has no Unity/XR dependency. The tracking package provides `SimXrPoseReader`, typed pose/gaze samples, `SimGazeSource`, and a generic OpenXR gaze provider. Its README describes coordinate frames, availability and timestamps. Generic acquisition changes belong upstream in SIM.

## Existing scene configuration

The six research scenes have explicit pose-recording origins and headset/controller acquisition enabled. Tracker roles are enabled only when the scene already contains a matching tracker action binding. Use **Tools > PAVE > Configure research tracking in current scene** when setting up a scene; the batch menu configures all six. Save open scene edits before the batch operation. The utility is explicit and repeatable; it never runs automatically at import.

A pose recording origin is the existing camera-offset transform of the XR rig, not a new calibration transform. Logs contain both local and world poses in meters, availability, and host acquisition time. Invalid measurements have empty numeric cells. Missing devices, failed reads and ambiguous roles are not treated as fresh poses.

## Gaze origins and provider choice

**HTC / SRanipal preserves the study's original camera binding and conversion.** The raw gaze origin remains headset-local millimeters, converted once to Unity meters with an X-axis sign change, then transformed by that same camera. No camera, calibration transform, or headset-local gaze offset was changed. The separate native adapter owns SDK startup/callback lifetime and only attempts startup for a supported HTC headset. Raw callback samples cross a bounded queue; Unity, CSV recording and ROI evaluation run on the main thread. Its detailed `Eye_Tracking.csv` retains the raw SDK coordinates, device timestamp and scientific fields.

Generic OpenXR gaze uses its own tracking-origin reference and is an explicitly identified fallback when no fresh SRanipal sample is available. Its frame is `tracking_origin`; SRanipal's is `head_local`. Combined OpenXR gaze does not provide separate per-eye validity: the UI reports n/a rather than inventing it. There is no head-direction substitute for eye measurements. Existing LR scenes keep eye acquisition disabled. Eye calibration for OpenXR is handled by the headset runtime; SRanipal calibration requires its active provider.

The Windows OpenXR settings enable Touch / Touch Plus / Touch Pro, Vive controllers, Vive trackers and Eye Gaze Interaction. This enables requested input routes, not a claim that every headset or runtime has those capabilities. Device software and eye permissions remain required. Android standalone remains outside this Windows MuJoCo baseline.

## Recording and verification

Pose CSV files now include availability and explicit local/world columns. `Gaze_Tracking.csv` includes provider, local frame and world ray; source changes are visible. ROI remains PAVE-owned. Host time is not a device-native sampling time, and world conversion uses the reference transform at host processing time. Do not assume external clock synchronization or prediction-corrected gaze.

Software verification on 2026-11-22:

- Unity 6 compilation passed with zero errors.
- Temporary checks passed pose conversion, finite-data validation, gaze freshness/disable handling, the original SRanipal origin/direction conversion, and unchanged observed transforms.
- Injecting 520 callback samples delivered 512 and reported 8 dropped samples, matching the queue limit; no native headset was used for that check.
- All six scene files retained all original transform blocks and existing gaze camera references.
- A TAC Play Mode run with zero connected XR devices reported unavailable gaze and no registered HTC callback. Four tracking CSV files each contained 4,097 data rows plus a header, with consistent column counts. XR startup was restored to enabled after the check.

Live Meta / HTC pose accuracy, tracker reconnection, eye calibration and hardware sampling require device validation. These software checks do not establish physical measurement accuracy. No new assessment scenes were added.

## Cleanup and migration checks

The follow-up cleanup standardizes public acquisition/provider names and uses FormerlySerializedAs for stored settings, including the original HTC camera, gaze cast radius and tracker roles. It replaces unused pose/eye wrapper buffers with typed samples, removes unused headers and empty callbacks, and formats the project-owned PAVE/student/SIM scripts. Public naming and migration conventions are in CODE_STYLE.md.

After migration, all six scenes loaded with zero missing scripts. The stored gaze camera, cast radius, tracker roles and provider references were checked in Unity before saving. Exactly one acquisition adapter is active per scene: the extra HIL object is inactive, and the redundant base components in Stage1/Stage3 are already disabled. Existing UDP enable flags and per-scene ports were retained (including Stage1's disabled receiver and Stage2's port 1100).
