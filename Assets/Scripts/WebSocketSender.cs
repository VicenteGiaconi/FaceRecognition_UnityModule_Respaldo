using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.IO;
public class WebSocketSender : MonoBehaviour
{
    [Header("Configuración WebSocket")]
    public string serverIP = "10.33.8.179";
    public int serverPort = 8765;
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
        if (PlayerPrefs.HasKey("ServerIP"))
        {
            serverIP = PlayerPrefs.GetString("ServerIP");
        }
        Debug.Log($"WebSocketSender configurado para: {serverIP}:{serverPort}");
    }
    public void StartSession()
    {
        sessionData.Clear();
        metricsStats.Clear();
        sessionStartTime = Time.time;
        totalBlinks = 0;
        isRecordingSession = true;
        Debug.Log("Sesión WebSocket iniciada");
    }
    public void RecordData(FacialExpressionCapture.FacialData data)
    {
        if (!isRecordingSession) return;
        sessionData.Add(data);
        CalculateAndRecordMetrics(data);
    }
    private void CalculateAndRecordMetrics(FacialExpressionCapture.FacialData data)
    {
        float attention = CalculateAttention(data);
        RecordMetric("attention", attention);
        float stress = CalculateStress(data);
        RecordMetric("stress", stress);
        float mouthActivity = CalculateMouthActivity(data);
        RecordMetric("mouth_activity", mouthActivity);
        if (DetectBlink(data))
        {
            totalBlinks++;
        }
    }
    private void RecordMetric(string metricName, float value)
    {
        if (!metricsStats.ContainsKey(metricName))
        {
            metricsStats[metricName] = new MetricStats();
        }
        metricsStats[metricName].AddValue(value);
    }
    private float CalculateAttention(FacialExpressionCapture.FacialData data)
    {
        int[] attentionExpressions = { 14, 15, 20, 21 };
        float sum = 0;
        int count = 0;
        foreach (int expId in attentionExpressions)
        {
            if (data.expressions.ContainsKey((OVRFaceExpressions.FaceExpression)expId))
            {
                sum += data.expressions[(OVRFaceExpressions.FaceExpression)expId];
                count++;
            }
        }
        return count > 0 ? 1.0f - (sum / count) : 1.0f;
    }
    private float CalculateStress(FacialExpressionCapture.FacialData data)
    {
        // Basado en tensión de cejas
        int[] stressExpressions = { 0, 1, 22, 23 };
        float sum = 0;
        int count = 0;
        foreach (int expId in stressExpressions)
        {
            if (data.expressions.ContainsKey((OVRFaceExpressions.FaceExpression)expId))
            {
                sum += data.expressions[(OVRFaceExpressions.FaceExpression)expId];
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }
    private float CalculateMouthActivity(FacialExpressionCapture.FacialData data)
    {
        int[] mouthExpressions = { 24, 32, 33, 42, 43 }; 
        float sum = 0;
        int count = 0;
        foreach (int expId in mouthExpressions)
        {
            if (data.expressions.ContainsKey((OVRFaceExpressions.FaceExpression)expId))
            {
                sum += data.expressions[(OVRFaceExpressions.FaceExpression)expId];
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }
    private bool DetectBlink(FacialExpressionCapture.FacialData data)
    {
        int[] blinkExpressions = { 12, 13 }; 
        float sum = 0;
        int count = 0;
        foreach (int expId in blinkExpressions)
        {
            if (data.expressions.ContainsKey((OVRFaceExpressions.FaceExpression)expId))
            {
                sum += data.expressions[(OVRFaceExpressions.FaceExpression)expId];
                count++;
            }
        }
        return count > 0 && (sum / count) > 0.7f;
    }
    public void EndSessionAndSend()
    {
        if (!isRecordingSession)
        {
            Debug.LogWarning("No hay sesión activa para enviar");
            return;
        }
        isRecordingSession = false;
        float sessionDuration = Time.time - sessionStartTime;
        Debug.Log($"Finalizando sesión. Duración: {sessionDuration}s, Datos: {sessionData.Count} puntos");
        string jsonData = BuildSessionSummary(sessionDuration);
        System.Threading.Thread sendThread = new System.Threading.Thread(() =>
        {
            SendDataToServerSync(jsonData);
        });
        sendThread.IsBackground = true;
        sendThread.Start();
    }
    private string BuildSessionSummary(float duration)
    {
        StringBuilder json = new StringBuilder();
        json.Append("{");
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
            json.Append("{");
            json.Append($"\"t\":{sessionData[i].timestamp:F3},");
            json.Append("\"e\":{");
            bool firstExp = true;
            foreach (var exp in sessionData[i].expressions)
            {
                if (!firstExp) json.Append(",");
                json.Append($"\"{(int)exp.Key}\":{exp.Value:F3}");
                firstExp = false;
            }
            json.Append("}}");
        }
        json.Append("]");
        json.Append("}");
        return json.ToString();
    }
    private void SendDataToServerSync(string data)
    {
        try
        {
            Debug.Log($"[WebSocket] Iniciando envío a {serverIP}:{serverPort}");
            Debug.Log($"[WebSocket] Tamaño de datos: {data.Length} caracteres");
            TcpClient client = new TcpClient();
            Debug.Log("[WebSocket] Cliente TCP creado, intentando conectar...");
            var result = client.BeginConnect(serverIP, serverPort, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            if (!success)
            {
                Debug.LogError("[WebSocket] Timeout (5s) conectando al servidor");
                Debug.LogError($"[WebSocket] Verifica que el servidor Python esté ejecutándose en {serverIP}:{serverPort}");
                client.Close();
                return;
            }
            client.EndConnect(result);
            Debug.Log("[WebSocket] Conexión establecida exitosamente");
            NetworkStream stream = client.GetStream();
            byte[] buffer = Encoding.UTF8.GetBytes(data);
            Debug.Log($"[WebSocket] Enviando {buffer.Length} bytes...");
            stream.Write(buffer, 0, buffer.Length);
            stream.Flush();
            Debug.Log($"[WebSocket] ✓ Datos enviados exitosamente: {buffer.Length} bytes");
            stream.Close();
            client.Close();
            Debug.Log("[WebSocket] Conexión cerrada correctamente");
        }
        catch (SocketException e)
        {
            Debug.LogError($"[WebSocket] SocketException: {e.Message}");
            Debug.LogError($"[WebSocket] ErrorCode: {e.ErrorCode}");
            Debug.LogError($"[WebSocket] Verifica que:");
            Debug.LogError($"[WebSocket]   1. La IP sea correcta: {serverIP}");
            Debug.LogError($"[WebSocket]   2. El servidor Python esté ejecutándose");
            Debug.LogError($"[WebSocket]   3. No haya firewall bloqueando el puerto {serverPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[WebSocket] Error general: {e.GetType().Name}");
            Debug.LogError($"[WebSocket] Mensaje: {e.Message}");
            Debug.LogError($"[WebSocket] Stack: {e.StackTrace}");
        }
    }
    public void SetServerIP(string ip)
    {
        serverIP = ip;
        PlayerPrefs.SetString("ServerIP", ip);
        PlayerPrefs.Save();
        Debug.Log($"IP del servidor actualizada: {ip}");
    }
}

