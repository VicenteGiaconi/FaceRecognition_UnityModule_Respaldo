using UnityEngine;
using System;
using System.Collections.Generic;

public class FacialExpressionCapture : MonoBehaviour
{
    [Header("Referencias")]
    public OVRFaceExpressions faceExpressions;

    [Header("Configuración")]
    public bool captureData = false;
    public float captureInterval = 0.1f; // Captura cada 100ms (10Hz)

    private float nextCaptureTime = 0f;
    private DataLogger dataLogger;
    private RealtimeDataTransmitter realtimeTransmitter;
    private WebSocketSender webSocketSender;

    // Estructura para almacenar datos de expresión facial
    [System.Serializable]
    public class FacialData
    {
        public float timestamp;
        public Dictionary<OVRFaceExpressions.FaceExpression, float> expressions;

        public FacialData()
        {
            expressions = new Dictionary<OVRFaceExpressions.FaceExpression, float>();
        }
    }

    void Start()
    {
        // Obtener o agregar componente OVRFaceExpressions
        if (faceExpressions == null)
        {
            faceExpressions = GetComponent<OVRFaceExpressions>();
            if (faceExpressions == null)
            {
                faceExpressions = gameObject.AddComponent<OVRFaceExpressions>();
            }
        }

        // Obtener DataLogger
        dataLogger = GetComponent<DataLogger>();
        if (dataLogger == null)
        {
            Debug.LogError("DataLogger no encontrado. Agrega el componente DataLogger al mismo GameObject.");
        }

        // Obtener RealtimeDataTransmitter
        realtimeTransmitter = GetComponent<RealtimeDataTransmitter>();
        if (realtimeTransmitter == null)
        {
            Debug.LogWarning("RealtimeDataTransmitter no encontrado. Transmisión en tiempo real deshabilitada.");
        }

        // Obtener WebSocketSender
        webSocketSender = GetComponent<WebSocketSender>();
        if (webSocketSender == null)
        {
            Debug.LogWarning("WebSocketSender no encontrado. Envío de resumen deshabilitado.");
        }

        Debug.Log("FacialExpressionCapture inicializado");
    }

    void Update()
    {
        if (!captureData || faceExpressions == null || !faceExpressions.FaceTrackingEnabled)
        {
            return;
        }

        // Capturar datos según el intervalo configurado
        if (Time.time >= nextCaptureTime)
        {
            CaptureFacialExpressions();
            nextCaptureTime = Time.time + captureInterval;
        }
    }

    void CaptureFacialExpressions()
    {
        FacialData data = new FacialData();
        data.timestamp = Time.time;

        // Capturar todas las expresiones faciales disponibles
        foreach (OVRFaceExpressions.FaceExpression expression in Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
        {
            if (expression == OVRFaceExpressions.FaceExpression.Max ||
                expression == OVRFaceExpressions.FaceExpression.Invalid)
            {
                continue;
            }

            float weight = 0f;
            if (faceExpressions.TryGetFaceExpressionWeight(expression, out weight))
            {
                data.expressions[expression] = weight;
            }
        }

        // Enviar datos al logger
        if (dataLogger != null)
        {
            dataLogger.LogFacialData(data);
        }

        // Enviar datos al transmisor en tiempo real
        if (realtimeTransmitter != null)
        {
            realtimeTransmitter.TransmitFacialData(data);
        }

        // Registrar datos para WebSocket
        if (webSocketSender != null)
        {
            webSocketSender.RecordData(data);
        }
    }

    // Métodos públicos para control externo
    public void StartCapture()
    {
        captureData = true;
        nextCaptureTime = Time.time;

        // Iniciar sesión de WebSocket
        if (webSocketSender != null)
        {
            webSocketSender.StartSession();
        }

        Debug.Log("Captura iniciada");
    }

    public void StopCapture()
    {
        captureData = false;

        // Enviar datos por WebSocket al finalizar
        if (webSocketSender != null)
        {
            webSocketSender.EndSessionAndSend();
        }

        Debug.Log("Captura detenida");
    }

    // Obtener expresión específica en tiempo real
    public float GetExpression(OVRFaceExpressions.FaceExpression expression)
    {
        if (faceExpressions != null && faceExpressions.FaceTrackingEnabled)
        {
            float weight = 0f;
            faceExpressions.TryGetFaceExpressionWeight(expression, out weight);
            return weight;
        }
        return 0f;
    }

    // Verificar si el tracking facial está activo
    public bool IsFaceTrackingEnabled()
    {
        return faceExpressions != null && faceExpressions.FaceTrackingEnabled;
    }
}