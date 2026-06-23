# FaceRecognition Unity Module

Unity VR application for real-time facial expression and eye tracking capture on Meta Quest Pro/Quest 3. Connects to `jacinta-rcp-backend` via WebSocket to stream biometric data and receive recording commands from a frontend operator.

## Requirements

- Unity 6000.0.47f1
- Meta Quest Pro or Quest 3 with **face tracking** and **eye tracking** enabled in headset settings
- ADB available at:
  ```
  /home/vgiac/Unity/Hub/Editor/6000.0.47f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
  ```

Define a shell alias to avoid typing the full path in every command:

```bash
alias adb="/home/vgiac/Unity/Hub/Editor/6000.0.47f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb"
```

---

## Project Structure

```
Assets/
└── Scripts/
    ├── FacialExpressionCapture.cs    # Reads 63 blendshapes from OVRFaceExpressions at ~10 Hz
    ├── EyeTrackingCapture.cs         # Reads gaze direction and confidence from OVREyeGaze
    ├── DataLogger.cs                 # Writes combined facial+eye data to a timestamped CSV on device
    ├── WebSocketSender.cs            # WebSocket client — streams data, receives commands, saves local JSON
    ├── RealtimeDataTransmiter.cs     # Streams facial data via ADB logcat (alternative to WebSocket)
    ├── RecordingController.cs        # Orchestrates all modules; handles start/stop from buttons or WebSocket
    ├── VideoLibraryManager.cs        # Manages 360° video playlist and playback
    ├── Video360Manager.cs            # Handles 360° video rendering
    ├── ADBCommandReceiver.cs         # Receives video control commands from PC via ADB file push
    └── UnityMainThreadDispatcher.cs  # Thread-safety utility for async WebSocket callbacks
```

All scripts are attached to the **FacialTrackingSystem** GameObject in `Assets/Scenes/SampleScene.unity`.

---

## Inspector Configuration

Open `SampleScene` and select **FacialTrackingSystem**. Verify these values on the **WebSocketSender** component:

| Field | Dev (LAN) | Production |
|---|---|---|
| Server Url | `ws://<YOUR_LAN_IP>:8010` | `wss://uandes-rcptraining.onrender.com` |
| Vr Name | `VR UANDES` | `VR UANDES` |
| Minimum Value Threshold | `0.001` | `0.001` |

To find your machine's LAN IP:
```bash
hostname -I | awk '{print $1}'
```

**DataLogger** component:
| Field | Default |
|---|---|
| File Name | `facial_data` |
| Use Timestamp In File Name | ✓ |

---

## Build and Deploy

### 1. Build APK

In Unity: **File → Build Settings** → confirm platform is **Android** → click **Build** → save as `Builds/vX.apk`

### 2. Install on Quest

```bash
adb install -r Builds/vX.apk
```

### 3. Watch logs

```bash
adb logcat -s Unity
```

---

## Testable Flows

### Flow 1 — Full connected session (WebSocket + backend + frontend)

This is the primary flow used during experiments.

**Start backend:**
```bash
cd jacinta-rcp-backend
pipenv shell
daphne -b 0.0.0.0 -p 8010 config.asgi:application
```

**Start frontend:**
```bash
cd jacinta-rcp-frontend
npm run dev
# Open http://localhost:5173
```

**On Quest:** launch the app. Logcat shows:
```
[WSSender] Conectado al WebSocket base.
[WSSender] sessionId asignado: <uuid>
[WSSender] WebSocket de sesión conectado (intento 1).
```

**On frontend:**
1. Home → **Start** → `/connect-vr`
2. Click **"VR UANDES"** in the session list
3. Both "Conectado a la sesión" and "VR Conectado" appear checked

**Start recording:** click **Start Recording** on the frontend (or press **A/X** on the controller).

Logcat:
```
[RecCtrl] Grabación iniciada.
[WSSender] Sesión iniciada.
[FaceCapture] Captura iniciada.
[EyeCapture] Captura iniciada.
```

Frontend receives real-time frames:
```json
{"type":"FACIAL_RT","t":12.345,"d":{"14":0.72,"15":0.68}}
{"type":"EYE_RT","t":12.345,"gx":0.12,"gy":-0.03,"lc":0.95,"rc":0.91,...}
```

**Stop recording:** click **Stop Recording** on the frontend (or press **B/Y**).

Logcat:
```
[WSSender] Finalizando sesión. Duración: XX.Xs, Puntos: XXX
[WSSender] Resumen guardado en: /storage/.../facial_summary_YYYYMMDD_HHmmss.json
[WSSender] Resumen de sesión enviado.
[WSSender] Resumen ocular guardado en: /storage/.../eye_summary_YYYYMMDD_HHmmss.json
[WSSender] Resumen ocular enviado.
```

