#if WAVE_SDK_IMPORTED

using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;

public class CannonballShip : MonoBehaviour
{
    [HideInInspector] public Ship targetShip; 

    async void OnCollisionEnter(Collision collision)
    {
        GameObject hitObj = collision.gameObject;
        
        if (hitObj.CompareTag("Ship"))
        {
            Ship ship = hitObj.GetComponent<Ship>();
            if (ship != null && !ship.IsSinking())
            {
                ship.Sink(); 
            }

            Destroy(gameObject); 
        }

        else if (hitObj.CompareTag("Boton"))
        {
            // Prefer AWS collector when active; disabled local singletons may still exist.
            if (StatsSavedAWS.Instance != null && StatsSavedAWS.Instance.isActiveAndEnabled)
            {
                await StatsSavedAWS.Instance.SaveFinalStatsAsync();
            }
            else if (StatsSaved.Instance != null && StatsSaved.Instance.isActiveAndEnabled)
            {
                await StatsSaved.Instance.SaveFinalStatsAsync(); 
            }

            if (HeatMapDataAWS.Instance != null && HeatMapDataAWS.Instance.isActiveAndEnabled)
            {
                await HeatMapDataAWS.Instance.SavePendingDataAsync();
            }

            await Task.Delay(400);

            SceneManager.LoadScene("Menu", LoadSceneMode.Single);
            Destroy(gameObject);
        }
    }
}
#endif

