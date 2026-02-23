#if WAVE_SDK_IMPORTED

using UnityEngine;
using System.Net.Sockets;
using System.Text;

public class GameManagerModoAleatorio : MonoBehaviour
{
    public GameObject endStatsCanvas;
    public GameObject gameOverPanel;
    public float delayBeforeShowingStats = 3f;

    [Header("End Bell Sound")]
    public AudioClip bellClip;
    private AudioSource bellSource;

    private bool gameEnded = false;
    private Stats statsUIManager;

    private UdpClient udpClient;
    public string externalAppIP = "192.168.0.00";
    public int externalAppPort = 5005;

    void Start()
    {
        bellSource = gameObject.AddComponent<AudioSource>();
        bellSource.clip = bellClip;
        bellSource.loop = false;
        bellSource.playOnAwake = false;

        if (endStatsCanvas != null)
        {
            endStatsCanvas.SetActive(false);
            statsUIManager = endStatsCanvas.GetComponent<Stats>();
        }

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        udpClient = new UdpClient();
    }

    void Update()
    {
        if (gameEnded) return;

        ModoAleatorio modo = FindObjectOfType<ModoAleatorio>();
        if (modo != null && modo.GetLostLivesCount() >= 3)
        {
            EndGame();
        }
    }

    void EndGame()
    {
        gameEnded = true;
        StatsTracker.Instance.gameOver = true;

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        if (bellSource != null)
            bellSource.Play();

        foreach (var ship in GameObject.FindGameObjectsWithTag("Ship"))
        {
            Destroy(ship);
        }
        Invoke(nameof(ShowEndStats), delayBeforeShowingStats);

    }

    void ShowEndStats()
    {

        SendExternalMessage("state:end");
        HeatMapData.RecordingState.IsRecording = false;

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (endStatsCanvas != null)
        {
            endStatsCanvas.SetActive(true);

            statsUIManager?.UpdateStats(
                StatsTracker.Instance.GetPiratesEliminated(),
                StatsTracker.Instance.GetFishingEliminated(),
                StatsTracker.Instance.GetBestPirateStreak(),
                StatsTracker.Instance.GetMaxTimeWithoutFishing(),
                StatsTracker.Instance.GetShortestTimeToSinkPirate(),
                StatsTracker.Instance.GetAverageTimeToSinkPirate(),
                StatsTracker.Instance.GetPiratesEscaped()
            );
        }
    }

    void SendExternalMessage(string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpClient.Send(data, data.Length, externalAppIP, externalAppPort);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[UDP] Failed to send message '{message}': {e.Message}");
        }
    }


    void OnApplicationQuit()
    {
        udpClient?.Dispose();
    }
}
#endif



