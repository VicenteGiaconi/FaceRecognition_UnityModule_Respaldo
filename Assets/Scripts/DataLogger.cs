using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System;
public class DataLogger : MonoBehaviour
{
    [Header("Configuraci�n de archivo")]
    public string fileName = "facial_data";
    public bool useTimestampInFileName = true;
    private string filePath;
    private StreamWriter writer;
    private bool isLogging = false;
    private StringBuilder csvBuilder;
    void Start()
    {
        // Crear nombre de archivo con timestamp si est� habilitado
        string finalFileName = fileName;
        if (useTimestampInFileName)
        {
            finalFileName += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
        finalFileName += ".csv";
        // Ruta del archivo (en Android se guarda en persistentDataPath)
        filePath = Path.Combine(Application.persistentDataPath, finalFileName);
        Debug.Log("Archivo de datos se guardar� en: " + filePath);
        csvBuilder = new StringBuilder();
    }
    public void StartLogging()
    {
        if (isLogging) return;
        try
        {
            writer = new StreamWriter(filePath, false); // false = sobrescribir
            // Escribir encabezados
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
            // Mostrar ruta en VR (�til para encontrar el archivo despu�s)
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
        // Agregar todas las expresiones faciales como columnas
        foreach (OVRFaceExpressions.FaceExpression expression in Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
        {
            if (expression == OVRFaceExpressions.FaceExpression.Max ||
                expression == OVRFaceExpressions.FaceExpression.Invalid)
            {
                continue;
            }
            csvBuilder.Append(expression.ToString());
            csvBuilder.Append(",");
        }
        // Remover �ltima coma y agregar nueva l�nea
        csvBuilder.Length--;
        csvBuilder.AppendLine();
        writer.WriteLine(csvBuilder.ToString());
    }
    public void LogFacialData(FacialExpressionCapture.FacialData data)
    {
        if (!isLogging || writer == null) return;
        try
        {
            csvBuilder.Clear();
            csvBuilder.Append(data.timestamp.ToString("F4"));
            csvBuilder.Append(",");
            // Escribir valores de todas las expresiones
            foreach (OVRFaceExpressions.FaceExpression expression in Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
            {
                if (expression == OVRFaceExpressions.FaceExpression.Max ||
                    expression == OVRFaceExpressions.FaceExpression.Invalid)
                {
                    continue;
                }
                float value = 0f;
                if (data.expressions.ContainsKey(expression))
                {
                    value = data.expressions[expression];
                }
                csvBuilder.Append(value.ToString("F4"));
                csvBuilder.Append(",");
            }
            // Remover �ltima coma y agregar nueva l�nea
            csvBuilder.Length--;
            csvBuilder.AppendLine();
            writer.WriteLine(csvBuilder.ToString());
            // Flush peri�dicamente para evitar p�rdida de datos
            if (Time.frameCount % 100 == 0)
            {
                writer.Flush();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error al escribir datos: " + e.Message);
        }
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
        Debug.Log("3. Navega a: Quest 2 > Internal Storage > Android > data > com.tucompa�ia.FacialTracking > files");
        Debug.Log("=================================================");
    }
    void OnApplicationQuit()
    {
        StopLogging();
    }
    void OnDestroy()
    {
        StopLogging();
    }
    // M�todo p�blico para obtener la ruta del archivo
    public string GetFilePath()
    {
        return filePath;
    }
}

