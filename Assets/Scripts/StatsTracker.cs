#if WAVE_SDK_IMPORTED
using System.Collections.Generic;
using UnityEngine;

public class StatsTracker : MonoBehaviour
{
    public static StatsTracker Instance;

    private int piratesEliminated = 0;
    private int fishingEliminated = 0;
    private int piratesEscaped = 0;
    private int livesLostToPirateEscapes = 0;
    private int currentPirateStreak = 0;
    private int bestPirateStreak = 0;
    private float lastFishingEliminatedTime = -1f;
    private float maxTimeWithoutFishing = 0f;
    private List<float> pirateSinkTimes = new List<float>();
    private float shortestPirateSinkTime = float.MaxValue;
    public bool gameOver = false;
    private float gameStartTime;
    private int fishingEliminatedAleatorio = 0;

    void Awake()
    {
        Instance = this;
        gameStartTime = Time.timeSinceLevelLoad;
        ResetAll();
    }

    public void RegisterShipElimination(bool isPirate, float spawnTime, bool isRed = false)
    {
        float currentTime = Time.timeSinceLevelLoad;

        if (isPirate)
        {
            piratesEliminated++;
            currentPirateStreak++;
            bestPirateStreak = Mathf.Max(bestPirateStreak, currentPirateStreak);

            float sinkTime = currentTime - spawnTime;
            pirateSinkTimes.Add(sinkTime);
            if (sinkTime < shortestPirateSinkTime)
                shortestPirateSinkTime = sinkTime;
        }
        else if (!isRed)
        {
            fishingEliminated++;
            fishingEliminatedAleatorio++;

            float interval = (lastFishingEliminatedTime >= 0f)
                ? currentTime - lastFishingEliminatedTime
                : currentTime - gameStartTime;
            maxTimeWithoutFishing = Mathf.Max(maxTimeWithoutFishing, interval);
            lastFishingEliminatedTime = currentTime;

            currentPirateStreak = 0;
        }
    }

    public void RegisterPirateEscape()
    {
        piratesEscaped++;
        livesLostToPirateEscapes++;
        currentPirateStreak = 0;
    }

    public int GetTotalLivesLost()
    {
        return fishingEliminatedAleatorio + livesLostToPirateEscapes;
    }

    public void RestoreFishingLife()
    {

        if (livesLostToPirateEscapes > 0)
        {
            livesLostToPirateEscapes--;
            FindObjectOfType<ModoAleatorio>()?.RestoreLife();
        }
        else if (fishingEliminatedAleatorio > 0)
        {
            fishingEliminatedAleatorio--;
            FindObjectOfType<ModoAleatorio>()?.RestoreLife();
        }
    }

    public float GetAverageTimeToSinkPirate()
    {
        if (pirateSinkTimes.Count == 0) return 0f;
        float total = 0f;
        foreach (var time in pirateSinkTimes)
            total += time;
        return total / pirateSinkTimes.Count;
    }

    public float GetMaxTimeWithoutFishing()
    {
        float now = Time.timeSinceLevelLoad;
        float sinceLastFishing = fishingEliminated == 0 ? now - gameStartTime : now - lastFishingEliminatedTime;
        float rawMax = Mathf.Max(maxTimeWithoutFishing, sinceLastFishing);

        bool lastIntervalIsMax = sinceLastFishing > maxTimeWithoutFishing;
        if (gameOver && lastIntervalIsMax)
        {
            rawMax -= 3f;
        }
        return Mathf.Max(0f, rawMax);
    }

    public int GetPiratesEliminated() => piratesEliminated;
    public int GetFishingEliminated() => fishingEliminated;
    public int GetBestPirateStreak() => bestPirateStreak;
    public int GetFishingEliminatedAleatorio() => fishingEliminatedAleatorio;

    public int GetPiratesEscaped() => piratesEscaped;

    public int GetLivesLostToPirateEscapes() => livesLostToPirateEscapes;

    public float GetShortestTimeToSinkPirate()
    {
        return pirateSinkTimes.Count == 0 ? 0f : shortestPirateSinkTime;
    }

    public void ResetAll()
    {
        piratesEliminated = 0;
        fishingEliminated = 0;
        piratesEscaped = 0;
        livesLostToPirateEscapes = 0;
        currentPirateStreak = 0;
        bestPirateStreak = 0;
        lastFishingEliminatedTime = -1f;
        maxTimeWithoutFishing = 0f;
        pirateSinkTimes.Clear();
        shortestPirateSinkTime = float.MaxValue;
        gameStartTime = Time.timeSinceLevelLoad;
        fishingEliminatedAleatorio = 0;
    }
}
#endif






