#if WAVE_SDK_IMPORTED

using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Alex.OcularVergenceLibrary;

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

    private readonly List<HeatmapDataPoint> bufferedData = new List<HeatmapDataPoint>();

    private float lastDataTime;
    private int frameCounter = 0;
    private float recordingStartTime = -1f;
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
        HeatmapDataPoint heatmapData = new HeatmapDataPoint
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

    private bool CollectEyeTrackingData(ref HeatmapDataPoint data)
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
            return true;
        }

        data.hasEyeTracking = false;
        return false;
    }

    private void CollectGazeScreenPosition(ref HeatmapDataPoint data)
    {
        if (targetCamera == null)
        {
            return;
        }

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

        string fileName = BuildHeatmapFileName();
        string payload = BuildJsonlPayload();

        bool uploadSucceeded = false;

        yield return StartCoroutine(UploadJsonFileCoroutine(fileName, payload, success =>
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

    private IEnumerator UploadJsonFileCoroutine(string fileName, string content, System.Action<bool> onCompleted)
    {
        string uploadUrlEndpoint = uploadApiEndpoint.TrimEnd('/') + "/upload-url";

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
    }

    [System.Serializable]
    private class UploadUrlResponse
    {
        public string upload_url;
        public string file_key;
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
