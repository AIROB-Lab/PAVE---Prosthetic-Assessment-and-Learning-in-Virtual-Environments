# Assessment scenes

These scenes are for **testing a fixed prosthetic controller**, not training it. There are no learning updates, rewards, ghost demonstrations, adaptive difficulty, object snapping or automatic practice/retry loops. PAVE cannot disable learning inside an external Python process: the operator must freeze it and record the controller/model identifier before starting.

## Open and run

Use Unity 6000.3.29f1 and initialize the SIM submodule. Open one of these scenes under `PAVE_unity/Assets/Scenes/Assessments/`:

| Scene | Fixed virtual protocol |
| --- | --- |
| TargetedBoxAndBlock | Nine numbered blocks transferred in order to mirrored target apertures |
| BoxAndBlock | 150 blocks; 60 seconds; one point per qualifying hand transport |
| ClothespinRelocationUp | Three physical spring clips transferred from horizontal to vertical rail |
| ClothespinRelocationDown | Three physical spring clips transferred from vertical to horizontal rail |

The scenes reuse the TAC Hannes hand, existing UDP bindings, tracked right-elbow base and XR rig. They currently configure the right prosthesis; changing the source-side boolean does not turn it into a validated left-hand apparatus. Original scenes and HTC/SRanipal gaze origins are preserved.

Enter Play Mode. On the desktop operator panel, enter a participant pseudonym and frozen controller/model identifier, confirm that external learning is disabled, and confirm apparatus/fingertip calibration. Give the participant the task instructions and select **Start assessment / give GO cue**. Start is blocked when native physics, fresh input, head tracking, elbow tracking or hand alignment is unavailable. During a participant trial, losing input/tracking, changing recorded configuration, pausing, losing application focus or a prolonged physics-step gap invalidates the trial. Abort is recorded separately from task failure. Reset is available only between trials and clears prop velocities and native warm-start/applied-force state.

A **Technical run - exclude from assessment** option bypasses the participant/input readiness checks for engineering verification. Its manifest and result rows are explicitly marked `diagnostics_excluded`. It does not enable a training controller, and its results must not be mixed with participant assessments.

The operator panel is desktop IMGUI, not participant coaching. Static numbers and instructions identify the task. New scenes are included in Build Settings after the existing entry; the existing TAC build entry is retained. The panel can load another assessment between trials.

## Scoring and virtual adaptations

### Box and Block

Each cube is 25 mm and the partition is 152 mm high. The scene contains 150 physical cubes with an explicit deterministic initial layout (seed 417 for small yaw variations). Cube mass is a provisional 10 g. The host monotonic timer runs for 60 seconds; samples at or after the deadline cannot add points.

Both the object trajectory and at least one configured fingertip must cross the divider plane above the divider and within its front/back extent. The object must have contacted the native hand and subsequently be released. Blocks transported on one hand trip count as one point. An already transported block cannot score again by bouncing or recrossing; a block that subsequently falls out does not lose its point. A simple destination trigger is not used for scoring.

The supplied fingertip markers are derived from distal mesh surfaces and must be checked against the hand before a study. Transport episodes and individual crossings are logged so the automated count can be compared with observer scoring. The published BBT includes familiarization; this assessment implementation does not automatically run a practice/training phase. Any separately administered familiarization must be documented by the study protocol.

