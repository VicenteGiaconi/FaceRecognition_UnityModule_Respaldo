using UnityEngine;
using UnityEngine.Video;
using System.Collections;
public class Video360Manager : MonoBehaviour
{
    [Header("Configuración")]
    [Tooltip("Nombre del archivo de video en StreamingAssets (ejemplo: video360.mp4)")]
    public string videoFileName = "VALDIVIA1_video.mp4";

    [Tooltip("Reproducir automáticamente al iniciar")]
    public bool autoPlay = true;

    [Tooltip("Volumen del video (0 a 1)")]
    [Range(0f, 1f)]
    public float volume = 1.0f;

    [Header("Control VR (Opcional)")]
    public OVRInput.Button playPauseButton = OVRInput.Button.One; // Botón A/X
    public OVRInput.Button restartButton = OVRInput.Button.Two;   // Botón B/Y

    private VideoPlayer videoPlayer;
    private bool isPaused = false;

    void Start()
    {
        // Obtener Video Player
        videoPlayer = GetComponent<VideoPlayer>();

        if (videoPlayer == null)
        {
            Debug.LogError("[Video360] No se encontró VideoPlayer en este GameObject");
            return;
        }

        // Configurar ruta del video
        string videoPath = System.IO.Path.Combine(Application.streamingAssetsPath, videoFileName);
        videoPlayer.url = videoPath;

        Debug.Log($"[Video360] Ruta del video: {videoPath}");

        // Configurar Audio
        SetupAudio();

        // Suscribirse a eventos
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.started += OnVideoStarted;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.loopPointReached += OnVideoLoop;

        // Preparar el video
        Debug.Log("[Video360] Preparando video...");
        videoPlayer.Prepare();

        if (autoPlay)
        {
            StartCoroutine(PlayWhenReady());
        }
    }

    void SetupAudio()
    {
        if (videoPlayer.audioTrackCount > 0)
        {
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.SetTargetAudioSource(0, audioSource);
            audioSource.volume = volume;

            Debug.Log($"[Video360] Audio configurado: {videoPlayer.audioTrackCount} pistas");
        }
        else
        {
            Debug.LogWarning("[Video360] El video no tiene pistas de audio");
        }
    }

    IEnumerator PlayWhenReady()
    {
        // Esperar a que el video esté preparado
        while (!videoPlayer.isPrepared)
        {
            yield return new WaitForSeconds(0.1f);
        }

        Debug.Log("[Video360] Video listo, reproduciendo...");
        videoPlayer.Play();
    }

    void Update()
    {
        if (videoPlayer == null) return;

        // Control con botones VR
        if (OVRInput.GetDown(playPauseButton))
        {
            TogglePlayPause();
        }

        if (OVRInput.GetDown(restartButton))
        {
            RestartVideo();
        }
    }

    public void TogglePlayPause()
    {
        if (videoPlayer == null) return;

        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
            isPaused = true;
            Debug.Log("[Video360] Video pausado");
        }
        else
        {
            videoPlayer.Play();
            isPaused = false;
            Debug.Log("[Video360] Video reproduciendo");
        }
    }

    public void RestartVideo()
    {
        if (videoPlayer == null) return;

        videoPlayer.time = 0;
        videoPlayer.Play();
        Debug.Log("[Video360] Video reiniciado");
    }

    public void StopVideo()
    {
        if (videoPlayer == null) return;

        videoPlayer.Stop();
        Debug.Log("[Video360] Video detenido");
    }

    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);

        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.volume = volume;
        }
    }

    // Eventos del Video Player
    void OnVideoPrepared(VideoPlayer vp)
    {
        Debug.Log($"[Video360] ✓ Video preparado");
        Debug.Log($"[Video360]   Resolución: {vp.width}x{vp.height}");
        Debug.Log($"[Video360]   Duración: {vp.length:F2}s");
        Debug.Log($"[Video360]   Frame Rate: {vp.frameRate}fps");
        Debug.Log($"[Video360]   Frames totales: {vp.frameCount}");
    }

    void OnVideoStarted(VideoPlayer vp)
    {
        Debug.Log("[Video360] ✓ Video iniciado");
    }

    void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError($"[Video360] ✗ ERROR: {message}");
        Debug.LogError($"[Video360]   URL: {vp.url}");
        Debug.LogError($"[Video360]   Verifica que:");
        Debug.LogError($"[Video360]   1. El archivo '{videoFileName}' existe en Assets/StreamingAssets/");
        Debug.LogError($"[Video360]   2. El formato sea MP4 con codec H.264");
        Debug.LogError($"[Video360]   3. El video no esté corrupto");
    }

    void OnVideoLoop(VideoPlayer vp)
    {
        Debug.Log("[Video360] Video completado, reiniciando loop...");
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.started -= OnVideoStarted;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.loopPointReached -= OnVideoLoop;
        }
    }

    // Métodos públicos de utilidad
    public bool IsPlaying()
    {
        return videoPlayer != null && videoPlayer.isPlaying;
    }

    public bool IsPaused()
    {
        return isPaused;
    }

    public float GetCurrentTime()
    {
        return videoPlayer != null ? (float)videoPlayer.time : 0f;
    }

    public float GetDuration()
    {
        return videoPlayer != null ? (float)videoPlayer.length : 0f;
    }

    public float GetProgress()
    {
        if (videoPlayer == null || videoPlayer.length == 0) return 0f;
        return (float)(videoPlayer.time / videoPlayer.length);
    }
}

