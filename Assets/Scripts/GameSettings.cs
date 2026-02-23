#if WAVE_SDK_IMPORTED
public static class GameSettings
{
    public enum DisparoMode { OnlyView, OnlyController, Both }
    public static DisparoMode CurrentShootingMode = DisparoMode.Both;
}

#endif