Frontend receives both summaries:
```json
{"type":"FACIAL_SUMMARY","metadata":{...},"statistics":{...},"rawData":[...]}
{"type":"EYE_SUMMARY","metadata":{...},"statistics":{...},"rawData":[...]}
```

**Start a second session without changing the video:** click **Nueva Sesión** → **Start Recording** again. The video restarts from the beginning automatically.

---

### Flow 2 — Offline recording (no backend)

Use this to verify capture works without a network connection.

1. Launch the app with the Quest disconnected from Wi-Fi or with the backend stopped.
2. Press **A/X** on the controller to start recording.
3. Press **B/Y** to stop.

Logcat shows:
```
[WSSender] No conectado. Resumen guardado localmente.
[WSSender] No conectado. Resumen ocular guardado localmente.
```

All data is still written to the Quest's storage. Retrieve it after with ADB (see [Retrieving saved files](#retrieving-saved-files)).

---

### Flow 3 — WebSocket reconnection during recording

If the backend restarts or the Wi-Fi drops mid-session, the app retries automatically up to 5 times with exponential backoff (2 s → 4 s → 8 s → 15 s max).

Logcat during reconnect:
```
[WSSender] Reconectando sesión <uuid> (intento 1/5) en 2s...
[WSSender] WebSocket de sesión conectado (intento 2).
```

If all retries fail:
```
[WSSender] No se pudo reconectar después de varios intentos.
```

The recording **continues capturing and logging locally** regardless of connection state. The summaries are saved to file before attempting to send them, so no data is lost on connection failure.

---

### Flow 4 — Video control via WebSocket

The frontend can change the video playing inside the headset during a session.

**From the frontend UI:** use the video selector in the session view.

**Manually from the browser DevTools console:**

```js
// Connect as frontend (replace <SESSION_ID> with the ID shown in the VR Selector)
const ws = new WebSocket("ws://localhost:8010/ws/session/<SESSION_ID>/");
ws.send(JSON.stringify({ type: "REGISTER", role: "frontend" }));
ws.onmessage = (e) => console.log(JSON.parse(e.data));

// Request the list of available videos
ws.send(JSON.stringify({ type: "VIDEO_LIST_REQUEST" }));
// Quest responds:
// {"type":"VIDEO_LIST","status":"ok","videos":["VALDIVIA1_video.mp4",...],"current":"..."}

// Change and play a specific video
ws.send(JSON.stringify({ type: "VIDEO_PLAY", name: "CONCON.mp4" }));
```

Available videos (defined in `VideoLibraryManager` Inspector):
- `VALDIVIA1_video.mp4`
- `AK_video.mp4`
- `AK_video_2.mp4`
- `CONCON.mp4`
- `stroop_congruente.mp4`
- `stroop_incongruente.mp4`
- `litoral_central_chile.mp4`
- `tricao.mp4`

---

### Flow 5 — Video control via ADB (without backend)

`ADBCommandReceiver` polls a file at `Application.persistentDataPath/quest_cmd.txt` every 0.5 s and executes video commands found there. Use this when there is no backend available.

```bash
PKG="com.UnityTechnologies.com.unity.template.urpblank"
FILES="/sdcard/Android/data/$PKG/files"

# Play a specific video
adb shell "echo 'PLAY:CONCON.mp4' > $FILES/quest_cmd.txt"

# Next video
adb shell "echo 'NEXT' > $FILES/quest_cmd.txt"

# Previous video
adb shell "echo 'PREV' > $FILES/quest_cmd.txt"

# List available videos (output appears in logcat)
adb shell "echo 'LIST' > $FILES/quest_cmd.txt"
adb logcat -s Unity | grep VIDEO_LIST
```

---

## Retrieving Saved Files

Every completed session writes three files to the Quest's storage:

| File | Content |
|---|---|
| `facial_data_YYYYMMDD_HHmmss.csv` | Per-frame: timestamp + 63 facial blendshapes + eye gaze (combined) |
| `facial_summary_YYYYMMDD_HHmmss.json` | `FACIAL_SUMMARY` JSON: metadata, statistics (attention/stress/mouth/blinks), last 1000 raw frames |
| `eye_summary_YYYYMMDD_HHmmss.json` | `EYE_SUMMARY` JSON: metadata, gaze statistics, last 1000 raw eye frames |

```bash
PKG="com.UnityTechnologies.com.unity.template.urpblank"

# List all session files
adb shell ls /sdcard/Android/data/$PKG/files/

# Copy everything to a local folder
adb pull /sdcard/Android/data/$PKG/files/ ./quest_backup/
```

---

## WebSocket Message Reference

### Quest → Backend (relayed to frontend)

