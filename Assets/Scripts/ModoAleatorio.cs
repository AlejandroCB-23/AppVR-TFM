#if WAVE_SDK_IMPORTED

using System.Collections.Generic;
using UnityEngine;

public class ModoAleatorio : MonoBehaviour
{
    public Transform[] spawnPoints;
    public Transform[] endPoints;
    public GameObject[] pirateShipPrefabs;
    public GameObject[] normalShipPrefabs;
    public GameObject redShipPrefab;
    public GameObject circleIndicatorPrefab;

    public GameManagerModoAleatorio gameManager;
    public GameObject[] heartLives;

    public float spawnIntervalMin = 0.1f;
    public float spawnIntervalMax = 0.18f;

    private float nextSpawnTime = 0f;
    private float timer = 0f;
    private int shipsGeneratedThisCycle = 0;
    private int maxShipsInCycle = 60;
    private Dictionary<int, float> lastSpawnTimePerLane = new Dictionary<int, float>();

    private bool gameEnded = false;
    private int lastEliminatedCount = 0;

    private float shipSpeed = 32f;
    public float speedIncreaseRate = 10f;
    private float difficultyTimer = 0f;

    private int shipsSpawnedSinceLastRed = 0;
    private const int shipsPerRedShip = 20;

    public float baseMinDistance = 50f;
    public float sinkingExtraDistance = 30f;
    public float speedDistanceMultiplier = 1.6f;

    private float currentMinDistance;

    private List<GameObject> activeShips = new List<GameObject>();
    private int shipCounter = 0;

    private float globalSpawnCooldown = 0f;
    private const float minGlobalSpawnInterval = 0.02f;

    void Start()
    {
        StatsTracker.Instance.ResetAll();
        currentMinDistance = baseMinDistance;
        nextSpawnTime = Random.Range(spawnIntervalMin, spawnIntervalMax);

        if (gameManager == null)
        {
            gameManager = FindObjectOfType<GameManagerModoAleatorio>();
        }
    }

    void Update()
    {
        if (gameEnded) return;

        timer += Time.deltaTime;
        difficultyTimer += Time.deltaTime;
        globalSpawnCooldown -= Time.deltaTime;

        CleanDestroyedShips();
        CheckEliminatedLives();

        if (GetLostLivesCount() >= heartLives.Length)
        {
            gameEnded = true;
            return;
        }

        if (difficultyTimer >= 2f)
        {
            difficultyTimer = 0f;

            if (Time.timeSinceLevelLoad < 120f)
            {
                float oldSpeed = shipSpeed;
                shipSpeed = Mathf.Min(85f, shipSpeed + speedIncreaseRate);
                maxShipsInCycle = Mathf.Min(200, maxShipsInCycle + 10);

                spawnIntervalMin = Mathf.Max(0.03f, spawnIntervalMin - 0.008f);
                spawnIntervalMax = Mathf.Max(0.08f, spawnIntervalMax - 0.008f);

                UpdateMinDistance();

                if (shipSpeed != oldSpeed)
                {
                    UpdateAllActiveShipsSpeed();
                }
            }
        }

        int shipsToTrySpawn = GetShipsToSpawn();
        int successfulSpawns = 0;

        for (int i = 0; i < shipsToTrySpawn && shipsGeneratedThisCycle < maxShipsInCycle; i++)
        {
            if (timer >= nextSpawnTime && globalSpawnCooldown <= 0f)
            {
                if (SpawnRandomShip())
                {
                    successfulSpawns++;
                    shipsGeneratedThisCycle++;
                    globalSpawnCooldown = minGlobalSpawnInterval;
                }
                else
                {
                    nextSpawnTime = timer + Random.Range(0.05f, 0.15f);
                }
            }
        }

        if (successfulSpawns > 0)
        {
            nextSpawnTime = timer + Mathf.Lerp(spawnIntervalMin, spawnIntervalMax, Random.value * 0.2f);
        }
        else if (timer >= nextSpawnTime)
        {
            nextSpawnTime = timer + spawnIntervalMin * 0.5f;
        }

        if (shipsGeneratedThisCycle >= maxShipsInCycle)
        {
            shipsGeneratedThisCycle = 0;
        }
    }

