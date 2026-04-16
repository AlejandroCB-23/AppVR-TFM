#if WAVE_SDK_IMPORTED

using UnityEngine;
using TMPro; 

public class Stats : MonoBehaviour
{
    [Header("References to the texts")]
    public TMP_Text piratesEliminatedText;
    public TMP_Text fishingVesselsEliminatedText;
    public TMP_Text pirateStreakText;
    public TMP_Text IDGAME;
    public TMP_Text shortestTimeSinkPirateText;
    public TMP_Text timeHalfDeletePirateText;
    public TMP_Text extraEscapedPirates;

    public void UpdateStats(int piratesEliminated, int fishingEliminated, int pirateStreak, int gameId,
                            float maxTimeWithoutFishing, float minTimeToSinkPirate, float avgTimeToSinkPirate, float extraStatEscapedPirates)
    {
        piratesEliminatedText.text = $"Piratas Eliminados: {piratesEliminated}";
        fishingVesselsEliminatedText.text = $"Pesqueros Eliminados: {fishingEliminated}";
        extraEscapedPirates.text = $"Piratas Escapados: {extraStatEscapedPirates}";
        pirateStreakText.text = $"Mejor Racha Pirata: {pirateStreak}";

        bool showAwsGameId = AwsGameSessionId.IsEnabledForCurrentScene();
        IDGAME.text = $"ID de Partida: {(showAwsGameId && gameId > 0 ? gameId.ToString() : "N/D")}";
        shortestTimeSinkPirateText.text = $"Menor Tiempo En Eliminar Pirata:\n{FormatTime(minTimeToSinkPirate)}";
        timeHalfDeletePirateText.text = $"Tiempo Medio En Eliminar Pirata:\n{FormatTime(avgTimeToSinkPirate)}";
    }

    private string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60);
        int secs = Mathf.FloorToInt(seconds % 60);
        return $"{minutes}:{secs:D2}";
    }
}
#endif