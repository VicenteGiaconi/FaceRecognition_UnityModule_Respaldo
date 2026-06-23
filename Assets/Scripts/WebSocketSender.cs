using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class WebSocketSender : MonoBehaviour
{
    [Header("Configuración WebSocket")]
    public string serverUrl = "ws://10.33.0.137:8010";
    // public string serverUrl = "wss://uandes-rcptraining.onrender.com";
    public string vrName = "VR UANDES";

    [Header("Control de Video")]
    public VideoLibraryManager videoLibrary;

    [Header("Filtro de ruido")]
    public float minimumValueThreshold = 0.001f;

    public string sessionId { get; private set; }
    public bool IsConnected => isConnected;

    public Action<string> OnCommandReceived;
    public Action OnConnectionDropped;
    public Action OnConnectionRestored;
    public Action OnConnectionFailed;

    private ClientWebSocket websocket;
    private CancellationTokenSource cts;
    private bool isConnected = false;

    private List<FacialExpressionCapture.FacialData> sessionData;
    private float sessionStartTime;
    private int totalBlinks;
    private bool isRecordingSession;
    private Dictionary<string, MetricStats> metricsStats;

    private DataLogger dataLogger;

    // Eye tracking session state
    private EyeTrackingCapture eyeCapture;
    private List<EyeTrackingCapture.EyeFrame> eyeData;
    private Dictionary<string, MetricStats> eyeStats;
    private float eyeSessionStartTime;
    private bool isRecordingEyeSession;

    [System.Serializable]
    public class MetricStats
    {
        public float min = float.MaxValue;
        public float max = float.MinValue;
        public float sum = 0;
        public int count = 0;
        public float Average => count > 0 ? sum / count : 0;

        public void AddValue(float value)
        {
            if (value < min) min = value;
            if (value > max) max = value;
            sum += value;
            count++;
        }
    }

    void Start()
    {
        sessionData = new List<FacialExpressionCapture.FacialData>();
        metricsStats = new Dictionary<string, MetricStats>();
        eyeData  = new List<EyeTrackingCapture.EyeFrame>();
        eyeStats = new Dictionary<string, MetricStats>();

        if (videoLibrary == null)
            videoLibrary = FindFirstObjectByType<VideoLibraryManager>();

        eyeCapture = FindFirstObjectByType<EyeTrackingCapture>();
        dataLogger = FindFirstObjectByType<DataLogger>();
    }

    public async void ConnectAsync()
    {
        await ConnectToBaseWebSocket();
    }

    private async Task ConnectToBaseWebSocket()
    {
        cts = new CancellationTokenSource();
        websocket = new ClientWebSocket();

        try
        {
            await websocket.ConnectAsync(new Uri($"{serverUrl}/ws/vr/base/"), cts.Token);
            Debug.Log("[WSSender] Conectado al WebSocket base.");
            await ReceiveBaseMessages();
        }
        catch (Exception e)
        {
            Debug.LogError("[WSSender] Error conectando al WebSocket base: " + e.Message);
        }
    }

    private async Task ReceiveBaseMessages()
    {
        var buffer = new byte[4096];

        while (websocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
                    break;
                }

                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Debug.Log("[WSSender] Mensaje base: " + msg);

                if (msg.Contains("ASSIGNED_SESSION"))
                {
                    var json = JsonUtility.FromJson<SessionIdMessage>(msg);
                    sessionId = json.session_id;
                    Debug.Log("[WSSender] sessionId asignado: " + sessionId);
                    await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
                    websocket.Dispose();
                    await ConnectToSessionWebSocket();
                    break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                Debug.LogError("[WSSender] Error en recepción base: " + e.Message);
                break;
            }
        }
    }

    private async Task ConnectToSessionWebSocket()
    {
        int retryDelay = 2000;
        const int maxRetries = 5;
        int attempt = 0;

        while (attempt <= maxRetries && !cts.IsCancellationRequested)
        {
            if (attempt > 0)
            {
                Debug.LogWarning($"[WSSender] Reconectando sesión {sessionId} (intento {attempt}/{maxRetries}) en {retryDelay / 1000}s...");
                UnityMainThreadDispatcher.Instance().Enqueue(() => OnConnectionDropped?.Invoke());
                try { await Task.Delay(retryDelay, cts.Token); }
                catch (OperationCanceledException) { break; }
                retryDelay = Math.Min(retryDelay * 2, 15000);
            }

            websocket?.Dispose();
            websocket = new ClientWebSocket();

            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                connectCts.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    await websocket.ConnectAsync(new Uri($"{serverUrl}/ws/session/{sessionId}/"), connectCts.Token);
                }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                {
                    throw new Exception("Timeout de conexión (10s)");
                }
                isConnected = true;

                var registerMsg = new VRRegisterMessage { type = "REGISTER", role = "vr", name = vrName, machine_id = SystemInfo.deviceUniqueIdentifier };
                await SendTextAsync(JsonUtility.ToJson(registerMsg));
                Debug.Log($"[WSSender] WebSocket de sesión conectado (intento {attempt + 1}).");

                if (attempt > 0)
                    UnityMainThreadDispatcher.Instance().Enqueue(() => OnConnectionRestored?.Invoke());

                await ReceiveSessionMessages();

                if (cts.IsCancellationRequested) break;

                Debug.LogWarning("[WSSender] Conexión caída, intentando reconectar...");
                attempt++;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                Debug.LogError($"[WSSender] Error conectando (intento {attempt + 1}): {e.Message}");
                isConnected = false;
                attempt++;
            }
        }

        isConnected = false;
        if (attempt > maxRetries)
        {
            Debug.LogError("[WSSender] No se pudo reconectar después de varios intentos.");
            UnityMainThreadDispatcher.Instance().Enqueue(() => OnConnectionFailed?.Invoke());
        }
    }

    private async Task ReceiveSessionMessages()
    {
        var buffer = new byte[4096];

        while (websocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await websocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
                    break;
                }

                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Debug.Log("[WSSender] Mensaje sesión: " + msg);
                ProcessMessage(msg);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                Debug.LogError("[WSSender] Error en recepción sesión: " + e.Message);
                break;
            }
        }

        isConnected = false;
        Debug.LogWarning("[WSSender] WebSocket de sesión cerrado.");
    }

    private async Task SendTextAsync(string text)
    {
        if (websocket == null || websocket.State != WebSocketState.Open) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await websocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                cts?.Token ?? CancellationToken.None);
        }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning("[WSSender] Error al enviar: " + e.Message);
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            var msg = JsonUtility.FromJson<IncomingMessage>(message);
            if (msg.type == "START_RECORDING" || msg.type == "STOP_RECORDING")
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    OnCommandReceived?.Invoke(msg.type));
            }
            else if (msg.type == "VIDEO_PLAY")
            {
                var videoMsg = JsonUtility.FromJson<VideoPlayMessage>(message);
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                    videoLibrary?.ChangeVideoPublic(videoMsg.name));
            }
            else if (msg.type == "VIDEO_LIST_REQUEST")
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    string response;
                    if (videoLibrary != null)
                    {
                        string listJson = videoLibrary.GetVideoListJSON();
                        response = "{\"type\":\"VIDEO_LIST\"," + listJson.Substring(1);
                    }
                    else
                    {
                        response = "{\"type\":\"VIDEO_LIST\",\"status\":\"ok\",\"videos\":[],\"current\":\"\"}";
                    }
                    _ = SendTextAsync(response);
                });
            }
        }
        catch (Exception)
        {
            Debug.Log("[WSSender] Mensaje no reconocido: " + message);
        }
    }

    // --- Session management ---

    public void StartSession()
    {
        sessionData.Clear();
        metricsStats.Clear();
        sessionStartTime = Time.time;
        totalBlinks = 0;
        isRecordingSession = true;
        Debug.Log("[WSSender] Sesión iniciada.");
    }

    public void RecordData(FacialExpressionCapture.FacialData data)
    {
        if (!isRecordingSession) return;

        sessionData.Add(data);
        CalculateAndRecordMetrics(data);

        // Sample eye at the same rate as facial (avoids concurrent sends)
        EyeTrackingCapture.EyeFrame eyeFrame = default;
        if (isRecordingEyeSession && eyeCapture != null)
        {
            eyeFrame = eyeCapture.Sample();
            if (eyeFrame.valid)
            {
                eyeData.Add(eyeFrame);
                RecordEyeMetric("gaze_x",     eyeFrame.gazeX);
                RecordEyeMetric("gaze_y",     eyeFrame.gazeY);
                RecordEyeMetric("confidence", (eyeFrame.leftConfidence + eyeFrame.rightConfidence) * 0.5f);
                RecordEyeMetric("dist",       eyeFrame.convergenceDist);
            }
        }

        dataLogger?.LogCombinedData(data, eyeFrame);

        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open) return;

        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"FACIAL_RT\",\"t\":");
        sb.Append(data.timestamp.ToString("F3", ic));
        sb.Append(",\"d\":{");
        bool first = true;
        foreach (var kvp in data.expressions)
        {
            if (kvp.Value < minimumValueThreshold) continue;
            if (!first) sb.Append(",");
            sb.Append("\"");
            sb.Append((int)kvp.Key);
            sb.Append("\":");
            sb.Append(kvp.Value.ToString("F3", ic));
            first = false;
        }
        sb.Append("}}");

        _ = SendRealTimeAsync(sb.ToString(), eyeFrame);
    }

    private async Task SendRealTimeAsync(string facialRt, EyeTrackingCapture.EyeFrame eye)
    {
        await SendTextAsync(facialRt);
        if (!eye.valid) return;
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"EYE_RT\",");
        sb.Append("\"t\":" + eye.timestamp.ToString("F3", ic) + ",");
        sb.Append("\"gx\":" + eye.gazeX.ToString("F3", ic) + ",");
        sb.Append("\"gy\":" + eye.gazeY.ToString("F3", ic) + ",");
        sb.Append("\"lc\":" + eye.leftConfidence.ToString("F3", ic) + ",");
        sb.Append("\"rc\":" + eye.rightConfidence.ToString("F3", ic) + ",");
        sb.Append("\"lgx\":" + eye.leftGazeX.ToString("F3", ic) + ",");
        sb.Append("\"lgy\":" + eye.leftGazeY.ToString("F3", ic) + ",");
        sb.Append("\"rgx\":" + eye.rightGazeX.ToString("F3", ic) + ",");
        sb.Append("\"rgy\":" + eye.rightGazeY.ToString("F3", ic) + ",");
        sb.Append("\"dist\":" + eye.convergenceDist.ToString("F2", ic) + "}");
        await SendTextAsync(sb.ToString());
    }

    public void EndSessionAndSend()
    {
        if (!isRecordingSession)
        {
            Debug.LogWarning("[WSSender] No hay sesión activa para enviar.");
            return;
        }

        isRecordingSession = false;
        float duration = Time.time - sessionStartTime;
        Debug.Log($"[WSSender] Finalizando sesión. Duración: {duration:F1}s, Puntos: {sessionData.Count}");

        string summary = BuildSessionSummary(duration);
        SaveSummaryToFile(summary, "facial");

        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("[WSSender] No conectado. Resumen guardado localmente.");
            return;
        }

        _ = SendTextAsync(summary);
        Debug.Log("[WSSender] Resumen de sesión enviado.");
    }

    // --- Metrics helpers ---

    private void CalculateAndRecordMetrics(FacialExpressionCapture.FacialData data)
    {
        RecordMetric("attention", CalculateAttention(data));
        RecordMetric("stress", CalculateStress(data));
        RecordMetric("mouth_activity", CalculateMouthActivity(data));
        if (DetectBlink(data)) totalBlinks++;
    }

    private void RecordMetric(string name, float value)
    {
        if (!metricsStats.ContainsKey(name)) metricsStats[name] = new MetricStats();
        metricsStats[name].AddValue(value);
    }

    private float CalculateAttention(FacialExpressionCapture.FacialData data)
    {
        int[] ids = { 14, 15, 20, 21 };
        float sum = 0; int count = 0;
        foreach (int id in ids)
            if (data.expressions.TryGetValue((OVRFaceExpressions.FaceExpression)id, out float v)) { sum += v; count++; }
        return count > 0 ? 1.0f - (sum / count) : 1.0f;
    }

    private float CalculateStress(FacialExpressionCapture.FacialData data)
    {
        int[] ids = { 0, 1, 22, 23 };
        float sum = 0; int count = 0;
        foreach (int id in ids)
            if (data.expressions.TryGetValue((OVRFaceExpressions.FaceExpression)id, out float v)) { sum += v; count++; }
        return count > 0 ? sum / count : 0;
    }

    private float CalculateMouthActivity(FacialExpressionCapture.FacialData data)
    {
        int[] ids = { 24, 32, 33, 42, 43 };
        float sum = 0; int count = 0;
        foreach (int id in ids)
            if (data.expressions.TryGetValue((OVRFaceExpressions.FaceExpression)id, out float v)) { sum += v; count++; }
        return count > 0 ? sum / count : 0;
    }

    private bool DetectBlink(FacialExpressionCapture.FacialData data)
    {
        int[] ids = { 12, 13 };
        float sum = 0; int count = 0;
        foreach (int id in ids)
            if (data.expressions.TryGetValue((OVRFaceExpressions.FaceExpression)id, out float v)) { sum += v; count++; }
        return count > 0 && (sum / count) > 0.7f;
    }

    private string BuildSessionSummary(float duration)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        StringBuilder json = new StringBuilder();
        json.Append("{\"type\":\"FACIAL_SUMMARY\",");
        json.Append("\"metadata\":{");
        json.Append($"\"timestamp\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        json.Append("\"duration\":");
        json.Append(duration.ToString("F2", ic));
        json.Append(",\"dataPoints\":");
        json.Append(sessionData.Count);
        json.Append(",\"totalBlinks\":");
        json.Append(totalBlinks);
        json.Append("},");
        json.Append("\"statistics\":{");
        bool firstMetric = true;
        foreach (var kvp in metricsStats)
        {
            if (!firstMetric) json.Append(",");
            json.Append("\"");
            json.Append(kvp.Key);
            json.Append("\":{\"min\":");
            json.Append(kvp.Value.min.ToString("F3", ic));
            json.Append(",\"max\":");
            json.Append(kvp.Value.max.ToString("F3", ic));
            json.Append(",\"avg\":");
            json.Append(kvp.Value.Average.ToString("F3", ic));
            json.Append("}");
            firstMetric = false;
        }
        json.Append("},");
        json.Append("\"rawData\":[");
        int startIdx = Mathf.Max(0, sessionData.Count - 1000);
        for (int i = startIdx; i < sessionData.Count; i++)
        {
            if (i > startIdx) json.Append(",");
            json.Append("{\"t\":");
            json.Append(sessionData[i].timestamp.ToString("F3", ic));
            json.Append(",\"e\":{");
            bool firstExp = true;
            foreach (var exp in sessionData[i].expressions)
            {
                if (!firstExp) json.Append(",");
                json.Append("\"");
                json.Append((int)exp.Key);
                json.Append("\":");
                json.Append(exp.Value.ToString("F3", ic));
                firstExp = false;
            }
            json.Append("}}");
        }
        json.Append("]}");
        return json.ToString();
    }

    // --- Eye tracking session ---

    public void StartEyeSession()
    {
        eyeData.Clear();
        eyeStats.Clear();
        eyeSessionStartTime = Time.time;
        isRecordingEyeSession = true;
        Debug.Log("[WSSender] Sesión ocular iniciada.");
    }

    public void EndEyeSessionAndSend()
    {
        if (!isRecordingEyeSession)
        {
            Debug.LogWarning("[WSSender] No hay sesión ocular activa para enviar.");
            return;
        }

        isRecordingEyeSession = false;
        float duration = Time.time - eyeSessionStartTime;
        Debug.Log($"[WSSender] Finalizando sesión ocular. Duración: {duration:F1}s, Puntos: {eyeData.Count}");

        string summary = BuildEyeSummary(duration);
        SaveSummaryToFile(summary, "eye");

        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("[WSSender] No conectado. Resumen ocular guardado localmente.");
            return;
        }

        _ = SendTextAsync(summary);
        Debug.Log("[WSSender] Resumen ocular enviado.");
    }

    private void RecordEyeMetric(string name, float value)
    {
        if (!eyeStats.ContainsKey(name)) eyeStats[name] = new MetricStats();
        eyeStats[name].AddValue(value);
    }

    private string BuildEyeSummary(float duration)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"EYE_SUMMARY\",");
        sb.Append("\"metadata\":{");
        sb.Append($"\"timestamp\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        sb.Append("\"duration\":");
        sb.Append(duration.ToString("F2", ic));
        sb.Append(",\"dataPoints\":");
        sb.Append(eyeData.Count);
        sb.Append("},");
        sb.Append("\"statistics\":{");
        bool firstMetric = true;
        foreach (var kvp in eyeStats)
        {
            if (!firstMetric) sb.Append(",");
            sb.Append("\"");
            sb.Append(kvp.Key);
            sb.Append("\":{\"min\":");
            sb.Append(kvp.Value.min.ToString("F3", ic));
            sb.Append(",\"max\":");
            sb.Append(kvp.Value.max.ToString("F3", ic));
            sb.Append(",\"avg\":");
            sb.Append(kvp.Value.Average.ToString("F3", ic));
            sb.Append("}");
            firstMetric = false;
        }
        sb.Append("},\"rawData\":[");
        int startIdx = Mathf.Max(0, eyeData.Count - 1000);
        for (int i = startIdx; i < eyeData.Count; i++)
        {
            if (i > startIdx) sb.Append(",");
            sb.Append("{\"t\":");
            sb.Append(eyeData[i].timestamp.ToString("F3", ic));
            sb.Append(",\"gx\":");
            sb.Append(eyeData[i].gazeX.ToString("F3", ic));
            sb.Append(",\"gy\":");
            sb.Append(eyeData[i].gazeY.ToString("F3", ic));
            sb.Append(",\"dist\":");
            sb.Append(eyeData[i].convergenceDist.ToString("F2", ic));
            sb.Append("}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private void SaveSummaryToFile(string json, string prefix)
    {
        try
        {
            string fileName = $"{prefix}_summary_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllText(path, json);
            Debug.Log($"[WSSender] Resumen guardado en: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError("[WSSender] Error guardando resumen local: " + e.Message);
        }
    }

    private async void OnApplicationQuit()
    {
        cts?.Cancel();
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            try { await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
        websocket?.Dispose();
    }

    [Serializable]
    private class SessionIdMessage { public string type; public string session_id; public string session_ws_path; }

    [Serializable]
    private class VRRegisterMessage { public string type; public string role; public string name; public string machine_id; }

    [Serializable]
    private class IncomingMessage { public string type; }

    [Serializable]
    private class VideoPlayMessage { public string type; public string name; }
}
