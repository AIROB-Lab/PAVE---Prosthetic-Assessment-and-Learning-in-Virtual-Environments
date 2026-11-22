# Shared runtime: a short guide for researchers

## Start here

Use Unity 6000.3.29f1 and initialize the SIM submodule. Dependency versions and native-library details are in DEPENDENCIES.md. Open an existing research scene; no new assessment scenes were introduced by this refactoring.

The shared path is:

    SIM package: codec -> UDP queue -> packet history
         -> PAVE StreamlinedInputManager adapter
         -> HandController -> MuJoCo pre-step
         -> physical joint state / task observations
         -> task feedback and session records

SIM's feature/modular-unity-runtime branch contains the upstream package. PAVE pins its published commit in Dependencies/SIM and references its package through a relative path. Change generic protocol, history or socket behavior upstream, then update the gitlink. Keep PAVE's DOA meanings, task restrictions and scientific outcomes here.

## Components and ownership

- StreamlinedInputManager owns PAVE's input settings and drains SIM packets on the main thread. Its original name and fields preserve scene compatibility. It no longer implements packet decoding or opens native socket callbacks itself.
- PaveTracking owns PAVE recording, provider selection and gaze regions. The optional SIM tracking package owns generic read-only XR acquisition. PaveSranipalEyeSource isolates the HTC SDK and callback queue. See RESEARCH_TRACKING.md for setup, unchanged HTC gaze origins, coordinate frames, CSV changes and verification.
- HandController owns normalized actuator/mocap bindings and applies them in MjScene.preUpdateEvent. It does not know about kitchen levels or WPS exercises. Command source, delay settings and axis restrictions are explicit.
- HandCommandSource is the small optional input boundary. ManualHandCommandSource provides Inspector values in [-1,1]; an unassigned source uses SIM. A future replay source can implement the same TryRead method without changing actuation.
- HandJointState reads a directly assigned MuJoCo hinge: PositionRad and VelocityRadPerSecond are measured values. GetValueForDOA deliberately remains a command-value API for existing external feedback contracts. Changing those contracts is a separate protocol decision.
- WpsTrainingRestriction is the student study rule that temporarily suppresses HOC/WFE during isolated WPS practice. The LR compatibility controller installs this bridge; the shared controller has no TaskManager dependency.
- LoggingManager owns a single session directory and synchronized buffers, periodically writing in order. Disable/quit performs a final synchronous flush and closes files. LoggingManager_LR delegates to the same implementation. Output defaults to persistentDataPath/PAVE/data, not a developer's working directory.
- PubSensorData reads typed scalar/vector/quaternion components, records their actual dimensionality and uses the shared feedback sender. Numeric CSV output uses invariant culture. Axis-selective aggregation excludes quaternion W.
- FollowTracker implements both families' calibrated following behavior. The LR component delegates to it; legacy optional realignment references are retained.

The LR-named wrappers remain to preserve serialized component identities. They contain no duplicated control/transport/logging implementation. Remove a wrapper only with a deliberate scene/prefab migration that keeps its GUID and fields valid.

## Configure a hand without copying code

1. Reuse a hand rig and its HandController component. Keep binding enum values stable; serialized scenes depend on them.
2. Assign each DOA's MuJoCo actuator objects, SIM subcategory and input/output ranges. Positive/negative normalized values map to the corresponding actuator control range. Nonfinite inputs and zero-width mappings are invalid.
3. If a legacy axis is kinematic, configure DOA_mocap with the attachment, local axis and degree limits. This is not a force-driven joint and must be identified as such in a study.
4. For a desktop check, add ManualHandCommandSource to the runtime hand and assign CommandSource. The axes being driven must have EMG_override enabled; ghost targets that deliberately override commands remain task-owned.
5. Assign HandJointState to physical hinges when measured joint quantities are needed. Do not label command fields as physical measurements.

CommandTimeoutSeconds is receipt-age based. Stale or unavailable input holds the preceding command and sets InputIsFresh false. The sample delay is zero-based: one sample back really selects the preceding packet. Time-based delays return no result when the required history is unavailable. Delay selection replaces the previous mode. The legacy active field remains study metadata; SetAxisRestricted is the explicit actuation restriction.

## Add a study with a small task class

Keep each task's rules in its own MonoBehaviour. Use Inspector references to the hand, targets and props. Request axis restrictions through HandController; write task events through LoggingManager; send contextual feedback through SimUdpSender. Do not add task-specific branches to SIM or the hand controller. Reuse plain settings/data classes when values belong together; no service locator, global event bus or dependency-injection framework is required.

TaskManager_LR.cs and LevelManager_LR.cs were renamed to match their actual TaskManager and LevelManager classes, preserving their meta GUIDs. Existing study logic is otherwise retained. Kitchen pouring variants and other legacy task-specific helpers are still candidates for a later focused cleanup; this change does not claim that their physical models have been validated.

## Comments, names and migration behavior

Explain a class's purpose, required references, lifecycle, units, frame and missing-data behavior. Document why a timing choice or formula exists. Use consistent PascalCase type/method names and camelCase parameters/locals for new code. Legacy serialized names are retained where renaming would add migration risk. Prefer a short example over extra abstraction layers.

Generic SIM fixes live in its own repository. PAVE integration checks should cover channel/DOA mapping and existing scene behavior. No BBT, CRT or other new assessment protocol is included in this implementation.

## Verification and remaining limits

The integrated project compiled in Unity 6 with zero errors; legacy/vendor warnings remain. All six research scene families were inspected for missing scripts and hand bindings. Four orphan script references with no available implementation were removed: camera-offset components in TAC and PastaBox, and two Stage3 egg-holder components; all had no serialized fields beyond the missing script reference. The preserved Unity 6 PastaBox scene is used because the incoming variant contained no shared hand-controller components.

Temporary C# checks outside the repository passed SIM wire round-trip, truncated-packet rejection, bounded sample/time history, invalid delay, loopback UDP, invalid-packet counting and disposal. A short existing TAC Play Mode run had active controllers and no runtime Console errors before the verification script's attempted live file read. That read encountered a sharing violation; checking after session close confirmed 100 data rows plus one header. This was a verification-reader error, not a lost-write result. XR startup was restored afterward.

These checks do not establish physical grasp fidelity, complete study-protocol equivalence or headset/EMG behavior. Hardware, full task sequences and standalone-player verification remain separate checks. Raw log formats/output location changed as described above; adapt downstream analysis explicitly.
