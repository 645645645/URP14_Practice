using System.Collections;
using UnityEngine;
using UnityEditor;
using System.IO;
using Unity.EditorCoroutines.Editor;
using Object = UnityEngine.Object;

public class PreIntegratedSkinShadingLut : EditorWindow
{
    enum TextureSize
    {
        _64x64 = 64,
        _128x128 = 128,
        _256x256 = 256,
        _512x512 = 512,
    }

    private const string CSBuildPath = "Assets/CustomTools/Editor/BakeTool/SSS_LutBuild/PreIntegratedSkinShadingLutCreator.compute";
    private const string Full_OutPath = "Assets/CustomTools/Engine/BakeOutput/SSS_Lut.TGA";
    private const string Ramp_OutPath = "Assets/CustomTools/Engine/BakeOutput/SSS_Lut_Low.TGA";

    private TextureSize LutReslution = TextureSize._128x128;
    private bool UseHalfSphere = true; //半圆/整圆积分
    private bool UseToneMaping = true;
    private bool ConvertSrgb = false;

    private static readonly int _LutTex = Shader.PropertyToID("_Lut");
    private static readonly int _LutSize = Shader.PropertyToID("_LutSize");


    [MenuItem("Tools/Baking/SSS_Lut", false, 1200)]
    private static void CreateSSS_Lut()
    {
        GetWindow<PreIntegratedSkinShadingLut>("SSS Skin LUT Builder");
    }

    private IEnumerator StartBuild(bool isFull)
    {
        ComputeShader cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(CSBuildPath);
        if (cs == null)
        {
            messge = $"ComputeShader is null, please check. path = {CSBuildPath}";
            yield break;
        }
        int kernel_full = cs.FindKernel("CSMain_Full");
        int kernel_low = cs.FindKernel("CSMain_Low");

        cs.SetInt("_IntegralInterval", UseHalfSphere ? 1 : 2);

        if (UseToneMaping)
            cs.EnableKeyword("_USE_TONEMAPING");
        else
            cs.DisableKeyword("_USE_TONEMAPING");
        
        if (ConvertSrgb)
            cs.EnableKeyword("_CONVERT_TO_SRGB");
        else
            cs.DisableKeyword("_CONVERT_TO_SRGB");

        switch (isFull)
        {
            case true:
                yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                    BuildSSSLut(cs, kernel_full, (int)LutReslution, (int)LutReslution, Full_OutPath));
                break;
            case false:
                yield return EditorCoroutineUtility.StartCoroutineOwnerless(
                    BuildSSSLut(cs, kernel_low, (int)LutReslution, 1, Ramp_OutPath, 0.5f));
                break;
        }
        
        messge = $"SSS Skin Lut Build Finishing, path = {Full_OutPath}";
    }

    private IEnumerator BuildSSSLut(ComputeShader cs, int kernel, int width, int height, string savePath, float vPixelOffset = 0)
    {
        var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB64, -1)
        {
            useMipMap = false,
            autoGenerateMips = false,
            sRGB = true,
            bindMS = false,
            msaaSamples = 1,
            enableRandomWrite = true,
        };
        Vector4 sizeParmas = new Vector4()
        {
            x = 0.5f / width,
            y = (0.5f + vPixelOffset) / height,
            z = 1.0f / width,
            w = 1.0f / height,
        };
        var rt = RenderTexture.GetTemporary(desc);
        rt.name = $"_{width}x{height}_" + nameof(PreIntegratedSkinShadingLut);
        cs.SetVector(_LutSize, sizeParmas);
        cs.SetTexture(kernel, _LutTex, rt);
        int numX = Mathf.Max(Mathf.CeilToInt(width / 8f), 1);
        int numY = Mathf.Max(Mathf.CeilToInt(height / 8f), 1);
        cs.Dispatch(kernel, numX, numY, 1);

        yield return EditorCoroutineUtility.StartCoroutineOwnerless(SaveRenderTexture(rt, TextureFormat.RGB24, savePath));
    }

    private IEnumerator SaveRenderTexture(RenderTexture rt, TextureFormat format, string path)
    {
        //cs下一帧才能拿到回读数据
        yield return new EditorWaitForSeconds(0);
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D png = new Texture2D(rt.width, rt.height, format, rt.useMipMap, !rt.sRGB);
        png.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        png.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(path, png.EncodeToTGA());
        Object.DestroyImmediate(png);
        // Debug.Log("SSS_Lut保存成功！" + path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.sRGBTexture = true; 
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 0;
        importer.SaveAndReimport();
    }

    private string messge = string.Empty;

    private void OnGUI()
    {
        GUILayout.Label("Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        LutReslution = (TextureSize)EditorGUILayout.EnumPopup("分辨率", LutReslution); 
        EditorGUILayout.Space();
        UseHalfSphere = EditorGUILayout.Toggle("积分域:半球?", UseHalfSphere);
        EditorGUILayout.Space();
        UseToneMaping = EditorGUILayout.Toggle("使用ToneMaping", UseToneMaping);
        EditorGUILayout.Space();
        ConvertSrgb = EditorGUILayout.Toggle("转为sRGB", ConvertSrgb);
        
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("BuildFullRes"))
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(StartBuild(true));
        }
        EditorGUILayout.Space();
        if (GUILayout.Button("BuildLowRes"))
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(StartBuild(false));
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(20);
        if(!string.IsNullOrEmpty(messge))
            EditorGUILayout.HelpBox(messge, MessageType.Info, true);
    }
}