using UnityEngine;

public class MoveRocker : MonoBehaviour
{
    [SerializeField] private int ZAngle = 20;
    [SerializeField] private float ZFrequency = 1f / 5f;
    [SerializeField] private int XAngle = 10;
    [SerializeField] private float XFrequency = 1f / 10f;
    [SerializeField] private int YAngle = 10;
    [SerializeField] private float YFrequency = 1f / 10f;

    private float rAX,rAY, rAZ;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rAX = (float)XAngle/ 360;
        rAY = (float)YAngle / 360;
        rAZ = (float)ZAngle / 360;
    }

    // Update is called once per frame
    void Update()
    {
        float angleX = Mathf.Cos(Time.time * XFrequency * 2f * Mathf.PI) * rAX;
        float angleY = Mathf.Cos(Time.time * YFrequency * 2f * Mathf.PI) * rAY;
        float angleZ = Mathf.Cos(Time.time * ZFrequency * 2f * Mathf.PI) * rAZ;
        transform.Rotate(angleX,angleY, angleZ);
    }
}
