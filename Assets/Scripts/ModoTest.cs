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
        float defaultSpeed = 37f;
        int[,] rawScheduleData = new int[,]
        {
            {0, 0, 1, 0}, {25, 1, 1, 1}, {48, 2, 0, 2}, {72, 0, 1, 1},
            {96, 1, 0, 0}, {121, 2, 1, 2}, {144, 0, 0, 1}, {169, 1, 1, 0},
            {193, 2, 1, 2}, {217, 0, 1, 1}, {240, 1, 0, 0}, {265, 2, 1, 2},
            {288, 0, 1, 1}, {312, 1, 1, 0}, {336, 2, 0, 2}, {360, 0, 0, 1},
            {384, 1, 1, 0}, {409, 2, 1, 2}, {432, 0, 1, 1}, {456, 1, 0, 0},
            {481, 2, 1, 2}, {505, 0, 1, 1}, {528, 1, 0, 0}, {553, 2, 1, 2},
            {576, 0, 1, 1}, {600, 1, 1, 0}, {624, 2, 0, 2}, {649, 0, 0, 1},
            {672, 1, 1, 0}, {696, 2, 1, 2}, {721, 0, 1, 1}, {744, 1, 0, 0},
            {768, 2, 1, 2}, {793, 0, 1, 1}, {816, 1, 1, 0}, {840, 2, 0, 2},
            {864, 0, 0, 1}, {889, 1, 1, 0}, {912, 2, 1, 2}, {936, 0, 1, 1},
            {961, 1, 0, 0}, {985, 2, 1, 2}, {1008, 0, 1, 1}, {1032, 1, 1, 0},
            {1056, 2, 0, 2}, {1080, 0, 0, 1}, {1104, 1, 1, 0}, {1129, 2, 1, 2},
            {1152, 0, 1, 1}, {1176, 1, 0, 0}, {1200, 2, 1, 2}, {1224, 0, 1, 1},
            {1248, 1, 1, 0}, {1273, 2, 0, 2}, {1296, 0, 0, 1}, {1320, 1, 1, 0},
            {1344, 2, 1, 2}, {1369, 0, 1, 1}, {1392, 1, 0, 0}, {1416, 2, 1, 2},
            {1440, 0, 1, 1}, {1464, 1, 1, 0}, {1488, 2, 0, 2}, {1513, 0, 0, 1},
            {1536, 1, 1, 0}, {1560, 2, 1, 2}, {1584, 0, 1, 1}, {1609, 1, 0, 0},
            {1632, 2, 1, 2}, {1656, 0, 1, 1}, {1680, 1, 1, 0}, {1704, 2, 0, 2},
            {1728, 0, 0, 1}, {1753, 1, 1, 0}, {1776, 2, 1, 2}, {1800, 0, 1, 1}
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









