using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System;

public class DataLogger : MonoBehaviour
{
    [Header("Configuracion de archivo")]
    public string fileName = "facial_data";
    public bool useTimestampInFileName = true;
    private string filePath;
    private StreamWriter writer;
    private bool isLogging = false;
    private StringBuilder csvBuilder;

    void Start()
    {
        string finalFileName = fileName;
        if (useTimestampInFileName)
            finalFileName += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        finalFileName += ".csv";
        filePath = Path.Combine(Application.persistentDataPath, finalFileName);
        Debug.Log("Archivo de datos se guardara en: " + filePath);
        csvBuilder = new StringBuilder();
    }

    public void StartLogging()
    {
        if (isLogging) return;
        try
        {
            writer = new StreamWriter(filePath, false);
            WriteHeaders();
            isLogging = true;
            Debug.Log("Logging iniciado: " + filePath);
        }
        catch (Exception e)
        {
            Debug.LogError("Error al iniciar logging: " + e.Message);
        }
    }

    public void StopLogging()
    {
        if (!isLogging) return;
        try
        {
            if (writer != null)
            {
                writer.Close();
                writer = null;
            }
            isLogging = false;
            Debug.Log("Logging detenido. Archivo guardado en: " + filePath);
            ShowFilePathInConsole();
        }
        catch (Exception e)
        {
            Debug.LogError("Error al detener logging: " + e.Message);
        }
    }

    void WriteHeaders()
    {
        csvBuilder.Clear();
        csvBuilder.Append("Timestamp,");
        foreach (OVRFaceExpressions.FaceExpression expression in Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
        {
            if (expression == OVRFaceExpressions.FaceExpression.Max ||
                expression == OVRFaceExpressions.FaceExpression.Invalid)
                continue;
            csvBuilder.Append(expression.ToString());
            csvBuilder.Append(",");
        }
        csvBuilder.Append("Eye_GazeX,Eye_GazeY,Eye_LeftGazeX,Eye_LeftGazeY,Eye_RightGazeX,Eye_RightGazeY,Eye_LeftConf,Eye_RightConf,Eye_ConvergenceDist");
        csvBuilder.AppendLine();
        writer.WriteLine(csvBuilder.ToString());
    }

    public void LogCombinedData(FacialExpressionCapture.FacialData facial, EyeTrackingCapture.EyeFrame eye)
    {
        if (!isLogging || writer == null) return;
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            csvBuilder.Clear();
            csvBuilder.Append(facial.timestamp.ToString("F4", ic));
            csvBuilder.Append(",");
            foreach (OVRFaceExpressions.FaceExpression expression in Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
            {
                if (expression == OVRFaceExpressions.FaceExpression.Max ||
                    expression == OVRFaceExpressions.FaceExpression.Invalid)
                    continue;
                float value = 0f;
                facial.expressions.TryGetValue(expression, out value);
                csvBuilder.Append(value.ToString("F4", ic));
                csvBuilder.Append(",");
            }
            if (eye.valid)
            {
                csvBuilder.Append(eye.gazeX.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.gazeY.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.leftGazeX.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.leftGazeY.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.rightGazeX.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.rightGazeY.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.leftConfidence.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.rightConfidence.ToString("F3", ic)); csvBuilder.Append(",");
                csvBuilder.Append(eye.convergenceDist.ToString("F3", ic));
            }
            else
            {
                csvBuilder.Append(",,,,,,,, ");
            }
            csvBuilder.AppendLine();
            writer.WriteLine(csvBuilder.ToString());
            if (Time.frameCount % 100 == 0) writer.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError("Error al escribir datos combinados: " + e.Message);
        }
    }

    // Mantiene compatibilidad con llamadas existentes sin eye tracking
    public void LogFacialData(FacialExpressionCapture.FacialData data)
    {
        LogCombinedData(data, default);
    }

    void ShowFilePathInConsole()
    {
        Debug.Log("=================================================");
        Debug.Log("ARCHIVO GUARDADO EN:");
        Debug.Log(filePath);
        Debug.Log("=================================================");
        Debug.Log("Para recuperar el archivo desde Quest Pro:");
        Debug.Log("1. Conecta Quest Pro a PC con USB");
        Debug.Log("2. Abre 'Archivos' o 'Explorador de archivos'");
        Debug.Log("3. Navega a: Quest Pro > Internal Storage > Android > data > com.tucompania.FacialTracking > files");
        Debug.Log("=================================================");
    }

    void OnApplicationQuit() { StopLogging(); }
    void OnDestroy() { StopLogging(); }

    public string GetFilePath() { return filePath; }
}
