#if WAVE_SDK_IMPORTED

using UnityEngine;

public class Cannonball : MonoBehaviour
{
    [HideInInspector]
    public GameObject targetButton; 
    [HideInInspector]
    public menu.Menu menuController;

    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject == targetButton || collision.gameObject.CompareTag("Boton"))
        {
            menuController.ExecuteButtonAction(targetButton);
            Destroy(gameObject); 
        }
    }
}
#endif




