
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[ExecuteAlways]
public class MaterialIDManager : MonoBehaviour
{
    private HashSet<Material> _materialsID;
    private List<Material> _materials;

    private Texture2D materialID_DiffuseTex;
    private Texture2D materialID_PBRTex;

    Material mat;
    
    private static int _SmoothnessID = Shader.PropertyToID("_Smoothness");
    private static int _MetallicID   = Shader.PropertyToID("_Metallic");
    private static int _BaseColorID  = Shader.PropertyToID("_BaseColor");
    private static int _MaterialID   = Shader.PropertyToID("_MaterialID");
    
    void Start()
    {
        _materialsID = new HashSet<Material>(256);
        _materials   = new List<Material>(256);
        
        GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
        
        GameObject   root;

        Material[] mats;
                     
        for (int i = 0; i < rootObjects.Length; i++)
        {
            root = rootObjects[i];
                
            var allChilds = root.GetComponentsInChildren<Renderer>(true);

            for (int j = 0; j < allChilds.Length; j++)
            {
                mats = allChilds[j].sharedMaterials;
                for (int k = 0; k < mats.Length; k++)
                {
                    mat = mats[k];

                    if (!mat)
                        return;

                    if (!(mat.HasProperty(_SmoothnessID) &&
                          mat.HasProperty(_MetallicID)   &&
                          mat.HasProperty(_BaseColorID)  &&
                          mat.HasProperty(_MaterialID)))
                        continue;

                    if (_materialsID.Add(mat))
                        _materials.Add(mat);
                }
            }
        }
        
        materialID_DiffuseTex            = new Texture2D(16,16, TextureFormat.RGBA32, 1, true, false);
        materialID_DiffuseTex.filterMode = FilterMode.Point;
        materialID_DiffuseTex.wrapMode   = TextureWrapMode.Clamp;
        materialID_DiffuseTex.name       = "_ID2Diffuse_Tex";
        
        
        materialID_PBRTex            = new Texture2D(16,16, TextureFormat.RGBA32, 1, true, false);
        materialID_PBRTex.filterMode = FilterMode.Point;
        materialID_PBRTex.wrapMode   = TextureWrapMode.Clamp;
        materialID_PBRTex.name       = "_ID2PBR_Tex";

        diffuseData = new Color32[256];
        pbrData     = new Color32[256];
    }


    private Color32[] diffuseData;
    private Color32[] pbrData;

    Color32 clear = new Color32(0, 0, 0, 1);
    
    void Update()
    {
        diffuseData[0] = clear;
        pbrData[0]     = clear;
        for (int i = 0; i < 256; i++)
        {
            if (i < _materials.Count)
            {
                mat = _materials[i];

                Color32 diffuse = mat.GetColor(_BaseColorID);
                Color32 pbr = new Color(1f - mat.GetFloat(_SmoothnessID), mat.GetFloat(_MetallicID), 0, 0);

                int id = i + 1;
                
                diffuseData[id] = diffuse;
                pbrData[id]     = pbr;
                mat.SetFloat(_MaterialID, id);
            }
            else
            {
                diffuseData[i] = clear;
                pbrData[i]     = clear;
            }
        }

        materialID_DiffuseTex.SetPixels32(diffuseData, 0);
        materialID_PBRTex.SetPixels32(pbrData, 0);
        materialID_DiffuseTex.Apply();
        materialID_PBRTex.Apply();

        Shader.SetGlobalTexture(materialID_DiffuseTex.name, materialID_DiffuseTex);
        Shader.SetGlobalTexture(materialID_PBRTex.name, materialID_PBRTex);
    }

    

    private void OnDestroy()
    {
        
        if (materialID_DiffuseTex)
        {
            SaveTexture2D(materialID_DiffuseTex);
            DestroyImmediate(materialID_DiffuseTex);
        }
        
        if (materialID_PBRTex)
        {
            SaveTexture2D(materialID_PBRTex);
            DestroyImmediate(materialID_PBRTex);
        }
        
        // cmd?.Release();
    }

    private const string path = "Assets/Graphics/PipelineFeature/GTAO/Scene/";

    void SaveTexture2D(in Texture2D tex)
    {
        string texPath = path + tex.name + ".png";

        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        
        #if UNITY_EDITOR
        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
        importer.sRGBTexture        = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode         = FilterMode.Point;
        importer.wrapMode           = TextureWrapMode.Clamp;
        importer.anisoLevel         = 0;
        importer.SaveAndReimport();
        #endif
    }
}