using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RecordingController : MonoBehaviour
{
    [Header("Módulos de medición")]
    public FacialExpressionCapture facialCapture;
    public DataLogger dataLogger;
    public RealtimeDataTransmitter realtimeTransmitter;
    public WebSocketSender webSocketSender;

    [Header("UI (opcional)")]
    public TextMeshProUGUI statusText;
    public Button startButton;
    public Button stopButton;

    [Header("Control por botones VR")]
    public OVRInput.Button startRecordingButton = OVRInput.Button.One; // Botón A/X
    public OVRInput.Button stopRecordingButton  = OVRInput.Button.Two; // Botón B/Y

    private bool isRecording = false;

    void Start()
    {
        if (facialCapture == null)
            facialCapture = FindFirstObjectByType<FacialExpressionCapture>();
        if (dataLogger == null)
            dataLogger = FindFirstObjectByType<DataLogger>();
        if (realtimeTransmitter == null)
            realtimeTransmitter = FindFirstObjectByType<RealtimeDataTransmitter>();
        if (webSocketSender == null)
            webSocketSender = FindFirstObjectByType<WebSocketSender>();

        if (webSocketSender != null)
        {
            webSocketSender.OnCommandReceived += HandleRemoteCommand;
            webSocketSender.ConnectAsync();
        }

        if (startButton != null) startButton.onClick.AddListener(StartRecording);
        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopRecording);
            stopButton.interactable = false;
        }

        UpdateStatus("Listo para grabar");
    }

    void Update()
    {
        if (OVRInput.GetDown(startRecordingButton) && !isRecording)
            StartRecording();

        if (OVRInput.GetDown(stopRecordingButton) && isRecording)
            StopRecording();

        if (isRecording && facialCapture != null && !facialCapture.IsFaceTrackingEnabled())
            UpdateStatus("ADVERTENCIA: Tracking facial perdido");
    }

    public void StartRecording()
    {
        if (isRecording) return;

        if (facialCapture == null || dataLogger == null)
        {
            Debug.LogError("[RecCtrl] Módulos no configurados.");
            UpdateStatus("Error: módulos faltantes");
            return;
        }

        if (!facialCapture.IsFaceTrackingEnabled())
        {
            Debug.LogWarning("[RecCtrl] Tracking facial no disponible.");
            UpdateStatus("Error: tracking facial no disponible");
            return;
        }

        dataLogger.StartLogging();
        facialCapture.StartCapture();
        realtimeTransmitter?.StartTransmission();
        webSocketSender?.StartSession();

        isRecording = true;

        if (startButton != null) startButton.interactable = false;
        if (stopButton != null)  stopButton.interactable  = true;

        UpdateStatus("GRABANDO...");
        Debug.Log("[RecCtrl] Grabación iniciada.");
    }

    public void StopRecording()
    {
        if (!isRecording) return;

        facialCapture?.StopCapture();
        dataLogger?.StopLogging();
        realtimeTransmitter?.StopTransmission();
        webSocketSender?.EndSessionAndSend();

        isRecording = false;

        if (startButton != null) startButton.interactable = true;
        if (stopButton != null)  stopButton.interactable  = false;

        UpdateStatus("Grabación detenida.");
        Debug.Log("[RecCtrl] Grabación detenida.");
    }

    private void HandleRemoteCommand(string command)
    {
        if (command == "START_RECORDING" && !isRecording)
        {
            Debug.Log("[RecCtrl] Comando START_RECORDING recibido del backend.");
            StartRecording();
        }
        else if (command == "STOP_RECORDING" && isRecording)
        {
            Debug.Log("[RecCtrl] Comando STOP_RECORDING recibido del backend.");
            StopRecording();
        }
    }

    void UpdateStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log($"[RecCtrl] Estado: {message}");
    }

    public bool IsRecording() => isRecording;
    public string GetCurrentFilePath() => dataLogger != null ? dataLogger.GetFilePath() : "";

    void OnApplicationQuit() { if (isRecording) StopRecording(); }
}