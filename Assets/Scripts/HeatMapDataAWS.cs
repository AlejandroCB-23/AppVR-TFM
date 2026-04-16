#if WAVE_SDK_IMPORTED

using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Alex.OcularVergenceLibrary;
using Wave.Essence.Eye;

public class HeatMapDataAWS : MonoBehaviour
{
    public static HeatMapDataAWS Instance { get; private set; }

    [Header("AWS Upload Configuration")]
    public string uploadApiEndpoint = "https://f2ytxnhuo0.execute-api.us-east-1.amazonaws.com";
    public bool logUploadSuccess = true;

    [Header("Data Collection Settings")]
    public float dataCollectionRate = 30f;
    public Camera targetCamera;

    [Header("Gaze Settings")]
    public float fallbackGazeDistance = 10f;
    public float noHitDistance = 1000000f;

    private readonly List<HeatmapDataPointAWS> bufferedData = new List<HeatmapDataPointAWS>();

    private float lastDataTime;
    private int frameCounter = 0;
    private float recordingStartTime = -1f;
    private float sessionDurationSeconds = -1f;
    private bool uploadInProgress = false;
    private bool uploadedThisSession = false;

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
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (HeatMapData.RecordingState.IsRecording && recordingStartTime < 0f)
        {
            recordingStartTime = Time.time;
            sessionDurationSeconds = -1f;
            frameCounter = 0;
            uploadedThisSession = false;
            bufferedData.Clear();
        }

        if (HeatMapData.RecordingState.IsRecording && Time.time - lastDataTime >= 1f / dataCollectionRate)
        {
            CollectDataPoint();
            lastDataTime = Time.time;
        }

