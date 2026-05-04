#if WAVE_SDK_IMPORTED

using System.Collections.Generic;
using UnityEngine;

public class ModoTest : MonoBehaviour
{
    public Transform[] spawnPoints;
    public Transform[] endPoints;
    public GameObject[] pirateShipPrefabs;
    public GameObject[] normalShipPrefabs;
    public GameObject circleIndicatorPrefab;

    private List<ShipSpawnEvent> schedule = new List<ShipSpawnEvent>();
    private float timer = 0f;
    private int nextEventIndex = 0;

    public GameManager gameManager;
    private bool gameEnded = false;
    private int shipCounter = 0;

    void Start()
    {
        StatsTracker.Instance.ResetAll();
        FindObjectOfType<GazeDetector>()?.ResetDetector();

        schedule.Clear();
        schedule.AddRange(BuildSchedule());

        if (gameManager == null)
        {
            gameManager = FindObjectOfType<GameManager>();
        }
    }

    void Update()
    {
        if (gameEnded)
        {
            return;
        }

        timer += Time.deltaTime;

        if (gameManager != null && timer >= gameManager.gameDuration)
        {
            gameEnded = true;
            CancelShipSpawning();
        }

        if (nextEventIndex < schedule.Count && timer >= schedule[nextEventIndex].time)
        {
            SpawnShip(schedule[nextEventIndex]);
            nextEventIndex++;
        }
    }

    void CancelShipSpawning()
    {
        schedule.Clear();
    }

    public void RemoveAllShips()
    {
        foreach (var ship in GameObject.FindGameObjectsWithTag("Ship"))
        {
            Destroy(ship);
        }
    }

