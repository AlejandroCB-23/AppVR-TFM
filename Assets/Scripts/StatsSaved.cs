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

        EyeManager.Instance.GetLeftEyePupilDiameter(out float leftPupil);
        EyeManager.Instance.GetRightEyePupilDiameter(out float rightPupil);
        EyeManager.Instance.GetLeftEyeOpenness(out float leftOpenness);
        EyeManager.Instance.GetRightEyeOpenness(out float rightOpenness);

        bool hitCollider = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity);
        float currentTime = Time.time - recordingStartTime;

        if (hitCollider)
        {
            // --- CASO 1: IMPACTO CON OBJETO ---
            string stimulusName = hit.collider.transform.parent != null ? hit.collider.transform.parent.name : hit.collider.gameObject.name;
            string stimulusType = ClassifyStimulus(stimulusName);

            float distance = Vector3.Distance(ray.origin, hit.point);
            float vergenceAngle = VergenceFunctions.CalculateVergenceAngle(interpupillaryDistance, distance);

            Vector3 combinedOrigin = Vector3.zero;
            Vector3 combinedDirection = Vector3.forward;
            EyeData.TryGetCombinedEyeWorldData(out combinedOrigin, out combinedDirection);

            EyeDataSample eyeDataSample = new EyeDataSample(currentTime, vergenceAngle, distance, combinedOrigin, combinedDirection, leftPupil, rightPupil, leftOpenness, rightOpenness);

            if (currentEvent != null && currentEvent.stimulus == stimulusName)
            {
                currentEvent.eyeDataSamples.Add(eyeDataSample);
                currentEvent.endTime = currentTime;
            }
            else
            {
                FinalizePreviousEvent();
                currentEvent = CreateNewEvent(stimulusName, stimulusType, currentTime, eyeDataSample);
            }
        }
        else
        {
            if (currentEvent != null && currentEvent.stimulus == "Sky")
            {
                EyeDataSample skySample = new EyeDataSample(currentTime, 0f, 1000f, ray.origin, ray.direction, leftPupil, rightPupil, leftOpenness, rightOpenness);
                currentEvent.eyeDataSamples.Add(skySample);
                currentEvent.endTime = currentTime;
            }
            else
            {
                FinalizePreviousEvent();

                EyeDataSample initialSkySample = new EyeDataSample(currentTime, 0f, 1000f, ray.origin, ray.direction, leftPupil, rightPupil, leftOpenness, rightOpenness);
                currentEvent = CreateNewEvent("Sky", "Sky", currentTime, initialSkySample);
            }
        }
    }

    private int CountShipsByType(string type)
    {
        int count = 0;
        foreach (var ship in GameObject.FindGameObjectsWithTag("Ship"))
        {
            Ship shipScript = ship.GetComponent<Ship>();
            if (shipScript != null && (shipScript.IsSinking() || shipScript.HasEscaped()))
                continue;
            if (ClassifyStimulus(ship.name) == type)
                count++;
        }
        return count;
    }

    private EyeVergenceEvent CreateNewEvent(string name, string type, float time, EyeDataSample sample)
    {
        float aliveTime = -1f;
        if (type == "Go" || type == "NoGo")
        {
            foreach (var shipObj in GameObject.FindGameObjectsWithTag("Ship"))
            {
                if (shipObj.name == name)
                {
                    Ship shipScript = shipObj.GetComponent<Ship>();
                    if (shipScript != null)
                        aliveTime = shipScript.GetAliveTime();
                    break;
                }
            }
        }

        return new EyeVergenceEvent
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
            currentGoStreak = StatsTracker.Instance != null ? StatsTracker.Instance.GetCurrentPirateStreak() : 0,
            eyeDataSamples = new List<EyeDataSample> { sample }
        };
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

    // Each EyeDataSample is ~225 bytes as JSON. UDP max payload = 65507 bytes.
    // At 66 samples/s, a fixation of >4s overflows a single packet.
    // Chunk into 150-sample slices (~33KB each) to stay well under the limit.
    private const int MaxSamplesPerPacket = 150;

    private void SendVergenceEvents()
    {
        if (completedEvents.Count == 0)
            return;

        try
        {
            foreach (var evt in completedEvents)
            {
                int totalSamples = evt.eyeDataSamples != null ? evt.eyeDataSamples.Count : 0;

                if (totalSamples <= MaxSamplesPerPacket)
                {
                    string jsonData = JsonUtility.ToJson(evt);
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonData);
                    udpVergenceClient.Send(bytes, bytes.Length, vergenceEndPoint);
                }
                else
                {
                    for (int startIdx = 0; startIdx < totalSamples; startIdx += MaxSamplesPerPacket)
                    {
                        int chunkSize = Mathf.Min(MaxSamplesPerPacket, totalSamples - startIdx);
                        var chunkEvent = new EyeVergenceEvent
                        {
                            stimulus = evt.stimulus,
                            type = evt.type,
                            wasShot = evt.wasShot,
                            startTime = evt.startTime,
                            endTime = evt.endTime,
                            shipAliveTime = evt.shipAliveTime,
                            shipShotTime = evt.shipShotTime,
                            goShipsAlive = evt.goShipsAlive,
                            noGoShipsAlive = evt.noGoShipsAlive,
                            goShipsSpawned = evt.goShipsSpawned,
                            noGoShipsSpawned = evt.noGoShipsSpawned,
                            goShipsEliminated = evt.goShipsEliminated,
                            noGoShipsEliminated = evt.noGoShipsEliminated,
                            goShipsEscaped = evt.goShipsEscaped,
                            currentGoStreak = evt.currentGoStreak,
                            eyeDataSamples = evt.eyeDataSamples.GetRange(startIdx, chunkSize)
                        };

                        string jsonChunk = JsonUtility.ToJson(chunkEvent);
                        byte[] chunkBytes = Encoding.UTF8.GetBytes(jsonChunk);
                        udpVergenceClient.Send(chunkBytes, chunkBytes.Length, vergenceEndPoint);
                    }
                }
            }
            completedEvents.Clear();
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to send vergence data: " + e.Message);
            completedEvents.Clear(); // Always clear to prevent blocking all future sends
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

    public void MarkShot(Ship ship = null)
    {
        if (currentEvent != null)
        {
            currentEvent.wasShot = true;
            if (ship != null)
                currentEvent.shipShotTime = ship.GetAliveTime();
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

    public float leftPupilDiameter;
    public float rightPupilDiameter;
    public float leftEyeOpenness;
    public float rightEyeOpenness;

    public EyeDataSample(float t, float v, float distance, Vector3 origin, Vector3 direction, float leftPupil, float rightPupil, float leftOpenness, float rightOpenness)
    {
        time = t;
        vergence = v;
        distanceToTarget = distance;
        combinedEyeOrigin = origin;
        combinedEyeDirection = direction;
        leftPupilDiameter = leftPupil;
        rightPupilDiameter = rightPupil;
        leftEyeOpenness = leftOpenness;
        rightEyeOpenness = rightOpenness;
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