Source: [Mathiowetz-derived BBT instructions](https://www.sralab.org/sites/default/files/2017-06/Box%20and%20Blocks%20Test%20Instructions.pdf).

### Targeted Box and Block

This uses the revised **nine-block** arrangement, not the earlier 16-block version. Blocks and target positions are numbered; transports must occur in order. A failed placement is retained as an unsuccessful transport rather than silently requiring nine perfect placements. A wrong transport order invalidates the trial. Completion time retains the last hand-contact timestamp of block nine; placement confirmation does not add its grace interval to that recorded time. Timing resolution is limited by the physics step and contact observations.

A short observation window lets released blocks settle before classifying placement. Fully-in-target requires every cube corner to be inside the aperture and the object to be sufficiently slow. The prototype aperture is 36 mm square with a 6.35 mm-high insert frame. These geometry/tolerance choices must be compared against reference equipment; they are an explicit virtual adaptation. The administrative timeout is 300 seconds, separate from the task's completion-time outcome.

Source: [Kontson et al., revised targeted BBT, 2025](https://pmc.ncbi.nlm.nih.gov/articles/PMC12447201/).

### Clothespin relocation

Each pin has a free lower body and a passive spring-hinged upper jaw. It must be physically released from the source rail and supported by contacts between both jaws and the target rail after hand withdrawal. There is no invisible attachment. Supported placement must persist for 0.25 seconds at low velocity; completion timing retains the last release timestamp. Dropping a pin fails that trial. A restarted attempt needs a new explicit trial/reset, preserving the failed record.

This is a **CRT-inspired virtual assessment**, not a calibrated replica of the Rolyan red pin apparatus. Prototype pins are 75 mm long, jaws are 8 g each, rails have 4 mm radius, and the hinge uses 0.3 N m/rad stiffness and 0.01 N m s/rad damping. The pin scenes use elliptic friction, impedance ratio 10, six-dimensional contacts, a 4 ms contact time constant and 3 mm rolling friction. The hinge range is -15 to 45 degrees with a -6 degree spring equilibrium; positive native rotation opens the jaws. Horizontal-source pins start hanging below the rail. These are provisional mechanical parameters: geometry, clamp retention and force-opening response require physical reference measurements. Operator start/automatic placement confirmation differs from the published participant-operated timing button; the initial poses also differ from the specified 45-degree pin presentation. The 300-second administrative timeout is not a published CRT score. Do not compare these times with physical CRT norms.

Source: [Kyberd, Hussaini and Maillet, CRT characterization and protocol appendix](https://pure.port.ac.uk/ws/portalfiles/portal/12980095/Clothespin_Relocation_Test.pdf).

## Records and physics ownership

The shared logger writes under `persistentDataPath/PAVE/data/<session>/`:

- `assessment_<trial id>.json`: protocol version, purpose, participant pseudonym, controller identifier, operator attestations, scene/build information, resolved configuration, compiled native object mass and geom friction/density settings.
- `AssessmentEvents.csv`: native contact transitions, divider crossings, transports and terminal outcomes.
- `AssessmentResults.csv`: one terminal result per started trial, including aborted/invalid/failed trials.
- `AssessmentObjects.csv`: native post-step observations at 20 Hz: pose, linear velocity, hand/support contact flags, hand-object contact normal-force sum and jaw angle.
- `AssessmentHand.csv`: hand position, normalized command values, input freshness and used UDP source time. Commands are explicitly labeled as commands, not measured joint states.
- Existing SIM acquisition streams retain their coordinate conventions and provider validity.

Scoring observes every native step. The block scenes request a 5 ms fixed step and the pin scenes a 2 ms fixed step and restore the previous step on destruction; they do not change the original research scenes or the vendor plugin. The known command-to-native synchronization delay is not repaired here and remains one step. Native contact normal-force sums are not calibrated pinch force. Unity supplies presentation and XR movement; MuJoCo owns hand/prop/rail mechanics. Legacy task managers, feedback senders, collision proxies and ghost rigs are removed from the new scene copies only.

## Maintenance and validation

`Tools > PAVE > Assessments > Create assessment scenes` generates scenes from a preview of the saved TAC scene without closing or saving open user scenes. The menu refuses to overwrite existing assessments. The explicit editor API `PaveAssessmentSceneBuilder.BuildAll(true)` regenerates them when deliberately requested; this replaces scene edits and should only be used with reviewed changes.

Focused Edit Mode tests cover deadline exclusion, multi-block scoring, repeated crossings, unsuccessful targeted transports, transport order and release flicker. They do not establish physical realism or clinical validity. Before participant use, compare automated events with manual/video scoring; measure reference apparatus geometry, masses and spring response; check stable grip/release and both CRT directions; verify headset/tracker setup and end-to-end UDP logging; and measure 150-block performance at the chosen timestep. No force/slip benchmark or full SHAP/PHAM score is introduced in this implementation.
### Implementation verification status — 27 November 2026

- The four scene assets were generated in Unity 6000.3.29f1. The final source compiled independently against the installed Unity assemblies with zero warnings and zero errors.
- All 13 focused NUnit scoring checks passed in the independent .NET harness using the actual rules and test source. This does not claim a successful Unity Test Runner run; that earlier request was interrupted by a save-scenes dialog.
- Native Play Mode smoke checks exercised stepping, diagnostic recording, abort and reset in all four scenes. Prop reset velocities were zero. Technical records are marked `diagnostics_excluded`. CSV/manifest checks confirmed one aborted result per diagnostic trial, object and hand rows, finite force/position values and observed source contacts.
- Correcting the native hinge direction restored physical jaw closure. Adding damping and using a 2 ms step with firmer contacts and rolling resistance kept all three vertical-rail pins supported by both jaws for 300 simulated seconds. Maximum displacement from the starting pose was approximately 22 mm. A 0.15 N m diagnostic joint torque opened a jaw to approximately 23 degrees, followed by a stationary reset. This applied torque is a validation probe, not participant assistance.
- Horizontal-rail pins also passed retention and opening checks. The earlier 5 ms configuration retained them for 300 simulated seconds; the final 2 ms configuration passed a 10-second retention check and opened to approximately 23 degrees.
- The saved-scene structural audit passed: expected prop counts (150/9/3/3), one hand controller per scene, no unresolved script GUIDs and no legacy task/feedback components in the copies.
- The 150-block native smoke took approximately 2.62 seconds of wall time for 2.5 seconds of simulated time on this Editor run, including diagnostic logging but excluding headset rendering. Real-time VR performance is not established.

These are engineering prototypes. Slow pin displacement remains measurable and the clamp parameters are not calibrated to a physical red pin. Headset/hand grasp-and-relocation trials, end-to-end participant UDP checks, visual review, reference apparatus measurements and observer-scoring agreement remain necessary before participant use. No clinical equivalence or normative score validity is claimed.
