using System;
using TMPro;
using UnityEditor;
using UnityEngine;

public class MipMapSampleMoveTest : MonoBehaviour
{
    private void Start()
    {
        // Application.targetFrameRate = targetFrameRate;
    }
    
    
    public int targetFrameRate = 60;

    [ContextMenu("CreatMipmapSampleDistribution")]
    void CreaterMipSampleDistribution()
    {
        for (int i = 0; i < 64; i++)
        {
            int xbits = 0, ybits = 0;
            for (int j = 0; j < 3; j++)
            {
                int DestBitId = 3 - 1 - j;
                int DestBitMask = 1 << (int)DestBitId;
                xbits |= DestBitMask & SignedRightShift(i, (int)(DestBitId) - (int)(j * 2 + 0));
                ybits |= DestBitMask & SignedRightShift(i, (int)(DestBitId) - (int)(j * 2 + 1));
            }


            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            obj.transform.position = new Vector3(xbits, 0, ybits);
            obj.transform.localScale = Vector3.one * 0.2f;
            obj.name = $"{i} = ({xbits},  {ybits})";
            var text = obj.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            // text.enableAutoSizing = true;
            text.text = $"{i}";
        }
    }


    int SignedRightShift(int x, int bitshift)
    {
        if (bitshift > 0)
        {
            return x << ((int)bitshift);
        }
        else if (bitshift < 0)
        {
            return x >> (int)(-bitshift);
        }

        return x;
    }
}