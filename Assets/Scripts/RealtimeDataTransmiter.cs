using UnityEngine;
using System.Text;
using System;

public class RealtimeDataTransmitter : MonoBehaviour
{
    [Header("Configuración")]
    public bool enableRealtimeTransmission = true;
    public string logTag = "FACIAL_DATA"; // Tag para identificar en logcat

    private FacialExpressionCapture facialCapture;
    private bool isTransmitting = false;

    void Start()
    {
        facialCapture = GetComponent<FacialExpressionCapture>();
        if (facialCapture == null)
        {
            Debug.LogError("FacialExpressionCapture no encontrado");
        }
    }

    public void StartTransmission()
    {
        isTransmitting = true;
        Debug.Log("Transmisión en tiempo real iniciada");
    }

    public void StopTransmission()
    {
        isTransmitting = false;
        Debug.Log("Transmisión en tiempo real detenida");
    }

    // Este método será llamado desde FacialExpressionCapture
    public void TransmitFacialData(FacialExpressionCapture.FacialData data)
    {
        if (!isTransmitting || !enableRealtimeTransmission) return;

        // Construir mensaje JSON compacto para transmisión
        StringBuilder json = new StringBuilder();
        json.Append("{");
        json.Append("\"t\":").Append(data.timestamp.ToString("F3")).Append(",");
        json.Append("\"d\":{");

        bool first = true;
        foreach (var kvp in data.expressions)
        {
            // Solo transmitir valores significativos (>0.01) para reducir ancho de banda
            if (kvp.Value > 0.01f)
            {
                if (!first) json.Append(",");
                json.Append("\"").Append((int)kvp.Key).Append("\":").Append(kvp.Value.ToString("F3"));
                first = false;
            }
        }

        json.Append("}}");

        // Enviar a logcat con tag especial para filtrado
        Debug.Log($"[{logTag}]{json.ToString()}");
    }

    public bool IsTransmitting()
    {
        return isTransmitting;
    }
}