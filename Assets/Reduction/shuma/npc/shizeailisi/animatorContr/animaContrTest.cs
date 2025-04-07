using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class animaContrTest : MonoBehaviour
{
    Animator animControl;
    void Start()
    {
        animControl = GetComponent<Animator>();
        //animControl.SetInteger("moveType", 0);
    }

    private void OnGUI()
    {
        if(GUI.Button(new Rect(0f, 0f, 100, 40), "Idle"))
        {
            animControl.SetInteger("moveType", 0);
        }
        if (GUI.Button(new Rect(0f, 50f, 100, 40), "walk"))
        {
            animControl.SetInteger("moveType", 1);
        }
        if (GUI.Button(new Rect(0f, 100f, 100, 40), "run"))
        {
            animControl.SetInteger("moveType", 2);
        }
        if (GUI.Button(new Rect(0f, 150f, 100, 40), "jump"))
        {
            animControl.SetInteger("moveType", 3);
        }
    }

}