    int GetShipsToSpawn()
    {
        float timeElapsed = Time.timeSinceLevelLoad;
        int activeShipCount = activeShips.Count;

        int baseShips = 1;
        if (timeElapsed > 240f) baseShips = 6;
        else if (timeElapsed > 180f) baseShips = 5;
        else if (timeElapsed > 120f) baseShips = 4;
        else if (timeElapsed > 60f) baseShips = 3;
        else if (timeElapsed > 30f) baseShips = 2;

        if (activeShipCount < 8) baseShips += 2;
        else if (activeShipCount < 12) baseShips += 1;

        return Mathf.Min(baseShips, 8);
    }

    void UpdateMinDistance()
    {
        currentMinDistance = Mathf.Max(baseMinDistance, shipSpeed * speedDistanceMultiplier);
    }

    void UpdateAllActiveShipsSpeed()
    {
        foreach (var ship in activeShips)
        {
            if (ship != null)
            {
                Ship shipScript = ship.GetComponent<Ship>();
                if (shipScript != null && !shipScript.IsSinking())
                {
                    shipScript.UpdateSpeed(shipSpeed);
                }
            }
        }
    }

    void CleanDestroyedShips()
    {
        for (int i = activeShips.Count - 1; i >= 0; i--)
        {
            if (activeShips[i] == null)
                activeShips.RemoveAt(i);
        }
    }

    void CheckEliminatedLives()
    {
        int eliminatedCount = StatsTracker.Instance.GetTotalLivesLost();
        if (eliminatedCount > lastEliminatedCount)
        {
            for (int i = 0; i < (eliminatedCount - lastEliminatedCount); i++)
            {
                RemoveNextActiveHeartLeftToRight();
            }
            lastEliminatedCount = eliminatedCount;
        }
    }

    public int GetLostLivesCount()
    {
        int lost = 0;
        foreach (var heart in heartLives)
        {
            if (!heart.activeSelf)
                lost++;
        }
        return lost;
    }

    void RemoveNextActiveHeartLeftToRight()
    {
        for (int i = 0; i < heartLives.Length; i++)
        {
            if (heartLives[i].activeSelf)
            {
                heartLives[i].SetActive(false);
                break;
            }
        }
    }

    public void RestoreLife()
    {
        for (int i = heartLives.Length - 1; i >= 0; i--)
        {
            if (!heartLives[i].activeSelf)
            {
                heartLives[i].SetActive(true);
                break;
            }
        }
        lastEliminatedCount = StatsTracker.Instance.GetTotalLivesLost();
    }

