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
        "VALDIVIA1_video.mp4",
        "AK_video_2.mp4",
    };

    [Tooltip("Reproducir el primer video al iniciar")]
    public bool autoPlayFirst = false;

    private List<string> availableVideos = new List<string>();
    private string currentVideo = "";

    void Start()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();

        ScanAvailableVideos();

        if (autoPlayFirst && availableVideos.Count > 0)
            ChangeVideoPublic(availableVideos[0]);

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
        if (!availableVideos.Contains(videoName))
        {
            Debug.LogWarning($"[VideoLibrary] Video '{videoName}' no está en la lista.");
            return;
        }
        ChangeVideoOnMainThread(videoName);
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

    void ChangeVideoOnMainThread(string videoName)
    {
        Debug.Log($"[VideoLibrary] Cambiando a: {videoName}");
        currentVideo = videoName;

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.url = Path.Combine(Application.streamingAssetsPath, videoName);
            videoPlayer.Prepare();
            StartCoroutine(PlayWhenReady());
        }
        else
        {
            Debug.LogError("[VideoLibrary] VideoPlayer no disponible.");
        }
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