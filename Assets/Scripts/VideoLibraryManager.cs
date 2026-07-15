using UnityEngine;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class VideoLibraryManager : MonoBehaviour
{
    [Header("Referencias")]
    public VideoPlayer videoPlayer;

    [Header("Videos disponibles")]
    [Tooltip("Lista de nombres de archivos .mp4 en Assets/StreamingAssets/")]
    public string[] knownVideos = {
        "VALDIVIA.mp4",
        "AUCKLAND.mp4", 
        "LITORAL_CENTRAL.mp4",
        "TRICAO.mp4",
        "MIX_TRICAO+PLAYAS.mp4",
        "stroop_congruente.mp4",
        "stroop_incongruente.mp4"
    };

    [Tooltip("Reproducir el primer video al iniciar (false = esperar señal del frontend)")]
    public bool autoPlayFirst = false;

    private List<string> availableVideos = new List<string>();
    private string currentVideo = "";

    void Start()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        ScanAvailableVideos();

        if (availableVideos.Count > 0)
        {
            if (autoPlayFirst)
                ChangeVideoPublic(availableVideos[0]);
            else
                PrepareVideoOnly(availableVideos[0]); // precarga sin reproducir
        }

        Debug.Log("[VideoLibrary] Listo. Videos en StreamingAssets.");
    }

    void ScanAvailableVideos()
    {
        availableVideos.Clear();

        foreach (string videoName in knownVideos)
        {
            availableVideos.Add(videoName);
            Debug.Log($"[VideoLibrary] Video registrado: {videoName}");
        }

        Debug.Log($"[VideoLibrary] {availableVideos.Count} videos disponibles.");
        Debug.Log($"[VIDEO_LIST]{GetVideoListJSON()}");
    }

    public void ChangeVideoPublic(string videoName)
    {
        ChangeVideoOnMainThread(videoName);
    }

    public void PlayCurrentVideo()
    {
        if (videoPlayer == null) return;
        if (playCoroutine != null) StopCoroutine(playCoroutine);
        videoPlayer.Prepare();
        playCoroutine = StartCoroutine(PlayWhenReady());
        Debug.Log($"[VideoLibrary] Reproduciendo video actual: {currentVideo}");
    }

    public void StopCurrentVideo()
    {
        if (videoPlayer == null) return;
        if (playCoroutine != null) { StopCoroutine(playCoroutine); playCoroutine = null; }
        videoPlayer.Stop();
        Debug.Log($"[VideoLibrary] Video detenido: {currentVideo}");
    }

    public string GetVideoListJSON()
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

    public void NextVideo()
    {
        if (availableVideos.Count == 0) return;
        int next = (availableVideos.IndexOf(currentVideo) + 1) % availableVideos.Count;
        ChangeVideoOnMainThread(availableVideos[next]);
    }

    public void PreviousVideo()
    {
        if (availableVideos.Count == 0) return;
        int prev = (availableVideos.IndexOf(currentVideo) - 1 + availableVideos.Count) % availableVideos.Count;
        ChangeVideoOnMainThread(availableVideos[prev]);
    }

    private Coroutine playCoroutine;

    // Precarga el video sin reproducirlo (para startup rápido)
    private void PrepareVideoOnly(string videoName)
    {
        Debug.Log($"[VideoLibrary] Precargando (sin reproducir): {videoName}");
        currentVideo = videoName;

        if (videoPlayer == null) { Debug.LogError("[VideoLibrary] VideoPlayer no disponible."); return; }

        if (playCoroutine != null) { StopCoroutine(playCoroutine); playCoroutine = null; }
        videoPlayer.Stop();
        videoPlayer.url = Path.Combine(Application.streamingAssetsPath, videoName);
        videoPlayer.Prepare();
    }

    void ChangeVideoOnMainThread(string videoName)
    {
        Debug.Log($"[VideoLibrary] Cambiando a: {videoName}");
        PrepareVideoOnly(videoName);
    }

    IEnumerator PlayWhenReady()
    {
        while (!videoPlayer.isPrepared)
            yield return new WaitForSeconds(0.1f);

        videoPlayer.Play();
        Debug.Log($"[VideoLibrary] Reproduciendo: {currentVideo}");
    }

    public string GetCurrentVideo() => currentVideo;
    public List<string> GetAvailableVideos() => availableVideos;
}