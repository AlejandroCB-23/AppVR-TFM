#if WAVE_SDK_IMPORTED

using System.Collections.Generic;
using System;
using UnityEngine;
using Wave.Essence.Eye;
using System.Net.Sockets;
using System.Text;
using System.Net;
using Alex.OcularVergenceLibrary;
using System.Threading.Tasks;
using System.IO;
using UnityEngine.Android;
using System.Collections;
using static HeatMapData;

public class StatsSaved: MonoBehaviour
{
    public static StatsSaved Instance { get; private set; }

    [Header("Network Configuration")]
    public string serverIP = "192.168.0.00";
    public int vergencePort = 5007;

    [Header("Data Collection Settings")]
    public float captureInterval = 0.015f;

    [Header("Storage Configuration")]
    private string statsPath;

    private UdpClient udpVergenceClient;
    private IPEndPoint vergenceEndPoint;

    private float lastCaptureTime = 0f;
    private EyeVergenceEvent currentEvent = null;
    private List<EyeVergenceEvent> completedEvents = new List<EyeVergenceEvent>();
    private float recordingStartTime = -1f;
    private int currentGameNumber;

    void Awake()
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

    void Start()
    {
        RequestStoragePermissions();
        InitializeStorage();
        InitializeNetwork();

        if (EyeManager.Instance != null)
        {
            EyeManager.Instance.EnableEyeTracking = true;
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
    }

    private void InitializeNetwork()
    {
        try
        {
            udpVergenceClient = new UdpClient();
            vergenceEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), vergencePort);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to initialize vergence network: " + e.Message);
        }
    }

    void Update()
    {
        if (RecordingState.IsRecording && recordingStartTime < 0)
        {
            recordingStartTime = Time.time;
        }

        if (!RecordingState.IsRecording && recordingStartTime >= 0)
        {
            FinalizePreviousEvent();
            SendVergenceEvents();
            StartCoroutine(SaveFinalStatsCoroutine());
            recordingStartTime = -1f;
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
        yield return SaveFinalStatsAsync().AsCoroutine();
    }

    void CaptureEyeTrackingData()
    {
        if (!VergenceFunctions.TryGetInterpupillaryDistance(out float interpupillaryDistance))
            return;

        if (!VergenceFunctions.TryGetCombinedEyeRay(out Ray ray))
            return;

        bool hitCollider = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity);

        if (hitCollider)
        {
            string stimulusName = hit.collider.gameObject.name;
            string stimulusType = ClassifyStimulus(stimulusName);

            float distance = Vector3.Distance(ray.origin, hit.point);
            float vergenceAngle = VergenceFunctions.CalculateVergenceAngle(interpupillaryDistance, distance);
            float currentTime = Time.time - recordingStartTime;

            Vector3 combinedOrigin = Vector3.zero;
            Vector3 combinedDirection = Vector3.forward;
            EyeData.TryGetCombinedEyeWorldData(out combinedOrigin, out combinedDirection);

            EyeDataSample eyeDataSample = new EyeDataSample(
                currentTime,
                vergenceAngle,
                distance,
                combinedOrigin,
                combinedDirection
            );

            if (currentEvent != null && currentEvent.stimulus == stimulusName)
            {
                currentEvent.eyeDataSamples.Add(eyeDataSample);
                currentEvent.endTime = currentTime;
            }
            else
            {
                FinalizePreviousEvent();

                currentEvent = new EyeVergenceEvent
                {
                    stimulus = stimulusName,
                    type = stimulusType,
                    wasShot = false,
                    startTime = currentTime,
                    endTime = currentTime,
                    eyeDataSamples = new List<EyeDataSample> { eyeDataSample }
                };
            }
        }
        else
        {
            FinalizePreviousEvent();
        }
    }

    private void FinalizePreviousEvent()
    {
        if (currentEvent != null)
        {
            if (currentEvent.eyeDataSamples != null && currentEvent.eyeDataSamples.Count > 0)
            {
                completedEvents.Add(currentEvent);
                SendVergenceEvents();
            }
            currentEvent = null;
        }
    }

    private void SendVergenceEvents()
    {
        if (completedEvents.Count == 0)
            return;

        try
        {
            foreach (var evt in completedEvents)
            {
                string jsonData = JsonUtility.ToJson(evt);
                byte[] bytes = Encoding.UTF8.GetBytes(jsonData);
                udpVergenceClient.Send(bytes, bytes.Length, vergenceEndPoint);
            }
            completedEvents.Clear();
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to send vergence data: " + e.Message);
        }
    }

    private string ClassifyStimulus(string name)
    {
        if (name.StartsWith("ship-pirate-small") || name.StartsWith("ship-pirate-medium") || name.StartsWith("ship-pirate-large"))
            return "Go";

        if (name.StartsWith("ship-small") || name.StartsWith("ship-medium") || name.StartsWith("ship-large"))
            return "NoGo";

        if (name.StartsWith("Water") || name.StartsWith("ship-large-health"))
            return "Other";

        return "Unknown";
    }

    public void MarkShot()
    {
        if (currentEvent != null)
        {
            currentEvent.wasShot = true;
        }
    }

    public async Task SaveFinalStatsAsync()
    {
        FinalizePreviousEvent();
        SendVergenceEvents();

        try
        {
            var stats = StatsTracker.Instance;

            var gameStats = new GameStats
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

            await AppendStatsAsync(gameStats);

            currentGameNumber++;
            PlayerPrefs.SetInt("GameNumber", currentGameNumber);
            PlayerPrefs.Save();
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error saving final stats: " + e.Message);
        }
    }

    private async Task AppendStatsAsync(GameStats gameStats)
    {
        string statsJson = JsonUtility.ToJson(gameStats);
        try
        {
            if (!File.Exists(statsPath))
            {
                File.WriteAllText(statsPath, "{\"Items\":[" + statsJson + "]}");
            }
            else
            {
                using (FileStream fs = new FileStream(statsPath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(-2, SeekOrigin.End);
                    byte[] contentBytes = Encoding.UTF8.GetBytes("," + statsJson + "]}");
                    await fs.WriteAsync(contentBytes, 0, contentBytes.Length);
                    await fs.FlushAsync();
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error appending stats to file: " + e.Message);
        }
    }

    void RequestStoragePermissions()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageWrite))
            Permission.RequestUserPermission(Permission.ExternalStorageWrite);
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
            Permission.RequestUserPermission(Permission.ExternalStorageRead);
    }

    void OnDestroy()
    {
        if (udpVergenceClient != null)
        {
            udpVergenceClient.Close();
            udpVergenceClient = null;
        }
    }
}

public static class TaskExtensions
{
    public static IEnumerator AsCoroutine(this Task task)
    {
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            throw task.Exception;
        }
    }
}


[Serializable]
public class EyeVergenceEvent
{
    public string stimulus;
    public string type;
    public bool wasShot;
    public float startTime;
    public float endTime;
    public List<EyeDataSample> eyeDataSamples;
}

[Serializable]
public class EyeDataSample
{
    public float time;
    public float vergence;
    public float distanceToTarget;
    public Vector3 combinedEyeOrigin;
    public Vector3 combinedEyeDirection;

    public EyeDataSample(float t, float v, float distance, Vector3 origin, Vector3 direction)
    {
        time = t;
        vergence = v;
        distanceToTarget = distance;
        combinedEyeOrigin = origin;
        combinedEyeDirection = direction;
    }
}

[Serializable]
public class GameStats
{
    public int gameNumber;
    public int piratesEliminated;
    public int fishingEliminated;
    public int bestPirateStreak;
    public float maxTimeWithoutFishing;
    public float shortestTimeToSinkPirate;
    public float avgTimeToSinkPirate;
    public int piratesEscaped;
}

#endif