    bool CanSpawnInLane(int laneIndex, Vector3 spawnPosition)
    {
        foreach (var ship in activeShips)
        {
            if (ship == null) continue;

            if (Mathf.Abs(ship.transform.position.x - spawnPosition.x) < 8f)
            {
                float distance = Vector3.Distance(ship.transform.position, spawnPosition);
                Ship shipScript = ship.GetComponent<Ship>();

                if (shipScript != null && shipScript.IsSinking())
                {
                    if (distance < currentMinDistance + sinkingExtraDistance)
                        return false;
                }
                else
                {
                    float gameTimeMultiplier = Mathf.Max(1f, Time.timeSinceLevelLoad / 30f);
                    float adjustedMinDistance = currentMinDistance * (1.2f + (1f / gameTimeMultiplier));

                    if (ship.transform.position.z > spawnPosition.z - 25f)
                    {
                        adjustedMinDistance *= 1.4f;
                    }

                    if (distance < adjustedMinDistance)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    bool SpawnRandomShip()
    {
        int lane = GetBestAvailableLane();
        if (lane == -1) return false;

        bool isPirate, isRed;
        GameObject prefab = ChooseShipPrefab(out isPirate, out isRed);
        GameObject ship = InstantiateShipInLane(lane, prefab);
        ConfigureShip(ship, lane, isPirate, isRed); 
        return true;
    }

    int GetBestAvailableLane()
    {
        List<int> availableLanes = new();
        Dictionary<int, bool> canSpawnCache = new();

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            bool canSpawn = CanSpawnInLane(i, spawnPoints[i].position);
            canSpawnCache[i] = canSpawn;
            if (canSpawn)
            {
                if (!lastSpawnTimePerLane.TryGetValue(i, out float lastTime) ||
                    Time.time - lastTime >= GetLaneCooldown())
                {
                    availableLanes.Add(i);
                }
            }
        }

        if (availableLanes.Count == 0) return -1;

        availableLanes.Sort((a, b) => {
            float timeA = lastSpawnTimePerLane.ContainsKey(a) ? Time.time - lastSpawnTimePerLane[a] : float.MaxValue;
            float timeB = lastSpawnTimePerLane.ContainsKey(b) ? Time.time - lastSpawnTimePerLane[b] : float.MaxValue;
            return timeB.CompareTo(timeA);
        });

        return availableLanes[0];
    }

    float GetLaneCooldown()
    {
        float earlyGameMultiplier = Time.timeSinceLevelLoad < 45f ? 1.5f : 1f;
        return (currentMinDistance / shipSpeed) * 0.7f * earlyGameMultiplier;
    }

    GameObject ChooseShipPrefab(out bool isPirate, out bool isRed)
    {
        isPirate = Random.value < 0.7f;
        isRed = false;

        bool shouldSpawnRed = GetLostLivesCount() > 0 &&
                              shipsSpawnedSinceLastRed >= shipsPerRedShip &&
                              Random.value < 0.7f;

        if (shouldSpawnRed)
        {
            shipsSpawnedSinceLastRed = 0;
            isRed = true;
            isPirate = false;
            return redShipPrefab;
        }

        shipsSpawnedSinceLastRed++;

        int sizeIndex = Random.Range(0, 3);
        GameObject prefab = isPirate ? pirateShipPrefabs[sizeIndex] : normalShipPrefabs[sizeIndex];

        return prefab;
    }


    GameObject InstantiateShipInLane(int lane, GameObject prefab)
    {
        Transform spawnPoint = spawnPoints[lane];
        GameObject ship = Instantiate(prefab, spawnPoint.position, Quaternion.identity);
        ship.transform.localScale = new Vector3(12f, 12f, 12f);
        ship.tag = "Ship";
        ship.name = prefab.name + "_" + (++shipCounter);

        Renderer rend = ship.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            float bottomY = rend.bounds.center.y - rend.bounds.extents.y;
            float heightOffset = spawnPoint.position.y - bottomY;
            ship.transform.position += new Vector3(0f, heightOffset, 0f);

            BoxCollider box = ship.GetComponentInChildren<BoxCollider>();
            if (box != null) box.center -= new Vector3(0, heightOffset, 0);
        }

        if (ship.GetComponentInChildren<Collider>() == null)
        {
            ship.GetComponentInChildren<MeshRenderer>().gameObject.AddComponent<BoxCollider>();
        }

        activeShips.Add(ship);
        lastSpawnTimePerLane[lane] = Time.time;

        return ship;
    }

    void ConfigureShip(GameObject ship, int lane, bool isPirate, bool isRed)
    {
        Ship shipScript = ship.GetComponent<Ship>();
        shipScript.Initialize(isPirate, shipSpeed); 
        shipScript.SetDestination(endPoints[lane].position);
        shipScript.isRedShip = isRed;

       
        GameObject indicator = Instantiate(circleIndicatorPrefab, ship.transform.position, Quaternion.identity);
        indicator.transform.SetParent(ship.transform);

        float xOffset = (lane == 1) ? 3f : (lane == 2) ? -3f : (lane == 3) ? 5f : 0f;
        float radius = ship.transform.localScale.x * 0.5f;
        indicator.transform.localPosition = new Vector3(xOffset, 3f, 0f);
        indicator.transform.localScale = new Vector3(radius * 1.1f, radius * 2.8f, radius * 2.8f);
        indicator.SetActive(false);

        shipScript.indicatorCircle = indicator;
    }

}

#endif

































