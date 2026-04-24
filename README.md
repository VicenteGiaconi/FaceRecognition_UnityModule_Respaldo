# FaceRecognition Unity Module

Unity VR application for real-time facial expression capture on Meta Quest Pro/Quest 3. Connects to `jacinta-rcp-backend` via WebSocket to stream facial data and receive recording commands from a frontend client.

## Requirements

- Unity 6000.0.47f1
- Meta Quest Pro or Quest 3 with face tracking enabled
- [jacinta-rcp-backend](../jacinta-rcp-backend) running locally
- [jacinta-rcp-frontend](../jacinta-rcp-frontend) running locally
- ADB available at:
  ```
  /home/vgiac/Unity/Hub/Editor/6000.0.47f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
  ```

## Project Structure

```
Assets/
└── Scripts/
    ├── FacialExpressionCapture.cs   # Reads 44+ facial expressions from OVRFaceExpressions at ~10Hz
    ├── DataLogger.cs                # Saves facial data to a timestamped CSV file on device
    ├── RealtimeDataTransmitter.cs   # Streams facial data via ADB logcat
    ├── WebSocketSender.cs           # WebSocket client — connects to backend, streams data, receives commands
    ├── RecordingController.cs       # Orchestrates all modules; handles start/stop from backend or VR buttons
    ├── VideoLibraryManager.cs       # Manages 360° video playlist
    ├── Video360Manager.cs           # Handles 360° video playback
    ├── ADBCommandReceiver.cs        # Receives video control commands from PC via ADB file polling
    └── UnityMainThreadDispatcher.cs # Thread-safety utility for WebSocket callbacks
```

## Scene Setup

All scripts are attached to the **FacialTrackingSystem** GameObject in `Assets/Scenes/SampleScene.unity`.

When opening the project for the first time, verify the **WebSocketSender** component on `FacialTrackingSystem` has **Server Url** set to:
```
ws://<YOUR_MACHINE_IP>:8010
```

To find your machine's IP:
```bash
hostname -I | awk '{print $1}'
```

## How to Build and Test

### Step 1 — Start the backend

```bash
cd ../jacinta-rcp-backend
pipenv shell
daphne -b 0.0.0.0 -p 8010 config.asgi:application
```

### Step 2 — Start the frontend

```bash
cd ../jacinta-rcp-frontend
npm install
npm run dev
```

Open `http://localhost:5173` in your browser.

### Step 3 — Build and deploy the APK

In Unity:
1. **File → Build Settings** → confirm platform is **Android**
2. Click **Build** and save the APK (e.g. `Builds/v6.apk`)
3. Deploy via ADB:

```bash
/home/vgiac/Unity/Hub/Editor/6000.0.47f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb install -r Builds/v6.apk
```

### Step 4 — Open ADB logcat

```bash
/home/vgiac/Unity/Hub/Editor/6000.0.47f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb logcat -s Unity
```

### Step 5 — Launch the app on the Quest

Put on the headset and open the app. In logcat you should see:

```
[WSSender] Conectado al WebSocket base.
[WSSender] sessionId asignado: xxxxxxxx
[WSSender] WebSocket de sesión conectado.
[WSSender] Mensaje REGISTER enviado.
```

### Step 6 — Connect the frontend to the VR session

1. Click **Start** on the frontend home page → navigates to `/connect-vr`
2. The VR Selector will list your Quest as **"VR UANDES"** with a green dot
3. Click on it to join the session
4. Both **"Conectado a la sesión"** and **"VR Conectado"** should show as checked

### Step 7 — Start recording

Open browser DevTools (`F12`) → Console and run each line separately:

```js
const ws = new WebSocket(`ws://localhost:8010/ws/session/<SESSION_ID>/`);
```
```js
ws.send(JSON.stringify({ type: "REGISTER", role: "frontend" }));
```
```js
ws.onmessage = (e) => console.log("Received:", e.data);
```
```js
ws.send(JSON.stringify({ type: "START_RECORDING" }));
```

Replace `<SESSION_ID>` with the ID shown in the VR Selector on the frontend.

In logcat you should see:
```
[RecCtrl] Comando START_RECORDING recibido del backend.
[RecCtrl] Grabación iniciada.
[WSSender] Sesión iniciada.
```

Real-time facial data starts arriving in the console:
```json
{"type":"FACIAL_RT","t":12.345,"d":{"14":0.72,"15":0.68}}
```

### Step 8 — Stop recording

```js
ws.send(JSON.stringify({ type: "STOP_RECORDING" }));
```

In logcat:
```
[RecCtrl] Comando STOP_RECORDING recibido del backend.
[WSSender] Finalizando sesión. Duración: XX.Xs, Puntos: XXX
[WSSender] Resumen de sesión enviado.
```

The browser console receives the full session summary:
```json
{"type":"FACIAL_SUMMARY","metadata":{...},"statistics":{...},"rawData":[...]}
```

## WebSocket Message Reference

| Message | Direction | Description |
|---------|-----------|-------------|
| `{"type":"REGISTER","role":"vr","name":"VR UANDES"}` | Quest → Backend | Sent on connection to register the VR device |
| `{"type":"START_RECORDING"}` | Frontend → Quest | Triggers recording start |
| `{"type":"STOP_RECORDING"}` | Frontend → Quest | Triggers recording stop and summary send |
| `{"type":"FACIAL_RT","t":...,"d":{...}}` | Quest → Frontend | Real-time facial expression data per capture tick |
| `{"type":"FACIAL_SUMMARY",...}` | Quest → Frontend | Full session summary sent at end of recording |

## Data Output

Each recording session produces:
- **CSV file** on the Quest at `/storage/emulated/0/Android/data/<app>/files/facial_data_YYYYMMDD_HHmmss.csv`
- **ADB logcat stream** tagged `FACIAL_DATA` (filter with `adb logcat -s Unity`)
- **WebSocket stream** of `FACIAL_RT` messages to the connected frontend
- **WebSocket summary** (`FACIAL_SUMMARY`) sent to the frontend at session end

## Troubleshooting

| Symptom | Likely cause |
|---------|-------------|
| No `[WSSender]` logs on startup | `WebSocketSender` component not added to `FacialTrackingSystem` |
| `Unable to connect to the remote server` | Wrong `serverUrl` in Inspector — must be machine's LAN IP, not `localhost` |
| VR Selector shows no sessions | Quest and machine are not on the same Wi-Fi network |
| `START_RECORDING` has no effect | Face tracking not enabled or headset not worn |
| No `FACIAL_RT` messages | All expression values below noise threshold (0.01) — wear the headset correctly |
