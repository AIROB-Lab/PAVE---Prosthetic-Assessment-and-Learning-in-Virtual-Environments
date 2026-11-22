using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Writes one CSV line per Interact trial (for study_session.py analyze).
/// Times are Unix seconds (same clock as Python's time.time()), so trials can be matched to study blocks.
/// Called from HIL_Manager: StartTrial / PhaseChanged / SetResult / EndTrial.
/// </summary>
public static class InteractTrialLogger
{
    // Folder for the CSV files. Change if your HIL_IL_py folder is somewhere else.
    public static string Folder = @"C:\Users\fu32gesi\Development\HIL_IL_py\data\interact_logs";

    static StreamWriter writer;
    static int trial;
    static bool running;
    static double tStart, tPhase;
    static string phase = "None";
    static string result = "unknown";
    static double reachS, graspS, transportS, releaseS;
    static string startShelf, startPose, targetShelf, targetPose;

    static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    static string F(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    static void EnsureOpen()
    {
        if (writer != null) return;
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, $"interact_trials_{(long)Now()}.csv");
        writer = new StreamWriter(path, false);
        writer.WriteLine("trial,start_unix,end_unix,duration_s,result,reach_s,grasp_s,transport_s,release_s," +
                         "start_shelf,start_pose,target_shelf,target_pose,final_pcr,final_orient_dev,study_started");
        writer.Flush();
        Debug.Log($"[InteractTrialLogger] writing {path}");
    }

    static void AddPhaseTime()
    {
        double t = Now();
        double d = t - tPhase;
        switch (phase)
        {
            case "Reach": reachS += d; break;
            case "Grasp": graspS += d; break;
            case "Transport": transportS += d; break;
            case "Release": releaseS += d; break;
        }
        tPhase = t;
    }

    /// <summary>New trial starts (phase None -> Reach).</summary>
    public static void StartTrial(string startShelfName, string startPoseName, string targetShelfName, string targetPoseName)
    {
        EnsureOpen();
        trial++;
        running = true;
        tStart = tPhase = Now();
        phase = "None";
        result = "unknown";
        reachS = graspS = transportS = releaseS = 0;
        startShelf = startShelfName; startPose = startPoseName;
        targetShelf = targetShelfName; targetPose = targetPoseName;
    }

    /// <summary>Phase changed to newPhase (Reach, Grasp, Transport, Release).</summary>
    public static void PhaseChanged(string newPhase)
    {
        if (!running || newPhase == phase) return;
        AddPhaseTime();
        phase = newPhase;
    }

    /// <summary>Remember how the trial ended: success, fail_grasp, fail_transport, fail_release.</summary>
    public static void SetResult(string r)
    {
        if (running) result = r;
    }

    /// <summary>Trial ended (phase -> None). Writes the line.</summary>
    public static void EndTrial(float finalPcr, float finalOrientDev, bool studyStarted)
    {
        if (!running) return;
        AddPhaseTime();
        running = false;
        double tEnd = Now();
        writer.WriteLine(string.Join(",", new[] {
            trial.ToString(), F(tStart), F(tEnd), F(tEnd - tStart), result,
            F(reachS), F(graspS), F(transportS), F(releaseS),
            startShelf, startPose, targetShelf, targetPose,
            F(finalPcr), F(finalOrientDev), studyStarted ? "1" : "0" }));
        writer.Flush();
    }

    public static void Close()
    {
        writer?.Flush();
        writer?.Close();
        writer = null;
    }
}