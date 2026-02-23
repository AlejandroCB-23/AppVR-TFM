#if WAVE_SDK_IMPORTED
public class ShipSpawnEvent
{
    public float time;        
    public int lane;          
    public bool isPirate;     
    public int sizeIndex;    
    public float speed;       

    public ShipSpawnEvent(float t, int l, bool pirate, int size, float spd)
    {
        time = t;
        lane = l;
        isPirate = pirate;
        sizeIndex = size;
        speed = spd;
    }
}
#endif



