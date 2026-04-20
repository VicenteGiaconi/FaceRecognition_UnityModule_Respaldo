using UnityEngine;
using System;
using System.Collections.Generic;

public class FacialExpressionCapture : MonoBehaviour
{
    [Header("Referencias")]
    public OVRFaceExpressions faceExpressions;

    [Header("Configuración")]
    public bool captureData = false;

    [Tooltip("Intervalo de captura en segundos. 0.1 = 10Hz, 0.033 = 30Hz")]
    public float captureInterval = 0.1f;

    private float nextCaptureTime = 0f;
    private DataLogger dataLogger;
    private RealtimeDataTransmitter realtimeTransmitter;

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
        if (faceExpressions == null)
        {
            faceExpressions = GetComponent<OVRFaceExpressions>();
            if (faceExpressions == null)
                faceExpressions = gameObject.AddComponent<OVRFaceExpressions>();
        }

        dataLogger = GetComponent<DataLogger>();
        realtimeTransmitter = GetComponent<RealtimeDataTransmitter>();

        if (dataLogger == null)
            Debug.LogError("[FaceCapture] DataLogger no encontrado.");

        Debug.Log("[FaceCapture] Inicializado.");
    }

    void Update()
    {
        if (!captureData || faceExpressions == null || !faceExpressions.FaceTrackingEnabled)
            return;

        if (Time.time >= nextCaptureTime)
        {
            CaptureFacialExpressions();
            nextCaptureTime = Time.time + captureInterval;
        }
    }

    void CaptureFacialExpressions()
    {
        FacialData data = new FacialData { timestamp = Time.time };

        foreach (OVRFaceExpressions.FaceExpression expression in
                 Enum.GetValues(typeof(OVRFaceExpressions.FaceExpression)))
        {
            if (expression == OVRFaceExpressions.FaceExpression.Max ||
                expression == OVRFaceExpressions.FaceExpression.Invalid)
                continue;

            float weight = 0f;
            if (faceExpressions.TryGetFaceExpressionWeight(expression, out weight))
                data.expressions[expression] = weight;
        }

        dataLogger?.LogFacialData(data);
        realtimeTransmitter?.TransmitFacialData(data);
    }

    public void StartCapture()
    {
        captureData = true;
        nextCaptureTime = Time.time;
        Debug.Log("[FaceCapture] Captura iniciada.");
    }

    public void StopCapture()
    {
        captureData = false;
        Debug.Log("[FaceCapture] Captura detenida.");
    }

    public float GetExpression(OVRFaceExpressions.FaceExpression expression)
    {
        if (faceExpressions == null || !faceExpressions.FaceTrackingEnabled) return 0f;
        float weight = 0f;
        faceExpressions.TryGetFaceExpressionWeight(expression, out weight);
        return weight;
    }

    public bool IsFaceTrackingEnabled()
        => faceExpressions != null && faceExpressions.FaceTrackingEnabled;
}