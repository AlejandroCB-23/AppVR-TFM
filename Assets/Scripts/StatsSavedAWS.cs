#if WAVE_SDK_IMPORTED

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using Wave.Essence.Eye;
using Alex.OcularVergenceLibrary;
using static HeatMapData;

public class StatsSavedAWS : MonoBehaviour
{
    public static StatsSavedAWS Instance { get; private set; }

    [Header("AWS Upload Configuration")]
    public string uploadApiEndpoint = "https://f2ytxnhuo0.execute-api.us-east-1.amazonaws.com";
    public bool logUploadSuccess = true;

    [Header("Data Collection Settings")]
    public float captureInterval = 0.015f;

    [Header("Debug Logging")]
    public bool saveDebugLogToFile = true;
    public string debugLogFileName = "StatsSavedAWS.log";
    public bool mirrorLogsToConsole = true;

    [Header("Storage Configuration")]
    private string statsPath;
    private string debugLogPath;

    private float lastCaptureTime = 0f;
    private EyeVergenceEventAWS currentEvent = null;
    private readonly List<EyeVergenceEventAWS> completedEvents = new List<EyeVergenceEventAWS>();
    private readonly Dictionary<string, EyeVergenceEventAWS> pendingShotEventsByShip = new Dictionary<string, EyeVergenceEventAWS>();
    private float recordingStartTime = -1f;
    private float sessionDurationSeconds = -1f;
    private int currentGameNumber;
    private bool finalizedThisSession = false;
    private bool saveInProgress = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        RequestStoragePermissions();
        InitializeStorage();
        LogDebug("StatsSavedAWS.Start() inicializado.");

