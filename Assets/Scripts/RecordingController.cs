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
    public EyeTrackingCapture eyeCapture;
    public VideoLibraryManager videoLibrary;

    [Header("UI (opcional)")]
    public TextMeshProUGUI statusText;
    public Button startButton;
    public Button stopButton;

    private bool isRecording = false;
    private float lastTrackingWarnTime = -999f;

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
        if (eyeCapture == null)
            eyeCapture = FindFirstObjectByType<EyeTrackingCapture>();
        if (videoLibrary == null)
            videoLibrary = FindFirstObjectByType<VideoLibraryManager>();

        if (webSocketSender != null)
        {
            webSocketSender.OnCommandReceived    += HandleRemoteCommand;
            webSocketSender.OnConnectionDropped  += OnWsDropped;
            webSocketSender.OnConnectionRestored += OnWsRestored;
            webSocketSender.OnConnectionFailed   += OnWsFailed;
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
        if (isRecording && facialCapture != null && !facialCapture.IsFaceTrackingEnabled())
        {
            if (statusText != null) statusText.text = "ADVERTENCIA: Tracking facial perdido";
            if (Time.time - lastTrackingWarnTime > 3f)
            {
                Debug.LogWarning("[RecCtrl] Tracking facial perdido");
                lastTrackingWarnTime = Time.time;
            }
        }

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
        eyeCapture?.StartCapture();
        realtimeTransmitter?.StartTransmission();
        webSocketSender?.StartSession();
        webSocketSender?.StartEyeSession();
        videoLibrary?.PlayCurrentVideo();

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
        eyeCapture?.StopCapture();
        dataLogger?.StopLogging();
        realtimeTransmitter?.StopTransmission();
        webSocketSender?.EndSessionAndSend();
        webSocketSender?.EndEyeSessionAndSend();
        videoLibrary?.StopCurrentVideo();

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

    private void OnWsDropped()
    {
        if (isRecording)
            UpdateStatus("ADVERTENCIA: Conexión perdida. Reconectando...");
    }

    private void OnWsRestored()
    {
        if (isRecording)
            UpdateStatus("GRABANDO...");
    }

    private void OnWsFailed()
    {
        if (isRecording)
            UpdateStatus("ERROR: Sin conexión. Usa B/Y para detener y guardar CSV.");
    }

    void OnApplicationQuit() { if (isRecording) StopRecording(); }
}