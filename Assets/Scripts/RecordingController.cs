using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RecordingController : MonoBehaviour
{
    [Header("Referencias")]
    public FacialExpressionCapture facialCapture;
    public DataLogger dataLogger;
    public RealtimeDataTransmitter realtimeTransmitter;

    [Header("UI (opcional)")]
    public TextMeshProUGUI statusText;
    public Button startButton;
    public Button stopButton;

    [Header("Control por botones VR")]
    public OVRInput.Button startRecordingButton = OVRInput.Button.One; // Botón A/X
    public OVRInput.Button stopRecordingButton = OVRInput.Button.Two;  // Botón B/Y

    private bool isRecording = false;

    void Start()
    {
        // Obtener referencias si no están asignadas
        if (facialCapture == null)
        {
            facialCapture = FindObjectOfType<FacialExpressionCapture>();
        }

        if (dataLogger == null)
        {
            dataLogger = FindObjectOfType<DataLogger>();
        }

        if (realtimeTransmitter == null)
        {
            realtimeTransmitter = FindObjectOfType<RealtimeDataTransmitter>();
        }

        // Configurar botones UI si existen
        if (startButton != null)
        {
            startButton.onClick.AddListener(StartRecording);
        }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopRecording);
            stopButton.interactable = false;
        }

        UpdateStatus("Listo para grabar");
    }

    void Update()
    {
        // Control con botones del controlador VR
        if (OVRInput.GetDown(startRecordingButton) && !isRecording)
        {
            StartRecording();
        }

        if (OVRInput.GetDown(stopRecordingButton) && isRecording)
        {
            StopRecording();
        }

        // Mostrar estado de tracking facial
        if (facialCapture != null && statusText != null && isRecording)
        {
            bool trackingEnabled = facialCapture.IsFaceTrackingEnabled();
            if (!trackingEnabled)
            {
                UpdateStatus("ADVERTENCIA: Tracking facial deshabilitado");
            }
        }
    }

    public void StartRecording()
    {
        if (isRecording) return;

        if (facialCapture == null || dataLogger == null)
        {
            Debug.LogError("Referencias no configuradas");
            UpdateStatus("Error: Referencias faltantes");
            return;
        }

        // Verificar que el tracking facial esté habilitado
        if (!facialCapture.IsFaceTrackingEnabled())
        {
            Debug.LogWarning("El tracking facial no está habilitado en el dispositivo");
            UpdateStatus("Error: Tracking facial no disponible");
            return;
        }

        // Iniciar logging y captura
        dataLogger.StartLogging();
        facialCapture.StartCapture();

        // Iniciar transmisión en tiempo real
        if (realtimeTransmitter != null)
        {
            realtimeTransmitter.StartTransmission();
        }

        isRecording = true;

        // Actualizar UI
        if (startButton != null) startButton.interactable = false;
        if (stopButton != null) stopButton.interactable = true;

        UpdateStatus("GRABANDO...");
        Debug.Log("Grabación iniciada");
    }

    public void StopRecording()
    {
        if (!isRecording) return;

        // Detener captura y logging
        if (facialCapture != null)
        {
            facialCapture.StopCapture();
        }

        if (dataLogger != null)
        {
            dataLogger.StopLogging();
        }

        // Detener transmisión en tiempo real
        if (realtimeTransmitter != null)
        {
            realtimeTransmitter.StopTransmission();
        }

        isRecording = false;

        // Actualizar UI
        if (startButton != null) startButton.interactable = true;
        if (stopButton != null) stopButton.interactable = false;

        UpdateStatus("Grabación detenida");
        Debug.Log("Grabación detenida");
    }

    void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log("Estado: " + message);
    }

    void OnApplicationQuit()
    {
        if (isRecording)
        {
            StopRecording();
        }
    }

    // Métodos públicos de utilidad
    public bool IsRecording()
    {
        return isRecording;
    }

    public string GetCurrentFilePath()
    {
        if (dataLogger != null)
        {
            return dataLogger.GetFilePath();
        }
        return "";
    }
}