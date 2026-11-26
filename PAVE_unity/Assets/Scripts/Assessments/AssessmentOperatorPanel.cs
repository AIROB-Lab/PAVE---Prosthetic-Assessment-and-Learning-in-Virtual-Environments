using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pave.Assessments
{
    /// <summary>Desktop operator controls, not participant coaching. Protocol and input setup
    /// remain fixed during a trial. Diagnostics are explicitly excluded from assessment records.</summary>
    public sealed class AssessmentOperatorPanel : MonoBehaviour
    {
        public AssessmentSession Session;
        public static readonly string[] ScenePaths =
        {
            "Assets/Scenes/Assessments/TargetedBoxAndBlock.unity",
            "Assets/Scenes/Assessments/BoxAndBlock.unity",
            "Assets/Scenes/Assessments/ClothespinRelocationUp.unity",
            "Assets/Scenes/Assessments/ClothespinRelocationDown.unity"
        };
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 430, 580), GUI.skin.box);
            GUILayout.Label("PAVE assessment operator");
            GUILayout.Label(Session.ProtocolId + " | " + Session.State);
            bool running = Session.State == TrialState.Running;
            GUI.enabled = !running;
            GUILayout.Label("Participant pseudonym");
            Session.ParticipantCode = GUILayout.TextField(Session.ParticipantCode);
            GUILayout.Label("Frozen controller / model identifier");
            Session.FrozenControllerId = GUILayout.TextField(Session.FrozenControllerId);
            Session.FrozenControllerConfirmed = GUILayout.Toggle(Session.FrozenControllerConfirmed, "External learning disabled; controller fixed");
            Session.ApparatusCalibrationConfirmed = GUILayout.Toggle(Session.ApparatusCalibrationConfirmed, "Apparatus and fingertip calibration checked");
            Session.DiagnosticsOnly = GUILayout.Toggle(Session.DiagnosticsOnly, "Technical run - exclude from assessment");
            string reason = Session.StartBlockingReason();
            GUI.enabled = !running && reason.Length == 0;
            if (GUILayout.Button("Start assessment / give GO cue"))
            {
                Session.BeginTrial();
            }

            GUI.enabled = running;
            if (GUILayout.Button("Abort trial"))
            {
                Session.AbortTrial();
            }

            GUI.enabled = !running;
            if (GUILayout.Button("Reset apparatus for a new trial"))
            {
                Session.ResetForNextTrial();
            }

            GUILayout.Label("Load another assessment:");
            foreach (string path in ScenePaths)
            {
                if (GUILayout.Button(System.IO.Path.GetFileNameWithoutExtension(path)))
                {
                    SceneManager.LoadScene(path);
                }
            }

            GUI.enabled = true;
            GUILayout.Label("Elapsed: " + Session.ElapsedSeconds.ToString("F2") + " s");
            GUILayout.Label(running ? "Trial in progress" : Session.LastOutcome);
            if (!running && Session.State != TrialState.Ready)
            {
                GUILayout.Label("Score / released: " + Session.Score + " | unsuccessful: " + Session.UnsuccessfulTransports);
            }

            if (!running && reason.Length > 0)
            {
                GUILayout.Label(reason);
            }

            GUILayout.Label("Virtual assessment adaptation. No training, assistance or automatic practice.");
            GUILayout.EndArea();
        }
    }
}
