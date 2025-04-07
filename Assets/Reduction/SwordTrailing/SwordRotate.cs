using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SwordRotate : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        dir = Mathf.Sign(Random.Range(-1, 1));
    }

    private float dir;

    // Update is called once per frame
    void Update()
    {
        var seed = UnityEngine.Random.Range(1, 10);
        var sinTime = Mathf.Sin(Time.time);
        transform.Rotate(transform.forward, Time.deltaTime * seed * 80 * sinTime, Space.Self);
        var focusPos = new Vector3(Mathf.Cos(Time.time) * 2.5f, -sinTime, 0);
        transform.position = Vector3.Lerp(transform.position, focusPos, Time.deltaTime * seed);
    }
}