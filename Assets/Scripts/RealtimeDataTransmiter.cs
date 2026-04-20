using UnityEngine;
using System.Text;

/// <summary>
/// Transmite datos faciales en tiempo real via ADB logcat (cable USB).
/// En el PC, filtra con: adb logcat -s FACIAL_DATA
/// No requiere red ni WebSocket.
/// </summary>
public class RealtimeDataTransmitter : MonoBehaviour
{
    [Header("Configuración")]
    public bool enableRealtimeTransmission = true;

    [Tooltip("Tag usado para filtrar en logcat desde el PC")]
    public string logTag = "FACIAL_DATA";

    [Tooltip("Solo transmitir valores mayores a este umbral (reduce ruido)")]
    [Range(0f, 0.1f)]
    public float minimumValueThreshold = 0.01f;

    private bool isTransmitting = false;

    public void StartTransmission()
    {
        isTransmitting = true;
        // Señal de inicio de sesión para el receptor Python
        Debug.Log($"[{logTag}]{{\"event\":\"START\",\"t\":{Time.time:F3}}}");
        Debug.Log("Transmisión ADB iniciada");
    }

    public void StopTransmission()
    {
        // Señal de fin de sesión para el receptor Python
        Debug.Log($"[{logTag}]{{\"event\":\"STOP\",\"t\":{Time.time:F3}}}");
        isTransmitting = false;
        Debug.Log("Transmisión ADB detenida");
    }

    /// <summary>
    /// Llamado desde FacialExpressionCapture cada frame capturado.
    /// Emite JSON compacto con solo los valores activos (> threshold).
    /// Formato: [FACIAL_DATA]{"t":1.234,"d":{"12":0.85,"13":0.90}}
    /// </summary>
    public void TransmitFacialData(FacialExpressionCapture.FacialData data)
    {
        if (!isTransmitting || !enableRealtimeTransmission) return;

        StringBuilder json = new StringBuilder();
        json.Append("{\"t\":").Append(data.timestamp.ToString("F3")).Append(",\"d\":{");

        bool first = true;
        foreach (var kvp in data.expressions)
        {
            if (kvp.Value > minimumValueThreshold)
            {
                if (!first) json.Append(",");
                json.Append("\"").Append((int)kvp.Key).Append("\":").Append(kvp.Value.ToString("F3"));
                first = false;
            }
        }

        json.Append("}}");
        Debug.Log($"[{logTag}]{json}");
    }

    public bool IsTransmitting() => isTransmitting;
}