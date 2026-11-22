# Lightweight C# maintenance

Use PascalCase for types, methods, public configuration and properties; camelCase for parameters, locals and private fields. Keep methods short enough to follow the data flow. Prefer an Inspector reference or a small typed sample over a service locator or global event framework.

Comments should explain purpose, required references, units, coordinate frame, timing and missing-data behavior. Do not narrate obvious assignments. Keep vendor/native conversion at the provider boundary and scientific task rules in the task.

When renaming a serialized field, use FormerlySerializedAs and verify the existing scenes and prefabs load their values. Preserve script meta GUIDs. Do not renumber serialized enums or alter wire categories during a naming cleanup. Existing DOA binding names are retained for study compatibility; new acquisition/provider configuration uses standard public names.

Remove code only after checking C# references and serialized references. Unity lifecycle methods, Inspector events, animation events and native callbacks can be entry points without direct C# calls. Empty private Start/Update callbacks on direct MonoBehaviour classes can be removed; public callbacks or overrides require individual review.

The acquisition cleanup replaces unconsumed legacy pose/eye wrappers with typed HeadPose, LeftControllerPose, RightControllerPose and TrackerPoses observations. Always check IsValid. Generic gaze remains separate from HTC's original head-local camera reference. See RESEARCH_TRACKING.md for recording formats and verification limits.

This pass covers acquisition, shared runtime readability, unused tracking buffers/headers/helpers, empty first-party lifecycle callbacks and compiler-confirmed dead declarations. Student task state machines retain their scientific behavior and legacy serialized binding names. Vendor packages and SDK source are maintained upstream.