| Message | When |
|---|---|
| `{"type":"REGISTER","role":"vr","name":"VR UANDES","machine_id":"..."}` | On session WebSocket connect |
| `{"type":"FACIAL_RT","t":<float>,"d":{"<expr_id>":<float>,...}}` | Each capture tick (~10 Hz) while recording |
| `{"type":"EYE_RT","t":<float>,"gx":<float>,"gy":<float>,"lc":<float>,"rc":<float>,"lgx":<float>,"lgy":<float>,"rgx":<float>,"rgy":<float>,"dist":<float>}` | Same tick, if eye tracking is active |
| `{"type":"FACIAL_SUMMARY","metadata":{...},"statistics":{...},"rawData":[...]}` | On stop recording |
| `{"type":"EYE_SUMMARY","metadata":{...},"statistics":{...},"rawData":[...]}` | On stop recording |
| `{"type":"VIDEO_LIST","status":"ok","videos":[...],"current":"..."}` | In response to `VIDEO_LIST_REQUEST` |

### Frontend → Quest (via backend relay)

| Message | Effect |
|---|---|
| `{"type":"START_RECORDING"}` | Starts facial+eye capture, CSV logging, video playback |
| `{"type":"STOP_RECORDING"}` | Stops capture, saves CSV, saves and sends JSON summaries |
| `{"type":"VIDEO_PLAY","name":"<filename>"}` | Changes and plays the specified video |
| `{"type":"VIDEO_LIST_REQUEST"}` | Quest responds with `VIDEO_LIST` |

---

## Data Schema

### `FACIAL_SUMMARY` / `facial_summary_*.json`

```json
{
  "type": "FACIAL_SUMMARY",
  "metadata": {
    "timestamp": "2026-06-15 14:30:00",
    "duration": 62.5,
    "dataPoints": 625,
    "totalBlinks": 18
  },
  "statistics": {
    "attention": { "min": 0.612, "max": 0.998, "avg": 0.847 },
    "stress":    { "min": 0.001, "max": 0.312, "avg": 0.089 },
    "mouth_activity": { "min": 0.000, "max": 0.241, "avg": 0.031 }
  },
  "rawData": [
    { "t": 0.1, "e": { "14": 0.72, "15": 0.68 } }
  ]
}
```

Derived metrics:
- **attention** — `1 - avg(brow_lowerer_L/R, cheek_raiser_L/R)` (expressions 14, 15, 20, 21)
- **stress** — `avg(brow_lowerer_L/R, nose_wrinkler_L/R)` (expressions 0, 1, 22, 23)
- **mouth_activity** — `avg(lip_corner_puller_L/R, upper_lip_raiser_L/R, lip_stretcher_L/R)` (expressions 24, 32, 33, 42, 43)
- **blink** — detected when `avg(eyes_closed_L/R)` (expressions 12, 13) > 0.7

### `EYE_SUMMARY` / `eye_summary_*.json`

```json
{
  "type": "EYE_SUMMARY",
  "metadata": {
    "timestamp": "2026-06-15 14:30:00",
    "duration": 62.5,
    "dataPoints": 620
  },
  "statistics": {
    "gaze_x":     { "min": -0.41, "max": 0.38, "avg": 0.02 },
    "gaze_y":     { "min": -0.29, "max": 0.21, "avg": -0.05 },
    "confidence": { "min": 0.81,  "max": 0.99, "avg": 0.94 },
    "dist":       { "min": 0.52,  "max": 4.98, "avg": 1.83 }
  },
  "rawData": [
    { "t": 0.1, "gx": 0.12, "gy": -0.03, "dist": 1.24 }
  ]
}
```

### CSV columns (`facial_data_*.csv`)

`Timestamp, <63 OVRFaceExpressions blendshapes...>, Eye_GazeX, Eye_GazeY, Eye_LeftGazeX, Eye_LeftGazeY, Eye_RightGazeX, Eye_RightGazeY, Eye_LeftConf, Eye_RightConf, Eye_ConvergenceDist`

---

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| No `[WSSender]` logs on startup | `WebSocketSender` not attached to `FacialTrackingSystem` |
| `Unable to connect to the remote server` | `Server Url` in Inspector uses `localhost` — must be the machine's LAN IP |
| VR Selector shows no sessions | Quest and PC are not on the same Wi-Fi network |
| `Error: tracking facial no disponible` | Face tracking not enabled in Quest Settings → Privacy, or headset not worn |
| `FACIAL_RT` arrives but `d` is always `{}` | All expressions below noise threshold — wear headset correctly, check `minimumValueThreshold` |
| Video doesn't play on second session | Fixed in current branch — `PlayCurrentVideo()` calls `Prepare()` before starting |
| `[WSSender] No conectado. Resumen guardado localmente.` | Expected behavior when backend unreachable — retrieve file with ADB |
| ADB command has no effect | Check that `ADBCommandReceiver` is attached to `FacialTrackingSystem` and logcat shows `[ADBCmd] Polling iniciado` |