        if (!HeatMapData.RecordingState.IsRecording && recordingStartTime >= 0f)
        {
            sessionDurationSeconds = Mathf.Max(0f, Time.time - recordingStartTime);
            recordingStartTime = -1f;

            if (!uploadedThisSession && !uploadInProgress)
            {
                StartCoroutine(SavePendingDataCoroutine(null));
            }
        }
    }

    public Task SavePendingDataAsync()
    {
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

        if (!isActiveAndEnabled)
        {
            tcs.SetResult(true);
            return tcs.Task;
        }

        StartCoroutine(SavePendingDataCoroutine(tcs));
        return tcs.Task;
    }

    private void CollectDataPoint()
    {
        HeatmapDataPointAWS heatmapData = new HeatmapDataPointAWS
        {
            timestamp = Time.time - recordingStartTime,
            frameNumber = frameCounter++,
            deltaTime = Time.deltaTime
        };

        if (targetCamera != null)
        {
            heatmapData.headPosition = targetCamera.transform.position;
            heatmapData.headRotation = targetCamera.transform.rotation;
            heatmapData.cameraFOV = targetCamera.fieldOfView;
        }

        if (!CollectEyeTrackingData(ref heatmapData))
        {
            return;
        }

        CollectGazeScreenPosition(ref heatmapData);

        bufferedData.Add(heatmapData);
    }

    private bool CollectEyeTrackingData(ref HeatmapDataPointAWS data)
    {
        if (EyeData.TryGetWorldEyeData(out EyeTrackingData eyeData))
        {
            data.hasEyeTracking = true;
            data.leftEyeOrigin = eyeData.leftEyeOrigin;
            data.leftEyeDirection = eyeData.leftEyeDirection;
            data.rightEyeOrigin = eyeData.rightEyeOrigin;
            data.rightEyeDirection = eyeData.rightEyeDirection;
            data.combinedEyeOrigin = eyeData.combinedEyeOrigin;
            data.combinedEyeDirection = eyeData.combinedEyeDirection;

            if (EyeManager.Instance != null)
            {
                EyeManager.Instance.GetLeftEyePupilDiameter(out data.leftPupilDiameter);
                EyeManager.Instance.GetRightEyePupilDiameter(out data.rightPupilDiameter);
                EyeManager.Instance.GetLeftEyeOpenness(out data.leftEyeOpenness);
                EyeManager.Instance.GetRightEyeOpenness(out data.rightEyeOpenness);
            }

            PopulateVergenceAndDistance(ref data);
            return true;
        }

        data.hasEyeTracking = false;
        return false;
    }

    private void PopulateVergenceAndDistance(ref HeatmapDataPointAWS data)
    {
        Ray combinedRay = new Ray(data.combinedEyeOrigin, data.combinedEyeDirection);
        bool hitCollider = Physics.Raycast(combinedRay, out RaycastHit hit, Mathf.Infinity);

        data.isSkyTarget = !hitCollider;
        data.distanceToTarget = hitCollider ? Vector3.Distance(combinedRay.origin, hit.point) : Mathf.Max(1f, noHitDistance);

        if (hitCollider)
        {
            data.targetHitPoint = hit.point;
        }
        else
        {
            // Preserve gaze direction in world space even when no collider is hit.
            data.targetHitPoint = combinedRay.origin + (combinedRay.direction * Mathf.Max(1f, noHitDistance));
        }

        if (data.isSkyTarget)
        {
            data.vergence = 0f;
            return;
        }

        if (VergenceFunctions.TryGetInterpupillaryDistance(out float interpupillaryDistance))
        {
            data.vergence = VergenceFunctions.CalculateVergenceAngle(interpupillaryDistance, data.distanceToTarget);
        }
        else
        {
            data.vergence = 0f;
        }
    }

    private void CollectGazeScreenPosition(ref HeatmapDataPointAWS data)
    {
        if (targetCamera == null)
        {
            return;
        }

        Vector3 targetViewportPoint = targetCamera.WorldToViewportPoint(data.targetHitPoint);
        data.targetLocal = new Vector2(targetViewportPoint.x, targetViewportPoint.y);
        data.targetLocalDepth = targetViewportPoint.z;
        data.isTargetLocalValid = targetViewportPoint.z > 0f;

        Vector3 gazeOrigin = data.combinedEyeOrigin;
        Vector3 gazeDirection = data.combinedEyeDirection;

        Vector3 projectedPoint = gazeOrigin + gazeDirection * fallbackGazeDistance;
        data.gazeWorldPoint = projectedPoint;

        Vector3 screenPoint = targetCamera.WorldToScreenPoint(projectedPoint);
        data.gazeScreenPosition = new Vector2(screenPoint.x, screenPoint.y);
        data.gazeScreenDepth = screenPoint.z;

        float normalizedX = Mathf.Clamp01(screenPoint.x / Screen.width);
        float normalizedY = Mathf.Clamp01(1f - (screenPoint.y / Screen.height));
        data.gazeNormalizedPosition = new Vector2(normalizedX, normalizedY);

        data.screenWidth = Screen.width;
        data.screenHeight = Screen.height;
        data.isGazeValid = screenPoint.z > 0f;
    }

    private IEnumerator SavePendingDataCoroutine(TaskCompletionSource<bool> tcs)
    {
        if (!AwsGameSessionId.IsEnabledForCurrentScene())
        {
            uploadInProgress = false;
            uploadedThisSession = true;
            tcs?.SetResult(true);
            yield break;
        }

        if (uploadInProgress)
        {
            while (uploadInProgress)
            {
                yield return null;
            }

            tcs?.SetResult(uploadedThisSession);
            yield break;
        }

        if (uploadedThisSession)
        {
            tcs?.SetResult(true);
            yield break;
        }

        uploadInProgress = true;

        if (bufferedData.Count == 0)
        {
            uploadInProgress = false;
            uploadedThisSession = true;
            tcs?.SetResult(true);
            yield break;
        }

        NormalizeBufferedTimeBounds();

        int sessionGameId = AwsGameSessionId.CurrentGameId;
        if (sessionGameId <= 0)
        {
            yield return StartCoroutine(AwsGameSessionId.EnsureGameIdCoroutine(uploadApiEndpoint));
            sessionGameId = AwsGameSessionId.CurrentGameId;
        }

        if (sessionGameId <= 0)
        {
            uploadInProgress = false;
            tcs?.SetResult(false);
            yield break;
        }

        string fileName = BuildHeatmapFileName();
        string payload = BuildJsonlPayload();

        bool uploadSucceeded = false;

        yield return StartCoroutine(UploadJsonFileCoroutine(fileName, payload, sessionGameId, success =>
        {
            uploadSucceeded = success;
        }));

        if (uploadSucceeded)
        {
            bufferedData.Clear();
            uploadedThisSession = true;
        }

        uploadInProgress = false;
        tcs?.SetResult(uploadSucceeded);
    }

    private void NormalizeBufferedTimeBounds()
    {
        if (bufferedData.Count == 0)
        {
            return;
        }

        bufferedData[0].timestamp = 0f;

        if (sessionDurationSeconds >= 0f)
        {
            int lastIndex = bufferedData.Count - 1;
            bufferedData[lastIndex].timestamp = Mathf.Max(bufferedData[0].timestamp, sessionDurationSeconds);
        }
    }

    private string BuildHeatmapFileName()
    {
        return "HeatMap.jsonl";
    }

    private string BuildJsonlPayload()
    {
        StringBuilder sb = new StringBuilder(bufferedData.Count * 256);

        for (int i = 0; i < bufferedData.Count; i++)
        {
            sb.Append(JsonUtility.ToJson(bufferedData[i]));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private IEnumerator UploadJsonFileCoroutine(string fileName, string content, int gameId, System.Action<bool> onCompleted)
    {
        string uploadUrlEndpoint = uploadApiEndpoint.TrimEnd('/') + "/upload-url";

        UploadUrlRequest requestBody = new UploadUrlRequest { file_name = fileName, game_id = gameId };
        byte[] requestBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody));

        using (UnityWebRequest presignRequest = new UnityWebRequest(uploadUrlEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            presignRequest.uploadHandler = new UploadHandlerRaw(requestBytes);
            presignRequest.downloadHandler = new DownloadHandlerBuffer();
            presignRequest.SetRequestHeader("Content-Type", "application/json");

            yield return presignRequest.SendWebRequest();

            if (presignRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AWS][Heatmap] Error solicitando URL firmada: {presignRequest.error}");
                onCompleted?.Invoke(false);
                yield break;
            }

            UploadUrlResponse urlResponse = JsonUtility.FromJson<UploadUrlResponse>(presignRequest.downloadHandler.text);
            if (urlResponse == null || string.IsNullOrEmpty(urlResponse.upload_url))
            {
                Debug.LogError("[AWS][Heatmap] La API no devolvio una upload_url valida.");
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
                    Debug.LogError($"[AWS][Heatmap] Error subiendo archivo a S3: {uploadRequest.error}");
                    onCompleted?.Invoke(false);
                    yield break;
                }

                if (logUploadSuccess)
                {
                    Debug.Log($"[AWS][Heatmap] Subida completada: {fileName} ({urlResponse.file_key})");
                }

                onCompleted?.Invoke(true);
            }
        }
    }

    [System.Serializable]
    private class UploadUrlRequest
    {
        public string file_name;
        public int game_id;
    }

    [System.Serializable]
    private class UploadUrlResponse
    {
        public string upload_url;
        public string file_key;
        public int game_id;
    }

    [System.Serializable]
    private class HeatmapDataPointAWS
    {
        public float timestamp;
        public int frameNumber;
        public float deltaTime;

        public Vector3 headPosition;
        public Quaternion headRotation;
        public float cameraFOV;

        public bool hasEyeTracking;
        public Vector3 leftEyeOrigin;
        public Vector3 leftEyeDirection;
        public Vector3 rightEyeOrigin;
        public Vector3 rightEyeDirection;
        public Vector3 combinedEyeOrigin;
        public Vector3 combinedEyeDirection;

        public float vergence;
        public float distanceToTarget;
        public float leftPupilDiameter;
        public float rightPupilDiameter;
        public float leftEyeOpenness;
        public float rightEyeOpenness;
        public bool isSkyTarget;
        public Vector3 targetHitPoint;
        public Vector2 targetLocal;
        public float targetLocalDepth;
        public bool isTargetLocalValid;

        public Vector3 gazeWorldPoint;
        public Vector2 gazeScreenPosition;
        public Vector2 gazeNormalizedPosition;
        public float gazeScreenDepth;
        public bool isGazeValid;

        public int screenWidth;
        public int screenHeight;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}

#endif