        if (EyeManager.Instance != null)
        {
            EyeManager.Instance.EnableEyeTracking = true;
            LogDebug("EyeTracking habilitado desde StatsSavedAWS.");
        }
        else
        {
            LogDebug("EyeManager.Instance es null en Start().");
        }
    }

    private void InitializeStorage()
    {
        string dataPath = Application.persistentDataPath;
        if (!PlayerPrefs.HasKey("GameNumber"))
        {
            PlayerPrefs.SetInt("GameNumber", 1);
            PlayerPrefs.Save();
        }

        currentGameNumber = PlayerPrefs.GetInt("GameNumber", 1);
        statsPath = Path.Combine(dataPath, "Stats.json");
        debugLogPath = Path.Combine(dataPath, debugLogFileName);

        if (saveDebugLogToFile)
        {
            try
            {
                File.AppendAllText(debugLogPath, $"\n===== Nueva sesion {DateTime.UtcNow:O} =====\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AWS][Vergence] No se pudo inicializar el log persistente: " + e.Message);
            }
        }
    }

    private void Update()
    {
        if (RecordingState.IsRecording && recordingStartTime < 0f)
        {
            recordingStartTime = Time.time;
            sessionDurationSeconds = -1f;
            finalizedThisSession = false;
            LogDebug($"Inicio de sesion de grabacion. gameNumber={currentGameNumber}");
        }

        if (!RecordingState.IsRecording && recordingStartTime >= 0f)
        {
            sessionDurationSeconds = Mathf.Max(0f, Time.time - recordingStartTime);
            recordingStartTime = -1f;
            LogDebug("Fin de sesion detectado. Lanzando guardado final.");
            if (!finalizedThisSession && !saveInProgress)
            {
                StartCoroutine(SaveFinalStatsCoroutine());
            }
        }

        if (RecordingState.IsRecording && EyeManager.Instance != null && EyeManager.Instance.IsEyeTrackingAvailable())
        {
            if (Time.time - lastCaptureTime >= captureInterval)
            {
                lastCaptureTime = Time.time;
                CaptureEyeTrackingData();
            }
        }
    }

    private IEnumerator SaveFinalStatsCoroutine()
    {
        Task saveTask = SaveFinalStatsAsync();
        while (!saveTask.IsCompleted)
        {
            yield return null;
        }

        if (saveTask.IsFaulted)
        {
            Debug.LogError($"[AWS][Vergence] Error guardando estadisticas finales: {saveTask.Exception}");
            LogDebug($"SaveFinalStatsCoroutine faulted: {saveTask.Exception}");
        }
        else
        {
            LogDebug("SaveFinalStatsCoroutine completada correctamente.");
        }
    }

    public void MarkShot(Ship ship = null)
    {
        if (currentEvent != null)
        {
            currentEvent.wasShot = true;
            if (ship != null)
            {
                currentEvent.shipShotTime = ship.GetAliveTime();
                pendingShotEventsByShip[ship.name] = currentEvent;
            }

            LogDebug($"MarkShot aplicado a evento actual. stimulus={currentEvent.stimulus}, shipShotTime={currentEvent.shipShotTime}");
        }
        else
        {
            LogDebug("MarkShot llamado sin currentEvent activo.");
        }
    }

    public void NotifyShipEliminated(Ship ship)
    {
        if (ship == null)
        {
            return;
        }

        EyeVergenceEventAWS shotEvent = null;
        if (!pendingShotEventsByShip.TryGetValue(ship.name, out shotEvent))
        {
            shotEvent = FindMostRecentShotEvent(ship.name);
        }
        else
        {
            pendingShotEventsByShip.Remove(ship.name);
        }

        if (shotEvent != null)
        {
            UpdateEventCumulativeCountersFromLiveState(shotEvent);
            LogDebug($"Contadores sincronizados SOLO para el barco eliminado: {ship.name}");
        }
    }

    public void NotifyPirateEscaped(Ship ship = null)
    {
        LogDebug("Contadores sincronizados tras Escape de pirata.");
    }

    public Task SaveFinalStatsAsync()
    {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

        if (!isActiveAndEnabled)
        {
            tcs.SetResult(true);
            return tcs.Task;
        }

        StartCoroutine(SaveFinalStatsInternalCoroutine(tcs));
        return tcs.Task;
    }

    private IEnumerator SaveFinalStatsInternalCoroutine(TaskCompletionSource<bool> tcs)
    {
        if (saveInProgress)
        {
            while (saveInProgress)
            {
                yield return null;
            }

            tcs?.SetResult(true);
            yield break;
        }

        if (finalizedThisSession)
        {
            tcs?.SetResult(true);
            yield break;
        }

        saveInProgress = true;

        FinalizePreviousEvent();
        NormalizeEventTimeBounds();
        NormalizeCumulativeCounters();
        LogDebug($"SaveFinalStatsInternal: eventos listos para subida={completedEvents.Count}");

        bool uploadOk = true;
        if (completedEvents.Count > 0)
        {
            string fileName = BuildVergenceFileName(currentGameNumber);
            string payload = BuildVergenceJsonlPayload();
            LogDebug($"Preparando upload de {fileName}. bytes={Encoding.UTF8.GetByteCount(payload)}");

            bool callbackResult = false;
            bool callbackInvoked = false;

            yield return StartCoroutine(UploadJsonFileCoroutine(fileName, payload, success =>
            {
                callbackResult = success;
                callbackInvoked = true;
            }));

            uploadOk = callbackInvoked && callbackResult;
            LogDebug($"Resultado subida estimulos. callbackInvoked={callbackInvoked}, callbackResult={callbackResult}");
        }
        else
        {
            LogDebug("No hay eventos de estimulos para subir (completedEvents.Count == 0).");
        }

        if (uploadOk)
        {
            completedEvents.Clear();
            pendingShotEventsByShip.Clear();
        }

        try
        {
            StatsTracker stats = StatsTracker.Instance;
            if (stats != null)
            {
                GameStats gameStats = new GameStats
                {
                    gameNumber = currentGameNumber,
                    piratesEliminated = stats.GetPiratesEliminated(),
                    fishingEliminated = stats.GetFishingEliminated(),
                    bestPirateStreak = stats.GetBestPirateStreak(),
                    maxTimeWithoutFishing = stats.GetMaxTimeWithoutFishing(),
                    shortestTimeToSinkPirate = stats.GetShortestTimeToSinkPirate(),
                    avgTimeToSinkPirate = stats.GetAverageTimeToSinkPirate(),
                    piratesEscaped = stats.GetPiratesEscaped()
                };

                AppendStats(gameStats);
            }

            currentGameNumber++;
            PlayerPrefs.SetInt("GameNumber", currentGameNumber);
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogError("[AWS][Vergence] Error guardando estadisticas locales: " + e.Message);
            LogDebug("Error guardando stats locales: " + e.Message);
        }

        finalizedThisSession = true;
        saveInProgress = false;
        LogDebug($"SaveFinalStatsInternal finalizada. uploadOk={uploadOk}, nextGameNumber={currentGameNumber}");

        tcs?.SetResult(true);
    }

    private void NormalizeEventTimeBounds()
    {
        if (completedEvents.Count == 0)
        {
            return;
        }

        completedEvents[0].startTime = 0f;

        if (completedEvents[0].endTime < completedEvents[0].startTime)
        {
            completedEvents[0].endTime = completedEvents[0].startTime;
        }

        if (sessionDurationSeconds >= 0f)
        {
            int lastIndex = completedEvents.Count - 1;
            EyeVergenceEventAWS lastEvent = completedEvents[lastIndex];
            lastEvent.endTime = Mathf.Max(lastEvent.startTime, sessionDurationSeconds);
            completedEvents[lastIndex] = lastEvent;
        }
    }

    private void NormalizeCumulativeCounters()
    {
        if (completedEvents.Count == 0)
        {
            return;
        }

        int maxGoSpawned = 0;
        int maxNoGoSpawned = 0;
        int maxGoEliminated = 0;
        int maxNoGoEliminated = 0;
        int maxGoEscaped = 0;

        for (int i = 0; i < completedEvents.Count; i++)
        {
            EyeVergenceEventAWS evt = completedEvents[i];

            maxGoSpawned = Mathf.Max(maxGoSpawned, evt.goShipsSpawned);
            maxNoGoSpawned = Mathf.Max(maxNoGoSpawned, evt.noGoShipsSpawned);
            maxGoEliminated = Mathf.Max(maxGoEliminated, evt.goShipsEliminated);
            maxNoGoEliminated = Mathf.Max(maxNoGoEliminated, evt.noGoShipsEliminated);
            maxGoEscaped = Mathf.Max(maxGoEscaped, evt.goShipsEscaped);

            evt.goShipsSpawned = maxGoSpawned;
            evt.noGoShipsSpawned = maxNoGoSpawned;
            evt.goShipsEliminated = maxGoEliminated;
            evt.noGoShipsEliminated = maxNoGoEliminated;
            evt.goShipsEscaped = maxGoEscaped;
            completedEvents[i] = evt;
        }
    }

    private void CaptureEyeTrackingData()
    {
        if (!VergenceFunctions.TryGetCombinedEyeRay(out Ray ray))
        {
            LogDebug("CaptureEyeTrackingData: no combined eye ray.");
            return;
        }

        bool hitCollider = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity);
        float currentTime = Time.time - recordingStartTime;

        if (hitCollider)
        {
            string stimulusName = hit.collider.transform.parent != null ? hit.collider.transform.parent.name : hit.collider.gameObject.name;
            string stimulusType = ClassifyStimulus(stimulusName);

            if (currentEvent != null && currentEvent.stimulus == stimulusName)
            {
                currentEvent.endTime = currentTime;
            }
            else
            {
                FinalizePreviousEvent();
                currentEvent = CreateNewEvent(stimulusName, stimulusType, currentTime);
            }
        }
        else
        {
            if (currentEvent != null && currentEvent.stimulus == "Sky")
            {
                currentEvent.endTime = currentTime;
            }
            else
            {
                FinalizePreviousEvent();
                currentEvent = CreateNewEvent("Sky", "Sky", currentTime);
            }
        }
    }

    private EyeVergenceEventAWS CreateNewEvent(string name, string type, float time)
    {
        float aliveTime = -1f;
        if (type == "Go" || type == "NoGo")
        {
            foreach (GameObject shipObj in GameObject.FindGameObjectsWithTag("Ship"))
            {
                if (shipObj.name == name)
                {
                    Ship shipScript = shipObj.GetComponent<Ship>();
                    if (shipScript != null)
                    {
                        aliveTime = shipScript.GetAliveTime();
                    }
                    break;
                }
            }
        }

        return new EyeVergenceEventAWS
        {
            stimulus = name,
            type = type,
            wasShot = false,
            startTime = time,
            endTime = time,
            shipAliveTime = aliveTime,
            goShipsAlive = CountShipsByType("Go"),
            noGoShipsAlive = CountShipsByType("NoGo"),
            goShipsSpawned = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesSpawned() : 0,
            noGoShipsSpawned = StatsTracker.Instance != null ? StatsTracker.Instance.GetFishingSpawned() : 0,
            goShipsEliminated = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesEliminated() : 0,
            noGoShipsEliminated = StatsTracker.Instance != null ? StatsTracker.Instance.GetFishingEliminated() : 0,
            goShipsEscaped = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesEscaped() : 0,
            currentGoStreak = StatsTracker.Instance != null ? StatsTracker.Instance.GetCurrentPirateStreak() : 0
        };
    }

    private void UpdateEventCountersFromLiveState(EyeVergenceEventAWS targetEvent)
    {
        if (targetEvent == null)
        {
            return;
        }

        targetEvent.goShipsAlive = CountShipsByType("Go");
        targetEvent.noGoShipsAlive = CountShipsByType("NoGo");
        targetEvent.goShipsSpawned = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesSpawned() : 0;
        targetEvent.noGoShipsSpawned = StatsTracker.Instance != null ? StatsTracker.Instance.GetFishingSpawned() : 0;
        targetEvent.goShipsEliminated = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesEliminated() : 0;
        targetEvent.noGoShipsEliminated = StatsTracker.Instance != null ? StatsTracker.Instance.GetFishingEliminated() : 0;
        targetEvent.goShipsEscaped = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesEscaped() : 0;
        targetEvent.currentGoStreak = StatsTracker.Instance != null ? StatsTracker.Instance.GetCurrentPirateStreak() : 0;
    }

    private void UpdateEventCumulativeCountersFromLiveState(EyeVergenceEventAWS targetEvent)
    {
        if (targetEvent == null)
        {
            return;
        }

        targetEvent.goShipsSpawned = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesSpawned() : 0;
        targetEvent.noGoShipsSpawned = StatsTracker.Instance != null ? StatsTracker.Instance.GetFishingSpawned() : 0;
        targetEvent.goShipsEliminated = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesEliminated() : 0;
        targetEvent.noGoShipsEliminated = StatsTracker.Instance != null ? StatsTracker.Instance.GetFishingEliminated() : 0;
        targetEvent.goShipsEscaped = StatsTracker.Instance != null ? StatsTracker.Instance.GetPiratesEscaped() : 0;
        targetEvent.currentGoStreak = StatsTracker.Instance != null ? StatsTracker.Instance.GetCurrentPirateStreak() : 0;
    }

    private void UpdateLastCompletedEventCumulativeCounters()
    {
        if (completedEvents.Count == 0)
        {
            return;
        }

        int lastIndex = completedEvents.Count - 1;
        EyeVergenceEventAWS lastCompleted = completedEvents[lastIndex];
        UpdateEventCumulativeCountersFromLiveState(lastCompleted);
        completedEvents[lastIndex] = lastCompleted;
    }

    private EyeVergenceEventAWS FindMostRecentShotEvent(string shipName)
    {
        if (currentEvent != null && currentEvent.wasShot && currentEvent.stimulus == shipName)
        {
            return currentEvent;
        }

        for (int i = completedEvents.Count - 1; i >= 0; i--)
        {
            EyeVergenceEventAWS evt = completedEvents[i];
            if (evt.wasShot && evt.stimulus == shipName)
            {
                return evt;
            }
        }

        return null;
    }

    private int CountShipsByType(string type)
    {
        int count = 0;
        foreach (GameObject ship in GameObject.FindGameObjectsWithTag("Ship"))
        {
            Ship shipScript = ship.GetComponent<Ship>();
            if (shipScript != null && (shipScript.IsSinking() || shipScript.HasEscaped()))
            {
                continue;
            }

            if (ClassifyStimulus(ship.name) == type)
            {
                count++;
            }
        }

        return count;
    }

    private void FinalizePreviousEvent()
    {
        if (currentEvent != null)
        {
            completedEvents.Add(currentEvent);
            LogDebug($"Evento finalizado y agregado. stimulus={currentEvent.stimulus}, totalEventos={completedEvents.Count}");
            currentEvent = null;
        }
    }

    private string BuildVergenceFileName(int gameNumber)
    {
        return "Estimulos.jsonl";
    }

    private string BuildVergenceJsonlPayload()
    {
        StringBuilder sb = new StringBuilder(completedEvents.Count * 512);

        foreach (EyeVergenceEventAWS evt in completedEvents)
        {
            sb.Append(JsonUtility.ToJson(evt));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private IEnumerator UploadJsonFileCoroutine(string fileName, string content, Action<bool> onCompleted)
    {
        string uploadUrlEndpoint = uploadApiEndpoint.TrimEnd('/') + "/upload-url";
        LogDebug($"Solicitando URL firmada en {uploadUrlEndpoint} para file={fileName}");

        UploadUrlRequest requestBody = new UploadUrlRequest { file_name = fileName };
        byte[] requestBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody));

        using (UnityWebRequest presignRequest = new UnityWebRequest(uploadUrlEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            presignRequest.uploadHandler = new UploadHandlerRaw(requestBytes);
            presignRequest.downloadHandler = new DownloadHandlerBuffer();
            presignRequest.SetRequestHeader("Content-Type", "application/json");

            yield return presignRequest.SendWebRequest();

            if (presignRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AWS][Vergence] Error solicitando URL firmada: {presignRequest.error}");
                LogDebug($"Error URL firmada. result={presignRequest.result}, code={presignRequest.responseCode}, error={presignRequest.error}");
                onCompleted?.Invoke(false);
                yield break;
            }

            LogDebug($"URL firmada recibida. responseCode={presignRequest.responseCode}");

            UploadUrlResponse urlResponse = JsonUtility.FromJson<UploadUrlResponse>(presignRequest.downloadHandler.text);
            if (urlResponse == null || string.IsNullOrEmpty(urlResponse.upload_url))
            {
                Debug.LogError("[AWS][Vergence] La API no devolvio una upload_url valida.");
                LogDebug($"Respuesta de presign invalida. body={presignRequest.downloadHandler.text}");
                onCompleted?.Invoke(false);
                yield break;
            }

            byte[] jsonBytes = Encoding.UTF8.GetBytes(content);

            using (UnityWebRequest uploadRequest = new UnityWebRequest(urlResponse.upload_url, UnityWebRequest.kHttpVerbPUT))
            {
                uploadRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                uploadRequest.downloadHandler = new DownloadHandlerBuffer();
                uploadRequest.SetRequestHeader("Content-Type", "application/json");

                yield return uploadRequest.SendWebRequest();

                if (uploadRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[AWS][Vergence] Error subiendo archivo a S3: {uploadRequest.error}");
                    LogDebug($"Error PUT S3. result={uploadRequest.result}, code={uploadRequest.responseCode}, error={uploadRequest.error}");
                    onCompleted?.Invoke(false);
                    yield break;
                }

                if (logUploadSuccess)
                {
                    Debug.Log($"[AWS][Vergence] Subida completada: {fileName} ({urlResponse.file_key})");
                }

                LogDebug($"Subida completada correctamente. file={fileName}, file_key={urlResponse.file_key}, code={uploadRequest.responseCode}");

                onCompleted?.Invoke(true);
            }
        }
    }

    private void AppendStats(GameStats gameStats)
    {
        string statsJson = JsonUtility.ToJson(gameStats);

        if (!File.Exists(statsPath))
        {
            File.WriteAllText(statsPath, "{\"Items\":[" + statsJson + "]}");
            return;
        }

        using (FileStream fs = new FileStream(statsPath, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Seek(-2, SeekOrigin.End);
            byte[] contentBytes = Encoding.UTF8.GetBytes("," + statsJson + "]}");
            fs.Write(contentBytes, 0, contentBytes.Length);
            fs.Flush();
        }
    }

    private string ClassifyStimulus(string name)
    {
        if (name.StartsWith("ship-pirate-small") || name.StartsWith("ship-pirate-medium") || name.StartsWith("ship-pirate-large"))
            return "Go";

        if (name.StartsWith("ship-small") || name.StartsWith("ship-medium") || name.StartsWith("ship-large"))
            return "NoGo";

        if (name.StartsWith("MountainRigh") || name.StartsWith("MountainLeft") || name.StartsWith("MountainIsland"))
            return "Mountains";

        if (name.StartsWith("Pier"))
            return "Pier";

        if (name.StartsWith("Island"))
            return "Island";

        if (name.StartsWith("Castle"))
            return "Castle";

        if (name.StartsWith("Palms"))
            return "Palms";

        if (name.StartsWith("Background-Timer") || name.StartsWith("Timer") || name.StartsWith("Timer-Plane"))
            return "Timer";

        if (name.StartsWith("Water"))
            return "Water";

        if (name.StartsWith("ship-large-health"))
            return "Other";

        return "Other";
    }

    private void RequestStoragePermissions()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            Permission.RequestUserPermission(Permission.ExternalStorageWrite);
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            Permission.RequestUserPermission(Permission.ExternalStorageRead);
    }

    private void OnDestroy()
    {
        LogDebug("StatsSavedAWS.OnDestroy() ejecutado.");

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void LogDebug(string message)
    {
        string fullMessage = $"[{DateTime.UtcNow:O}] {message}";

        if (mirrorLogsToConsole)
        {
            Debug.Log("[AWS][Vergence][Trace] " + message);
        }

        if (!saveDebugLogToFile)
        {
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(debugLogPath))
            {
                string dataPath = Application.persistentDataPath;
                debugLogPath = Path.Combine(dataPath, debugLogFileName);
            }

            File.AppendAllText(debugLogPath, fullMessage + Environment.NewLine);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AWS][Vergence] No se pudo escribir log persistente: " + e.Message);
        }
    }

    [Serializable]
    private class UploadUrlRequest
    {
        public string file_name;
    }

    [Serializable]
    private class UploadUrlResponse
    {
        public string upload_url;
        public string file_key;
    }

    [Serializable]
    private class EyeVergenceEventAWS
    {
        public string stimulus;
        public string type;
        public bool wasShot;
        public float startTime;
        public float endTime;
        public float shipAliveTime = -1f;
        public float shipShotTime = -1f;
        public int goShipsAlive;
        public int noGoShipsAlive;
        public int goShipsSpawned;
        public int noGoShipsSpawned;
        public int goShipsEliminated;
        public int noGoShipsEliminated;
        public int goShipsEscaped;
        public int currentGoStreak;
    }
}

#endif
