using UnityEngine;
using System.IO;
using System.Collections;

public class ADBCommandReceiver : MonoBehaviour
{
    [Tooltip("Cada cuántos segundos revisar si hay un comando nuevo")]
    public float pollInterval = 0.5f;

    private string commandFilePath;
    private VideoLibraryManager videoLibrary;

    void Start()
    {
        commandFilePath = Path.Combine(Application.persistentDataPath, "quest_cmd.txt");

        // Buscar primero en el mismo GameObject, luego en toda la escena
        videoLibrary = GetComponent<VideoLibraryManager>();
        if (videoLibrary == null)
            videoLibrary = FindFirstObjectByType<VideoLibraryManager>();

        if (videoLibrary == null)
            Debug.LogError("[ADBCmd] VideoLibraryManager no encontrado en la escena.");
        else
            Debug.Log("[ADBCmd] VideoLibraryManager encontrado.");

        Debug.Log($"[ADBCmd] Polling iniciado. Leyendo: {commandFilePath}");
        StartCoroutine(PollCommandFile());
    }

    IEnumerator PollCommandFile()
    {
        while (true)
        {
            yield return new WaitForSeconds(pollInterval);

            if (!File.Exists(commandFilePath))
                continue;

            string content = "";
            try
            {
                content = File.ReadAllText(commandFilePath).Trim();
                File.Delete(commandFilePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ADBCmd] Error leyendo archivo: {e.Message}");
                continue;
            }

            if (string.IsNullOrEmpty(content))
                continue;

            Debug.Log($"[ADBCmd] Comando recibido: {content}");
            ProcessCommand(content);
        }
    }

    void ProcessCommand(string raw)
    {
        if (raw.StartsWith("PLAY:"))
        {
            string videoName = raw.Substring(5).Trim();
            if (!string.IsNullOrEmpty(videoName))
                videoLibrary?.ChangeVideoPublic(videoName);
        }
        else if (raw == "NEXT")
        {
            videoLibrary?.NextVideo();
        }
        else if (raw == "PREV")
        {
            videoLibrary?.PreviousVideo();
        }
        else if (raw == "LIST")
        {
            string json = videoLibrary != null ? videoLibrary.GetVideoListJSON() : "{}";
            Debug.Log($"[VIDEO_LIST]{json}");
        }
        else
        {
            Debug.LogWarning($"[ADBCmd] Comando desconocido: {raw}");
        }
    }
}