    void SpawnShip(ShipSpawnEvent spawnEvent)
    {
        Transform spawnPoint = spawnPoints[spawnEvent.lane];
        Transform endPoint = endPoints[spawnEvent.lane];

        GameObject[] prefabArray = spawnEvent.isPirate ? pirateShipPrefabs : normalShipPrefabs;
        GameObject prefab = prefabArray[spawnEvent.sizeIndex];

        GameObject ship = Instantiate(prefab, spawnPoint.position, Quaternion.identity);
        ship.name = prefab.name + "_" + (++shipCounter);

        ship.transform.localScale = new Vector3(12f, 12f, 12f);

        Renderer rend = ship.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Bounds bounds = rend.bounds;
            float bottomY = bounds.center.y - bounds.extents.y;
            float heightOffset = spawnPoint.position.y - bottomY;
            ship.transform.position += new Vector3(0f, heightOffset, 0f);

            BoxCollider boxCollider = ship.GetComponentInChildren<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.center = new Vector3(boxCollider.center.x, boxCollider.center.y - heightOffset, boxCollider.center.z);
            }
        }

        if (ship.GetComponentInChildren<Collider>() == null)
        {
            ship.GetComponentInChildren<MeshRenderer>().gameObject.AddComponent<BoxCollider>();
        }

        Ship shipScript = ship.GetComponent<Ship>();
        shipScript.Initialize(spawnEvent.isPirate, spawnEvent.speed);
        StatsTracker.Instance?.RegisterShipSpawn(spawnEvent.isPirate);
        shipScript.SetDestination(endPoint.position);

        float radius = GetIndicatorRadius(spawnEvent.sizeIndex, ship);

        GameObject indicator = Instantiate(circleIndicatorPrefab, ship.transform.position, Quaternion.identity);
        indicator.transform.SetParent(ship.transform);

        float xOffset = spawnEvent.lane == 1 ? 3f : spawnEvent.lane == 2 ? -3f : spawnEvent.lane == 3 ? 5f : 0f;
        indicator.transform.localPosition = new Vector3(xOffset, 3f, 0f);
        indicator.transform.localScale = new Vector3(radius * 1.1f, radius * 2.8f, radius * 2.8f);
        indicator.SetActive(false);

        shipScript.indicatorCircle = indicator;
    }

    float GetIndicatorRadius(int sizeIndex, GameObject ship)
    {
        float shipSize = ship.transform.localScale.x;
        return shipSize * 0.5f;
    }

    List<ShipSpawnEvent> BuildSchedule()
    {
        float defaultSpeed = 75f;
        int[,] rawScheduleData = new int[,]
        {
            {0, 0, 1, 0}, {18, 1, 1, 1}, {35, 2, 0, 2}, {52, 0, 1, 1},
            {69, 1, 0, 0}, {87, 2, 1, 2}, {104, 0, 0, 1}, {121, 1, 1, 0},
            {138, 2, 1, 2}, {156, 0, 1, 1}, {173, 1, 0, 0}, {190, 2, 1, 2},
            {207, 0, 1, 1}, {224, 1, 1, 0}, {241, 2, 0, 2}, {259, 0, 0, 1},
            {276, 1, 1, 0}, {294, 2, 1, 2}, {311, 0, 1, 1}, {328, 1, 0, 0},
            {346, 2, 1, 2}, {363, 0, 1, 1}, {380, 1, 0, 0}, {398, 2, 1, 2},
            {415, 0, 1, 1}, {432, 1, 1, 0}, {450, 2, 0, 2}, {467, 0, 0, 1},
            {484, 1, 1, 0}, {502, 2, 1, 2}, {519, 0, 1, 1}, {536, 1, 0, 0},
            {553, 2, 1, 2}, {570, 0, 1, 1}, {587, 1, 1, 0}, {605, 2, 0, 2},
            {622, 0, 0, 1}, {639, 1, 1, 0}, {657, 2, 1, 2}, {674, 0, 1, 1},
            {691, 1, 0, 0}, {709, 2, 1, 2}, {726, 0, 1, 1}, {743, 1, 1, 0},
            {761, 2, 0, 2}, {778, 0, 0, 1}, {795, 1, 1, 0}, {813, 2, 1, 2},
            {830, 0, 1, 1}, {847, 1, 0, 0}, {864, 2, 1, 2}, {881, 0, 1, 1},
            {898, 1, 1, 0}, {916, 2, 0, 2}, {933, 0, 0, 1}, {950, 1, 1, 0},
            {968, 2, 1, 2}, {985, 0, 1, 1}, {1002, 1, 0, 0}, {1020, 2, 1, 2},
            {1037, 0, 1, 1}, {1054, 1, 1, 0}, {1072, 2, 0, 2}, {1089, 0, 0, 1},
            {1106, 1, 1, 0}, {1124, 2, 1, 2}, {1141, 0, 1, 1}, {1158, 1, 0, 0},
            {1176, 2, 1, 2}, {1193, 0, 1, 1}, {1210, 1, 1, 0}, {1227, 2, 0, 2},
            {1244, 0, 0, 1}, {1261, 1, 1, 0}, {1279, 2, 1, 2}, {1296, 0, 1, 1},
            {1313, 1, 0, 0}, {1331, 2, 1, 2}, {1348, 0, 1, 1}, {1365, 1, 1, 0},
            {1383, 2, 0, 2}, {1400, 0, 0, 1}, {1417, 1, 1, 0}, {1435, 2, 1, 2},
            {1452, 0, 1, 1}, {1469, 1, 0, 0}, {1487, 2, 1, 2}, {1504, 0, 1, 1},
            {1521, 1, 1, 0}, {1539, 2, 0, 2}, {1556, 0, 0, 1}, {1573, 1, 1, 0},
            {1590, 2, 1, 2}, {1607, 0, 1, 1}, {1624, 1, 0, 0}, {1642, 2, 1, 2},
            {1659, 0, 1, 1}, {1676, 1, 1, 0}, {1694, 2, 0, 2}, {1711, 0, 0, 1},
            {1728, 1, 1, 0}, {1746, 2, 1, 2}, {1763, 0, 1, 1}, {1780, 1, 0, 0},
            {1798, 2, 1, 2}, {1815, 0, 1, 1}, {1832, 1, 1, 0}, {1850, 2, 0, 2},
            {1867, 0, 0, 1}, {1884, 1, 1, 0}, {1901, 2, 1, 2}, {1918, 0, 1, 1},
            {1935, 1, 0, 0}, {1953, 2, 1, 2}, {1970, 0, 1, 1}, {1987, 1, 1, 0},
            {2005, 2, 0, 2}, {2022, 0, 0, 1}, {2039, 1, 1, 0}, {2057, 2, 1, 2},
            {2074, 0, 1, 1}, {2091, 1, 0, 0}, {2109, 2, 1, 2}, {2126, 0, 1, 1},
            {2143, 1, 1, 0}, {2161, 2, 0, 2}, {2178, 0, 0, 1}, {2195, 1, 1, 0},
            {2213, 2, 1, 2}, {2230, 0, 1, 1}, {2247, 1, 0, 0}, {2264, 2, 1, 2},
            {2281, 0, 1, 1}, {2298, 1, 1, 0}, {2316, 2, 0, 2}, {2333, 0, 0, 1},
            {2350, 1, 1, 0}, {2368, 2, 1, 2}, {2385, 0, 1, 1}, {2402, 1, 0, 0},
            {2420, 2, 1, 2}, {2437, 0, 1, 1}, {2454, 1, 1, 0}, {2472, 2, 0, 2},
            {2489, 0, 0, 1}, {2506, 1, 1, 0}, {2525, 2, 1, 2}, {2541, 0, 1, 1},
            {2558, 1, 0, 0}, {2576, 2, 1, 2}, {2593, 0, 1, 1}, {2610, 1, 1, 0},
            {2628, 2, 0, 2}, {2645, 0, 0, 1}, {2662, 1, 1, 0}, {2680, 2, 1, 2},
            {2697, 0, 1, 1}, {2714, 1, 0, 0}, {2732, 2, 1, 2}, {2749, 0, 1, 1},
            {2766, 1, 1, 0}, {2784, 2, 0, 2}, {2801, 0, 0, 1}, {2818, 1, 1, 0},
            {2836, 2, 1, 2}, {2853, 0, 1, 1}, {2870, 1, 0, 0}, {2888, 2, 1, 2},
            {2905, 0, 1, 1}, {2922, 1, 1, 0}, {2940, 2, 0, 2}, {2957, 0, 0, 1},
            {2974, 1, 1, 0}, {2992, 2, 1, 2}, {3009, 0, 1, 1}, {3026, 1, 0, 0},
            {3044, 2, 1, 2}, {3061, 0, 1, 1}, {3078, 1, 1, 0}, {3096, 2, 0, 2},
            {3113, 0, 0, 1}, {3130, 1, 1, 0}, {3148, 2, 1, 2}, {3165, 0, 1, 1},
            {3182, 1, 0, 0}, {3200, 2, 1, 2}, {3217, 0, 1, 1}, {3234, 1, 1, 0},
            {3252, 2, 0, 2}, {3269, 0, 0, 1}, {3286, 1, 1, 0}, {3304, 2, 1, 2},
            {3321, 0, 1, 1}, {3338, 1, 0, 0}, {3356, 2, 1, 2}, {3373, 0, 1, 1},
            {3390, 1, 1, 0}, {3408, 2, 0, 2}, {3425, 0, 0, 1}, {3442, 1, 1, 0},
            {3460, 2, 1, 2}, {3477, 0, 1, 1}
        };

        var result = new List<ShipSpawnEvent>();
        for (int i = 0; i < rawScheduleData.GetLength(0); i++)
        {
            float time = rawScheduleData[i, 0] / 10f;
            int lane = rawScheduleData[i, 1];
            bool isPirate = rawScheduleData[i, 2] == 1;
            int sizeIndex = rawScheduleData[i, 3];
            result.Add(new ShipSpawnEvent(time, lane, isPirate, sizeIndex, defaultSpeed));
        }

        return result;
    }
}
#endif









