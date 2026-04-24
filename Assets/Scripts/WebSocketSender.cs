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
    public string serverUrl = "ws://10.33.9.230:8010";
    // public string serverUrl = "wss://uandes-rcptraining.onrender.com";
    public string vrName = "VR UANDES";

    [Header("Filtro de ruido")]
    public float minimumValueThreshold = 0.01f;

    public string sessionId { get; private set; }
    public bool IsConnected => isConnected;

    public Action<string> OnCommandReceived;

    private ClientWebSocket websocket;
    private CancellationTokenSource cts;
    private bool isConnected = false;

    private List<FacialExpressionCapture.FacialData> sessionData;
    private float sessionStartTime;
    private int totalBlinks;
    private bool isRecordingSession;
    private Dictionary<string, MetricStats> metricsStats;

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
        websocket = new ClientWebSocket();

        try
        {
            await websocket.ConnectAsync(new Uri($"{serverUrl}/ws/session/{sessionId}/"), cts.Token);
            Debug.Log("[WSSender] WebSocket de sesión conectado.");
            isConnected = true;

            var registerMsg = new VRRegisterMessage { type = "REGISTER", role = "vr", name = vrName };
            await SendTextAsync(JsonUtility.ToJson(registerMsg));
            Debug.Log("[WSSender] Mensaje REGISTER enviado.");

            await ReceiveSessionMessages();
        }
        catch (Exception e)
        {
            Debug.LogError("[WSSender] Error conectando al WebSocket de sesión: " + e.Message);
            isConnected = false;
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
        var bytes = Encoding.UTF8.GetBytes(text);
        await websocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
            cts?.Token ?? CancellationToken.None);
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

        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open) return;

        StringBuilder sb = new StringBuilder();
        sb.Append("{\"type\":\"FACIAL_RT\",\"t\":");
        sb.Append(data.timestamp.ToString("F3"));
        sb.Append(",\"d\":{");
        bool first = true;
        foreach (var kvp in data.expressions)
        {
            if (kvp.Value < minimumValueThreshold) continue;
            if (!first) sb.Append(",");
            sb.Append($"\"{(int)kvp.Key}\":{kvp.Value:F3}");
            first = false;
        }
        sb.Append("}}");

        _ = SendTextAsync(sb.ToString());
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

        if (!isConnected || websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("[WSSender] No conectado, no se puede enviar resumen.");
            return;
        }

        _ = SendTextAsync(BuildSessionSummary(duration));
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
        StringBuilder json = new StringBuilder();
        json.Append("{\"type\":\"FACIAL_SUMMARY\",");
        json.Append("\"metadata\":{");
        json.Append($"\"timestamp\":\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        json.Append($"\"duration\":{duration:F2},");
        json.Append($"\"dataPoints\":{sessionData.Count},");
        json.Append($"\"totalBlinks\":{totalBlinks}");
        json.Append("},");
        json.Append("\"statistics\":{");
        bool firstMetric = true;
        foreach (var kvp in metricsStats)
        {
            if (!firstMetric) json.Append(",");
            json.Append($"\"{kvp.Key}\":{{");
            json.Append($"\"min\":{kvp.Value.min:F3},");
            json.Append($"\"max\":{kvp.Value.max:F3},");
            json.Append($"\"avg\":{kvp.Value.Average:F3}");
            json.Append("}");
            firstMetric = false;
        }
        json.Append("},");
        json.Append("\"rawData\":[");
        int startIdx = Mathf.Max(0, sessionData.Count - 1000);
        for (int i = startIdx; i < sessionData.Count; i++)
        {
            if (i > startIdx) json.Append(",");
            json.Append($"{{\"t\":{sessionData[i].timestamp:F3},\"e\":{{");
            bool firstExp = true;
            foreach (var exp in sessionData[i].expressions)
            {
                if (!firstExp) json.Append(",");
                json.Append($"\"{(int)exp.Key}\":{exp.Value:F3}");
                firstExp = false;
            }
            json.Append("}}");
        }
        json.Append("]}");
        return json.ToString();
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
    private class VRRegisterMessage { public string type; public string role; public string name; }

    [Serializable]
    private class IncomingMessage { public string type; }
}
