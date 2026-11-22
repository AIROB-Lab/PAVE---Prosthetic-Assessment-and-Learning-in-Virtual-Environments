# Windows clone and player validation

Validated on 22 November 2026 with Unity 6000.3.29f1 on the existing Windows development machine. This was an independent Git clone without an existing Library folder; it was not a clean operating-system installation. Unity's normal user-level package cache was available.

## Result

- Fresh `dev` clone and recursive SIM retrieval succeeded. Initial PAVE commit: 953a075. SIM commit: 16d74a1960ce228d9d94ca8c257c120d9352d0e2.
- Final source commit tested: 501417a593ac33560d30cbeccc00ddb7b22f4e81 on feature/windows-player-validation.
- Default TAC Windows x64 development build succeeded: **0 errors, 2 optional OpenXR warnings**, 258,559,649 bytes, 32.27 seconds with the generated import/shader cache. First shader compilation took about six minutes.
- The standalone player was launched independently of the Editor. Its process loaded `PAVE_Data/Plugins/x86_64/mujoco.dll` from the build folder, not a developer installation.
- Loaded native DLL SHA-256: A44726AA6BE39F57468D2A8BBC15AF93477F7948510D50F693AF3C4580636E37. No DllNotFoundException or EntryPointNotFoundException was recorded.
- Runtime evidence, CSV row counts and the session path are in runtime-evidence.json beside this report. Test players were stopped after verification.

## Build issues found and corrected

1. The project retained URP compatibility rendering, but Unity 6.3 required an explicit URP_COMPATIBILITY_MODE define for the Standalone target. The define now preserves the existing rendering behavior without a Render Graph migration.
2. EmissionScript imported UnityEditor.PackageManager from runtime code. The unused Editor-only import was removed.
3. The default build list pointed to the legacy SampleScene, whose XR Hands setup prefab is missing. It now points to the existing HIL-IL_TAC_VR research scene. The legacy source scene remains available, with its unresolved prefab documented; it was not silently repaired or used as the validation scene.

## Limits

No XR headset was connected. The player reported XR_ERROR_FORM_FACTOR_UNAVAILABLE, as expected for that environment. This verifies package retrieval, player compilation, native-library packaging/loading and startup, not headset compatibility or physical grasp accuracy. Only the TAC scene was built. The two optional build warnings concern future OpenXR PoseControl and thumbstick-control API migration.