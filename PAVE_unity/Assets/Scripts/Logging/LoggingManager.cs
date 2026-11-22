using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Shared session recorder. Producers are synchronized; flush and shutdown are ordered.</summary>
public class LoggingManager : MonoBehaviour
{
    sealed class LogStream
    {
        internal readonly StreamWriter Writer;
        internal readonly StringBuilder Pending = new StringBuilder();
        internal LogStream(string path, string header)
        {
            Writer = new StreamWriter(path, false, new UTF8Encoding(false));
            Writer.Write(header);
            Writer.Flush();
        }
    }

    static readonly object gate = new object ();
    static readonly Dictionary<string, LogStream> streams = new Dictionary<string, LogStream>();
    static LoggingManager owner;
    static volatile bool recording;
    public static int RecordsAfterClose { get; private set; }
    public static string OutputDirectory { get; private set; }

    [Min(0.1f)]
    public float loggingInterval = 1;
    [Tooltip("Empty uses persistentDataPath/PAVE/data. Relative paths resolve below persistentDataPath.")]
    public string outputFolder = "";
    float nextFlush;
    protected virtual void Awake()
    {
        if (owner != null && owner != this)
        {
            Debug.LogWarning("Using the existing session logger.", this);
            enabled = false;
            return;
        }

        owner = this;
        string root = string.IsNullOrWhiteSpace(outputFolder) ? Path.Combine(Application.persistentDataPath, "PAVE", "data") : Path.IsPathRooted(outputFolder) ? outputFolder : Path.Combine(Application.persistentDataPath, outputFolder);
        OutputDirectory = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(OutputDirectory);
        recording = true;
        RecordsAfterClose = 0;
        File.WriteAllText(Path.Combine(OutputDirectory, "session.txt"), "Unity=" + Application.unityVersion + "\nScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + "\n");
        Debug.Log("PAVE session data: " + OutputDirectory);
    }

    protected virtual void OnEnable()
    {
        if (owner == null)
        {
            Awake();
        }
    }

    protected virtual void Update()
    {
        if (owner != this || Time.unscaledTime < nextFlush)
        {
            return;
        }

        Flush();
        nextFlush = Time.unscaledTime + Mathf.Max(0.1f, loggingInterval);
    }

    /// <summary>Register a CSV stream once per session. Legacy logRate is retained for callers;
    /// all streams flush together at loggingInterval, without dropping producer samples.</summary>
    public static void CreateNewLog(string fileName, string header, float logRate = 5f)
    {
        lock (gate)
        {
            if (streams.ContainsKey(fileName))
            {
                return;
            }

            if (OutputDirectory == null)
            {
                throw new InvalidOperationException("Add a LoggingManager before registering logs.");
            }

            string safeName = fileName;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(c, '_');
            }

            streams.Add(fileName, new LogStream(Path.Combine(OutputDirectory, safeName + ".csv"), header));
        }
    }

    public static void AddToBuffer(string fileName, string buffer)
    {
        lock (gate)
        {
            // Native callbacks may finish after the session is closed. Do not reopen files.
            if (!recording)
            {
                RecordsAfterClose++;
                return;
            }

            if (!streams.TryGetValue(fileName, out var stream))
            {
                throw new InvalidOperationException("Unregistered log: " + fileName);
            }

            stream.Pending.Append(buffer);
        }
    }

    public static void Flush()
    {
        lock (gate)
        {
            foreach (var stream in streams.Values)
            {
                stream.Writer.Write(stream.Pending.ToString());
                stream.Writer.Flush();
                stream.Pending.Clear();
            }
        }
    }

    protected virtual void OnDisable()
    {
        if (owner == this)
        {
            Close();
        }
    }

    protected virtual void OnApplicationQuit()
    {
        if (owner == this)
        {
            Close();
        }
    }

    void Close()
    {
        lock (gate)
        {
            recording = false;
            try
            {
                Flush();
            }
            finally
            {
                foreach (var stream in streams.Values)
                {
                    stream.Writer.Dispose();
                }

                streams.Clear();
                OutputDirectory = null;
                owner = null;
            }
        }
    }
}
