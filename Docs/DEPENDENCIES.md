# Dependencies and Unity 6 baseline

Use Unity 6000.3.29f1. Clone with `git clone --recurse-submodules` or run `git submodule update --init --recursive` after checkout.

- MuJoCo 3.2.0 is embedded in PAVE_unity/Packages/org.mujoco with its matching Windows native library and Apache license. This preserves the existing loaded engine baseline. Native SHA256: A44726AA6BE39F57468D2A8BBC15AF93477F7948510D50F693AF3C4580636E37.
- mj-unity-extensions 0.1.0 is embedded from the existing local installation, with upstream license. Original upstream: https://github.com/Balint-H/modular-agents. The original local folder had no Git history; do not claim a verified upstream commit. This snapshot is pinned by PAVE Git content.
- SIM is a separate Git submodule at Dependencies/SIM, canonical upstream https://github.com/AIROB-Lab/SIM---Streamlined-Input-Manager-UDP. Its package is referenced by a repository-relative path. The gitlink pins the exact commit; update it explicitly after reviewing upstream changes. The modular package is maintained on SIM's feature/modular-unity-runtime branch; it is not copied into Assets.

Do not hand-copy mujoco.dll into Assets or edit dependencies to personal drive paths. Upgrading MuJoCo requires updating the managed package and native binary together, then verifying the existing reference scene and a Windows player. Vendor assets/SDKs retain their own licenses. XR and SRanipal remain optional hardware capabilities; no headset is required to compile the transport module.

The integration preserves the Unity 6 migration, kitchen Stage3 content and the later HIL shelf/ghost/trial changes. Conflicting Unity settings/materials favor the Unity 6 baseline; PastaBox retains the preserved Unity 6 baseline; the incoming student scene contained none of the shared prosthesis controllers. HIL-IL_VR uses the later shelf/ghost scene because generated mesh IDs made a line merge unsafe. Scene import/reference checks are still required before declaring behavioral equivalence.
