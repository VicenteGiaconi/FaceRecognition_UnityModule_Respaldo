using UnityEngine;

public class EyeTrackingCapture : MonoBehaviour
{
    [Header("Eye Gaze References")]
    [SerializeField] private OVREyeGaze leftEyeGaze;
    [SerializeField] private OVREyeGaze rightEyeGaze;

    private bool isCapturing;

    public struct EyeFrame
    {
        public bool valid;
        public float timestamp;
        public float gazeX;            // promedio horizontal (-1..+1, + = derecha)
        public float gazeY;            // promedio vertical   (-1..+1, + = arriba)
        public float leftGazeX;
        public float leftGazeY;
        public float rightGazeX;
        public float rightGazeY;
        public float leftConfidence;
        public float rightConfidence;
        public float convergenceDist;  // metros, limitado 0.2..5
    }

    // Called directly by WebSocketSender on its own timer — no Update() caching needed.
    public EyeFrame Sample()
    {
        if (!isCapturing) return default;

        bool leftOk  = leftEyeGaze  != null && leftEyeGaze.EyeTrackingEnabled;
        bool rightOk = rightEyeGaze != null && rightEyeGaze.EyeTrackingEnabled;

        Vector3 leftFwd  = leftOk  ? leftEyeGaze.transform.forward  : Vector3.forward;
        Vector3 rightFwd = rightOk ? rightEyeGaze.transform.forward : Vector3.forward;

        float leftInward  =  leftFwd.x;
        float rightInward = -rightFwd.x;
        float halfAngle = Mathf.Clamp((leftInward + rightInward) * 0.5f, 0f, 1f);

        return new EyeFrame
        {
            valid            = leftOk || rightOk,
            timestamp        = Time.time,
            gazeX            = (leftFwd.x + rightFwd.x) * 0.5f,
            gazeY            = (leftFwd.y + rightFwd.y) * 0.5f,
            leftGazeX        = leftFwd.x,
            leftGazeY        = leftFwd.y,
            rightGazeX       = rightFwd.x,
            rightGazeY       = rightFwd.y,
            leftConfidence   = leftOk  ? leftEyeGaze.Confidence  : 0f,
            rightConfidence  = rightOk ? rightEyeGaze.Confidence : 0f,
            convergenceDist  = halfAngle > 0.01f ? Mathf.Clamp(0.032f / halfAngle, 0.2f, 5f) : 5f,
        };
    }

    public void StartCapture()
    {
        isCapturing = true;
        Debug.Log("[EyeCapture] Captura iniciada.");
    }

    public void StopCapture()
    {
        isCapturing = false;
        Debug.Log("[EyeCapture] Captura detenida.");
    }

    public bool IsCapturing() => isCapturing;
}
