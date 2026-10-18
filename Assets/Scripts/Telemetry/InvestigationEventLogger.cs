using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Appends investigation events to one CSV file per session under Application.persistentDataPath.
/// </summary>
[DisallowMultipleComponent]
public class InvestigationEventLogger : MonoBehaviour
{
    public const string Header =
        "session_id,relative_time_s,event_type,evidence_id,world_x,world_y,world_z,ground_truth_x,ground_truth_y,ground_truth_z,spatial_error_m";

    StreamWriter writer;
    bool openFailed;
    bool failureLogged;

    public string LogFilePath { get; private set; }
    public bool IsOpen => writer != null;

    public bool Begin(string sessionId, string directory = null)
    {
        Close();
        openFailed = false;
        failureLogged = false;
        LogFilePath = null;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogError("Forensic: investigation log was not opened because the session ID is empty.");
            openFailed = true;
            return false;
        }

        try
        {
            string folder = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Application.persistentDataPath, "InvestigationLogs")
                : directory;
            Directory.CreateDirectory(folder);
            LogFilePath = Path.Combine(folder, "investigation-" + sessionId + ".csv");

            var stream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(Header);
            writer.Flush();
            Debug.Log("Forensic: investigation log opened at " + LogFilePath);
            return true;
        }
        catch (Exception exception)
        {
            openFailed = true;
            LogFilePath = null;
            Debug.LogError("Forensic: could not open the investigation log. " + exception.Message);
            return false;
        }
    }

    public void Log(
        string sessionId,
        float relativeTimeSeconds,
        string eventType,
        string evidenceId = "",
        Vector3? worldPosition = null,
        Vector3? groundTruthPosition = null,
        float? spatialErrorMetres = null)
    {
        if (writer == null)
        {
            if (!failureLogged)
            {
                string reason = openFailed ? "the log file could not be opened" : "the session log is not open";
                Debug.LogError("Forensic: event '" + eventType + "' was not written because " + reason + ".");
                failureLogged = true;
            }
            return;
        }

        var line = new StringBuilder(160);
        line.Append(Escape(sessionId));
        line.Append(',');
        line.Append(FormatNumber(relativeTimeSeconds));
        line.Append(',');
        line.Append(Escape(eventType));
        line.Append(',');
        line.Append(Escape(evidenceId));
        line.Append(',');
        AppendVector(line, worldPosition);
        line.Append(',');
        AppendVector(line, groundTruthPosition);
        line.Append(',');
        line.Append(spatialErrorMetres.HasValue ? FormatNumber(spatialErrorMetres.Value) : "");

        try
        {
            writer.WriteLine(line.ToString());
            writer.Flush();
        }
        catch (Exception exception)
        {
            Debug.LogError("Forensic: failed to write '" + eventType + "' to the investigation log. " + exception.Message);
        }
    }

    public void Close()
    {
        if (writer == null)
            return;

        try
        {
            writer.Flush();
            writer.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogError("Forensic: failed to close the investigation log. " + exception.Message);
        }
        finally
        {
            writer = null;
        }
    }

    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!mustQuote)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    static void AppendVector(StringBuilder line, Vector3? value)
    {
        if (!value.HasValue)
        {
            line.Append(",,");
            return;
        }

        line.Append(FormatNumber(value.Value.x));
        line.Append(',');
        line.Append(FormatNumber(value.Value.y));
        line.Append(',');
        line.Append(FormatNumber(value.Value.z));
    }

    static string FormatNumber(float value)
    {
        return value.ToString("0.0000", CultureInfo.InvariantCulture);
    }

    void OnDestroy()
    {
        Close();
    }

    void OnApplicationQuit()
    {
        Close();
    }
}
