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
            if (StatsSaved.Instance != null)
            {
                await StatsSaved.Instance.SaveFinalStatsAsync(); 
            }
            await Task.Delay(400);

            SceneManager.LoadScene("Menu", LoadSceneMode.Single);
            Destroy(gameObject);
        }
    }
}
#endif

