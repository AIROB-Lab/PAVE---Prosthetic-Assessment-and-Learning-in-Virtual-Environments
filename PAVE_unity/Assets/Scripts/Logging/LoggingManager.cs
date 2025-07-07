using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;



public class LoggingManager : MonoBehaviour
{
    partial class LogSingle
    {
        private string fileName;
        private string header;
        private string buffer = "";
        private float logInterval; // log rate in seconds // Currently not used

        // need an object for a couple of functions

        public LogSingle(string fileName, string header, float logInterval = 5f)
        {
            this.fileName = fileName;
            this.header = header;
            this.logInterval = logInterval;

            // create file & add header
            LoggingManager.CreateLogFile(fileName, header);
        }

        internal void AddToBuffer(string buffer)
        {
            this.buffer += buffer;
        }

        internal string GetResetBuffer()
        {
            string _buffer = this.buffer;
            this.buffer = "";
            return _buffer;
        }

        internal string GetFileName()
        {
            return this.fileName;
        }

    }

    // files and directories
    private static DirectoryInfo LogDirectory = null;
    private static string dateTime;
    private static Dictionary<string, LogSingle> LogsDict = new Dictionary<string, LogSingle>();

    public float loggingInterval;
    

    private void Awake()
    {
        //create folder and filepaths from datetime
        string dateTimeFormat = "yyyyMMd_HHmmss";
        dateTime = DateTime.Now.ToString(dateTimeFormat);
        LogDirectory = CreateDir(dateTime);
    }

    private void Start()
    {
        // Start logging coroutine
        StartCoroutine(LogAllCoroutine(loggingInterval));
    }

    private void OnApplicationQuit()
    {
        // log a final time on application quit
        LogAll();
    }

    // creation of the measurement directory
    // called within Start()
    public DirectoryInfo CreateDir(string date_time)
    {
        string currentDirPath = Directory.GetCurrentDirectory() + "//" + "data" + "//";
        string newDirPath = currentDirPath + date_time;
        return Directory.CreateDirectory(newDirPath);
    }

    /// <summary>
    /// Creates new Log which is automatically managed
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="header"></param>
    /// <param name="logRate">NOT IMPLEMENTED YET; IT WILL USE LOGGINGMANAGER LOG RATE INSTEAD</param>
    public static void CreateNewLog(string fileName, string header, float logRate = 5f)
    {
        LogsDict.Add(fileName, new LogSingle(fileName, header, logRate));
    }


    public static void addTracker(string tracker)
    {
        string trackerHeader = "," +
                    tracker + "_position_x" + "," +
                    tracker + "_position_y" + "," +
                    tracker + "_position_z" + "," +
                    tracker + "_rotation_w" + "," +
                    tracker + "_rotation_x" + "," +
                    tracker + "_rotation_y" + "," +
                    tracker + "_rotation_z" + Environment.NewLine;

        CreateNewLog(tracker, trackerHeader, 5f);
    }


    /// <summary>
    /// Adds new logfile to same folder as existing log files for this run
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="header"></param>
    private static void CreateLogFile(string fileName, string header)
    {
        string logPath = LogDirectory.FullName + "/" + dateTime + "_" + fileName + ".csv";
        File.AppendAllText(logPath, header);
    }

    /// <summary>
    /// Function to log buffer in project folder 
    /// </summary>
    /// <param name="fileName">file name</param>
    /// <param name="buffer">copy of current buffer</param>
    /// <param name="async">indicate of operation should be performed synchronous or asynchronous</param>
    private static async void Log(string fileName, string buffer, bool asyncWrite = false)
    {
        if (asyncWrite)
        {
            await File.AppendAllTextAsync(LogDirectory.FullName + "/" + dateTime + "_" + fileName + ".csv", buffer);
        }
        else
        {
            File.AppendAllText(LogDirectory.FullName + "/" + dateTime + "_" + fileName + ".csv", buffer);
        }
    }

    private static IEnumerator LogAllCoroutine(float interval)
    {
        while (true)
        {
            LogAll();
            // wait for interval
            yield return new WaitForSeconds(interval);
            print("all logged");
        }
    }

    private static void LogAll()
    {
        foreach (var entry in LogsDict)
        {
            string _buffer = entry.Value.GetResetBuffer();
            LoggingManager.Log(fileName: entry.Value.GetFileName(), buffer: _buffer, asyncWrite: true);
        }
    }

    public static void AddToBuffer(string fileName, string buffer)
    {
        if (!LogsDict.ContainsKey(fileName)) throw new Exception("Given Log filename" + fileName + "was not present in dictionary");
        else
        {
            LogsDict[fileName].AddToBuffer(buffer);
        }
    }

}
