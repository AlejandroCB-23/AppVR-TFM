#if WAVE_SDK_IMPORTED

using UnityEngine;
using UnityEngine.InputSystem;
using System.Net.Sockets;
using System.Text;

public class GameManager : MonoBehaviour
{
    public float gameDuration = 120f;
    private float timer;
    private bool gameEnded = false;

    public GameObject endStatsCanvas;
    public GameObject Timer;
    public float delayBeforeShowingStats = 1f;

    [Header("Tic Tac Sound")]
    public AudioClip ticTacClip;
    private AudioSource ticTacSource;
    private bool ticTacStarted = false;

    [Header("End Bell Sound")]
    public AudioClip bellClip;
    private AudioSource bellSource;

    public float TimeRemaining => timer;

    [SerializeField] private GazeDetector gazeShipDetector;
    public InputAction fireAction;

    private Stats statsUIManager;

    private UdpClient udpClient;
    public string externalAppIP = "192.168.0.00"; 
    public int externalAppPort = 5005;

    void Start()
    {
        timer = gameDuration;

        ticTacSource = gameObject.AddComponent<AudioSource>();
        ticTacSource.clip = ticTacClip;
        ticTacSource.loop = true;
        ticTacSource.playOnAwake = false;

        bellSource = gameObject.AddComponent<AudioSource>();
        bellSource.clip = bellClip;
        bellSource.loop = false;
        bellSource.playOnAwake = false;

        if (endStatsCanvas != null)
            endStatsCanvas.SetActive(false);

        if (endStatsCanvas != null)
            statsUIManager = endStatsCanvas.GetComponent<Stats>();


        udpClient = new UdpClient();
    }

    void Update()
    {
        if (gameEnded)
            return;

        timer -= Time.deltaTime;

        if (!ticTacStarted && timer <= 30f)
        {
            ticTacStarted = true;
            ticTacSource.Play();
        }

        if (timer <= 0f)
        {
            timer = 0f;
            gameEnded = true;

            SendExternalMessage("state:end");
            HeatMapData.RecordingState.IsRecording = false;

            if (ticTacSource.isPlaying)
                ticTacSource.Stop();

            bellSource.Play();

            foreach (var ship in GameObject.FindGameObjectsWithTag("Ship"))
            {
                Destroy(ship);
            }

            Invoke(nameof(ShowEndStats), delayBeforeShowingStats);
            StatsTracker.Instance.gameOver = true;
        }
    }

    void ShowEndStats()
    {
        GameSettings.CurrentShootingMode = GameSettings.DisparoMode.Both;

        Timer.SetActive(false);

        if (endStatsCanvas != null)
        {
            endStatsCanvas.SetActive(true);

            if (gazeShipDetector != null)
            {
                gazeShipDetector.EnableControls();
            }

            if (statsUIManager != null)
            {
                statsUIManager.UpdateStats(
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
            Debug.LogWarning($"[UDP] Failed to send '{message}' to {externalAppIP}:{externalAppPort} ? {e.Message}");
        }
    }



    void OnApplicationQuit()
    {
        udpClient?.Dispose();
    }
}
#endif





