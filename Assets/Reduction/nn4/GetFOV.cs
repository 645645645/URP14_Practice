using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// [ExecuteInEditMode]
public class GetFOV : MonoBehaviour
{
    // private RenderTexture colorBuffer;
    // private RenderTexture depthBuffer;
    // private Camera cam;
    //
    // void Awake()
    // {
    //     cam = Camera.main;
    //     colorBuffer = new RenderTexture(Screen.width, Screen.height, 24, GraphicsFormat.R8G8B8A8_UNorm);
    //     depthBuffer = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth);
    // }

    void Start()
    {
        Vector4 VP_col1 = new Vector4(-2.40837f, -0.10622f, 0.06578f, 0.06499f);
        Vector4 VP_col2 = new Vector4(-0.05399f, 4.28789f, 0.03763f, 0.03719f);
        Vector4 VP_col3 = new Vector4(-0.15898f, 0.15297f, -1.00923f, -0.99719f);
        Vector4 VP_col4 = new Vector4(0.11319f, -3.09601f, 4.11081f, 4.6582f);
        Matrix4x4 VP = new Matrix4x4(VP_col1, VP_col2, VP_col3, VP_col4);
        
        Vector4 InvV_col1 = new Vector4(-0.99758f, -0.02236f, -0.06585f, 0f);
        Vector4 InvV_col2 = new Vector4(-0.02475f, 0.99906f, 0.03564f, 0f);
        Vector4 InvV_col3 = new Vector4(-0.06499f, -0.03719f, 0.99719f, 0f);
        Vector4 InvV_col4 = new Vector4(-0.27383f, 0.54581f, 4.67392f, 1.0f);
        Matrix4x4 InvV = new Matrix4x4(InvV_col1, InvV_col2, InvV_col3, InvV_col4);
        Matrix4x4 P = InvV.transpose * VP;
        float cotFOV_2 = P.m11;
        Debug.Log(P);
        // fov = arccot(P[1][1]) * 2 * 180 / pi
        Debug.LogFormat("FOV = {0}", (Mathf.PI - Mathf.Atan(cotFOV_2) * 2) * 180 / Mathf.PI);
        Debug.LogFormat("truth FOV = {0}", (Mathf.PI - Mathf.Atan(4.29193f) * 2) * 180 / Mathf.PI);
        Debug.LogFormat("Aspect = {0}", cotFOV_2 / P.m00);
        Debug.LogFormat("truth Aspect = {0}", 1280f / 720f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // private void OnPreRender()
    // {
    //     cam.SetTargetBuffers(colorBuffer.colorBuffer, depthBuffer.depthBuffer);
    // }
    //
    // private void OnPostRender()
    // {
    //     Graphics.Blit(colorBuffer, cam.targetTexture);
    // }
}
