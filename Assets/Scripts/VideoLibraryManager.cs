using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System;

public class VideoLibraryManager : MonoBehaviour
{
    [Header("Configuración de Red")]
    public string serverIP = "10.33.8.179";
    public int videoControlPort = 8766; // Puerto diferente al WebSocket

    [Header("Referencias")]
    public VideoPlayer videoPlayer;
    public Video360Manager videoManager;

    private List<string> availableVideos = new List<string>();
    private string currentVideo = "";
    private System.Threading.Thread listenerThread;
    private bool isListening = true;

    void Start()
    {
        Debug.Log("[VideoLibrary] Iniciando gestor de biblioteca de videos");

        // Obtener referencias
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
        }

        if (videoManager == null)
        {
            videoManager = GetComponent<Video360Manager>();
        }

        // Escanear videos disponibles
        ScanAvailableVideos();

        // Iniciar listener para comandos remotos
        StartCommandListener();
    }

    void ScanAvailableVideos()
    {
        Debug.Log("[VideoLibrary] Escaneando videos en StreamingAssets...");

        availableVideos.Clear();

        string streamingAssetsPath = Application.streamingAssetsPath;

        // En Android, StreamingAssets está dentro del APK
        // Necesitamos una lista predefinida o usar una convención de nombres

        // Para simplicidad, buscaremos archivos .mp4 conocidos
        // En producción, podrías tener un archivo index.txt con la lista

        string[] knownVideos = {
            "VALDIVIA1_video.mp4",
            "AK_video.mp4",
            // Agrega aquí los nombres de tus videos
        };

        foreach (string videoName in knownVideos)
        {
            string fullPath = Path.Combine(streamingAssetsPath, videoName);

            // En Android, File.Exists no funciona con APK
            // Asumimos que existen si están en la lista
            availableVideos.Add(videoName);
            Debug.Log($"[VideoLibrary] Video encontrado: {videoName}");
        }

        if (availableVideos.Count > 0)
        {
            currentVideo = availableVideos[0];
            Debug.Log($"[VideoLibrary] Total videos disponibles: {availableVideos.Count}");
            Debug.Log($"[VideoLibrary] Video actual: {currentVideo}");
        }
        else
        {
            Debug.LogWarning("[VideoLibrary] No se encontraron videos");
        }
    }

    void StartCommandListener()
    {
        Debug.Log($"[VideoLibrary] Iniciando listener en puerto {videoControlPort}");

        listenerThread = new System.Threading.Thread(() =>
        {
            ListenForCommands();
        });
        listenerThread.IsBackground = true;
        listenerThread.Start();
    }

    void ListenForCommands()
    {
        TcpListener listener = null;

        try
        {
            listener = new TcpListener(System.Net.IPAddress.Any, videoControlPort);
            listener.Start();

            Debug.Log($"[VideoLibrary] Escuchando comandos en puerto {videoControlPort}");

            while (isListening)
            {
                if (listener.Pending())
                {
                    TcpClient client = listener.AcceptTcpClient();
                    HandleClient(client);
                }
                else
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[VideoLibrary] Error en listener: {e.Message}");
        }
        finally
        {
            if (listener != null)
            {
                listener.Stop();
            }
        }
    }

    void HandleClient(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            Debug.Log($"[VideoLibrary] Comando recibido: {command}");

            string response = ProcessCommand(command);

            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            stream.Write(responseBytes, 0, responseBytes.Length);

            stream.Close();
            client.Close();
        }
        catch (Exception e)
        {
            Debug.LogError($"[VideoLibrary] Error procesando cliente: {e.Message}");
        }
    }

    string ProcessCommand(string command)
    {
        try
        {
            // Formato de comandos:
            // LIST - Listar videos disponibles
            // PLAY:nombre_video.mp4 - Reproducir video específico
            // CURRENT - Obtener video actual

            if (command == "LIST")
            {
                return GetVideoListJSON();
            }
            else if (command.StartsWith("PLAY:"))
            {
                string videoName = command.Substring(5);
                return ChangeVideo(videoName);
            }
            else if (command == "CURRENT")
            {
                return $"{{\"status\":\"ok\",\"current\":\"{currentVideo}\"}}";
            }
            else
            {
                return "{\"status\":\"error\",\"message\":\"Unknown command\"}";
            }
        }
        catch (Exception e)
        {
            return $"{{\"status\":\"error\",\"message\":\"{e.Message}\"}}";
        }
    }

    string GetVideoListJSON()
    {
        StringBuilder json = new StringBuilder();
        json.Append("{\"status\":\"ok\",\"videos\":[");

        for (int i = 0; i < availableVideos.Count; i++)
        {
            if (i > 0) json.Append(",");
            json.Append($"\"{availableVideos[i]}\"");
        }

        json.Append($"],\"current\":\"{currentVideo}\"}}");

        return json.ToString();
    }

    string ChangeVideo(string videoName)
    {
        if (!availableVideos.Contains(videoName))
        {
            return $"{{\"status\":\"error\",\"message\":\"Video '{videoName}' no encontrado\"}}";
        }

        // Cambiar video en el hilo principal de Unity
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            ChangeVideoOnMainThread(videoName);
        });

        return $"{{\"status\":\"ok\",\"message\":\"Cambiando a '{videoName}'\"}}";
    }

    void ChangeVideoOnMainThread(string videoName)
    {
        Debug.Log($"[VideoLibrary] Cambiando a video: {videoName}");

        currentVideo = videoName;

        if (videoPlayer != null)
        {
            videoPlayer.Stop();

            string videoPath = Path.Combine(Application.streamingAssetsPath, videoName);
            videoPlayer.url = videoPath;

            videoPlayer.Prepare();

            StartCoroutine(PlayWhenReady());
        }
        else
        {
            Debug.LogError("[VideoLibrary] VideoPlayer no disponible");
        }
    }

    IEnumerator PlayWhenReady()
    {
        while (!videoPlayer.isPrepared)
        {
            yield return new WaitForSeconds(0.1f);
        }

        videoPlayer.Play();
        Debug.Log($"[VideoLibrary] Reproduciendo: {currentVideo}");
    }

    void OnDestroy()
    {
        isListening = false;

        if (listenerThread != null && listenerThread.IsAlive)
        {
            listenerThread.Join(1000);
        }
    }

    // Métodos públicos para control manual
    public void NextVideo()
    {
        int currentIndex = availableVideos.IndexOf(currentVideo);
        int nextIndex = (currentIndex + 1) % availableVideos.Count;
        ChangeVideoOnMainThread(availableVideos[nextIndex]);
    }

    public void PreviousVideo()
    {
        int currentIndex = availableVideos.IndexOf(currentVideo);
        int prevIndex = (currentIndex - 1 + availableVideos.Count) % availableVideos.Count;
        ChangeVideoOnMainThread(availableVideos[prevIndex]);
    }
}

// Dispatcher para ejecutar código en el hilo principal de Unity
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance = null;
    private readonly Queue<Action> _executionQueue = new Queue<Action>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            GameObject go = new GameObject("UnityMainThreadDispatcher");
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }
}