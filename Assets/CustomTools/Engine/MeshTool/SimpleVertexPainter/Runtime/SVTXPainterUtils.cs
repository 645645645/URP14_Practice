using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Text.RegularExpressions;

namespace SVTXPainter
{
    public enum PaintLayerType
    {
        UV0,
        UV1,
        UV2,
        UV3,
        UV4,
        UV5,
        UV6,
        UV7,
        Color,
    }

    public enum PaintChannelType
    {
        R,
        G,
        B,
        A,
        All,
        // Smooth,
    }

    public static class SVTXPainterUtils
    {
        public static Mesh GetMesh(GameObject aGO)
        {
            Mesh curMesh = null;
            if (aGO)
            {
                MeshFilter curFilter = aGO.GetComponent<MeshFilter>();
                SkinnedMeshRenderer curSkinnned = aGO.GetComponent<SkinnedMeshRenderer>();

                if (curFilter && !curSkinnned)
                {
                    curMesh = curFilter.sharedMesh;
                }

                if (!curFilter && curSkinnned)
                {
                    curMesh = curSkinnned.sharedMesh;
                }
            }

            return curMesh;
        }

        //Falloff 
        public static float LinearFalloff(float distance, float brushRadius)
        {
            return Mathf.Clamp01(1 - distance / brushRadius);
        }

        // Lerp
        public static Vector4 VTXColorLerp(Vector4 colorA, Color colorB, float value)
        {
            return Vector4.Lerp(colorA, colorB, value);
        }

        public static Color32 VTXColorLerp(Color32 colorA, Color32 colorB, float value)
        {
            return Color32.Lerp(colorA, colorB, value);
        }

        public static Vector4 VTXOneChannelLerp(Vector4 color, float intensity, float value, PaintChannelType channel)
        {
            int channelIndex = (int)channel;
            if (channelIndex >= 0 && channelIndex <= 3)
            {
                color[channelIndex] = Mathf.Lerp(color[channelIndex], intensity, value);
                return color;
            }
            else
            {
                //error
                return Color.cyan;
            }
        }

        public static Color32 VTXOneChannelLerp(Color32 color, byte intensity, float value, PaintChannelType channel)
        {
            int channelIndex = (int)channel;
            if (channelIndex >= 0 && channelIndex <= 3)
            {
                color[channelIndex] = (byte)Mathf.Lerp(color[channelIndex], intensity, value);
                return color;
            }
            else
            {
                //error
                return new Color32(0, 255, 255, 255);
            }
        }

        public static string SanitizeForFileName(string name)
        {
            var reg = new Regex("[\\/:\\*\\?<>\\|\\\"]");
            return reg.Replace(name, "_");
        }
    }
}