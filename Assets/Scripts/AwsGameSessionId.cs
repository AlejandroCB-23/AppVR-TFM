#if WAVE_SDK_IMPORTED

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public static class AwsGameSessionId
{
    public const string TestSceneName = "ModoTest";

    private static int currentGameId = -1;
    private static bool requestInProgress = false;

    public static int CurrentGameId => currentGameId;

    public static bool IsEnabledForCurrentScene()
    {
        return SceneManager.GetActiveScene().name == TestSceneName;
    }

    public static void ResetSession()
    {
        currentGameId = -1;
        requestInProgress = false;
    }

    public static IEnumerator EnsureGameIdCoroutine(string uploadApiEndpoint, Action<int> onCompleted = null, Action<string> log = null)
    {
        if (currentGameId > 0)
        {
            onCompleted?.Invoke(currentGameId);
            yield break;
        }

        if (requestInProgress)
        {
            while (requestInProgress)
            {
                yield return null;
            }

            onCompleted?.Invoke(currentGameId);
            yield break;
        }

        requestInProgress = true;

        string endpoint = uploadApiEndpoint.TrimEnd('/') + "/upload-url";
        ReserveGameIdRequest body = new ReserveGameIdRequest { reserve_only = true };
        byte[] requestBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

        using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(requestBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                log?.Invoke($"Error reservando game_id en AWS: {request.error}");
                requestInProgress = false;
                onCompleted?.Invoke(currentGameId);
                yield break;
            }

            ReserveGameIdResponse response = JsonUtility.FromJson<ReserveGameIdResponse>(request.downloadHandler.text);
            if (response == null || response.game_id <= 0)
            {
                log?.Invoke("Respuesta invalida al reservar game_id.");
                requestInProgress = false;
                onCompleted?.Invoke(currentGameId);
                yield break;
            }

            currentGameId = response.game_id;
            log?.Invoke($"game_id reservado: {currentGameId}");
        }

        requestInProgress = false;
        onCompleted?.Invoke(currentGameId);
    }

    [Serializable]
    private class ReserveGameIdRequest
    {
        public bool reserve_only;
    }

    [Serializable]
    private class ReserveGameIdResponse
    {
        public int game_id;
        public bool reserved;
    }
}

#endif
