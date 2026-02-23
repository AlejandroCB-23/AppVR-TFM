#if WAVE_SDK_IMPORTED

using UnityEngine;

public class ShipSink : MonoBehaviour
{
    private float acceleration = 6f;        
    private float currentSpeed = 0f;        
    private float rotationSpeed = 20f;
    private float sinkDepth = 60f;
    private float startY;

    void Start()
    {
        startY = transform.position.y;
    }

    void Update()
    {
        currentSpeed += acceleration * Time.deltaTime;

        transform.Translate(Vector3.down * currentSpeed * Time.deltaTime, Space.World);
        transform.Rotate(Vector3.right * rotationSpeed * Time.deltaTime);

        if (transform.position.y < startY - sinkDepth)
        {
            Destroy(gameObject);
        }
    }
}
#endif


