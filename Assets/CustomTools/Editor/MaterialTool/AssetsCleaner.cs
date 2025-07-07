using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    /// <summary>
    /// MaterialClean剩下的问题
    /// 内置Lit ShaderGUI会置空以下同名材质Properties，自行处理
    /// ObsoleteProperties
    /// [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
    /// [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
    /// [HideInInspector] _GlossMapScale("Smoothness", Float) = 0.0
    /// [HideInInspector] _Glossiness("Smoothness", Float) = 0.0
    /// [HideInInspector] _GlossyReflections("EnvironmentReflections", Float) = 0.0
    ///
    /// [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
    /// [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
    /// [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    /// </summary>
    public class AssetsCleaner
    {
        
        //ClearMaterialProperties 不删除_MainTex(而是置空)，个别内置shader需要
        private static readonly string[] ExcudeMaterialTexProperties = { "_MainTex" };


        [MenuItem("GameObject/清理当前场景中的 MissScripts", false, 2000)]
        public static void ClearMissScriptsInCurrentScene()
        {
            Scene        currentScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects  = currentScene.GetRootGameObjects();

            int missCount = 0;
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                
                var allChilds = root.GetComponentsInChildren<Transform>(true);

                for (int j = 0; j < allChilds.Length; j++)
                {
                    GameObject child = allChilds[j].gameObject;
                    
                    missCount += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child);
                }
            }

            if (missCount > 0)
                EditorSceneManager.SaveScene(currentScene);

            Debug.LogFormat("currentScene: <{0}> Found and removed {1} missing scripts.",
                            currentScene.name,
                            missCount);
        }
        
        
        [MenuItem("Assets/Assets Cleaner/重新序列化选中Prefab", false, 1901)]
        public static void ReserializeSelectionPrefab()
        {
            var gos = Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets);
            for (int i = 0; i < gos.Length; i++)
            {
                var item = gos[i];
                // Filter non-prefab type,
                if (item && PrefabUtility.GetPrefabAssetType(item) != PrefabAssetType.NotAPrefab)
                {
                    EditorUtility.SetDirty(item);
                }
                
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> {0} Success. </color>", nameof(ReserializeSelectionPrefab));
        }


        
        [MenuItem("Assets/Assets Cleaner/清理选中Prefab里的 MissScripts", false, 1908)]
        public static void ClearMissScriptsInSelectionPrefab()
        {
            var gos = Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets);
            for (int i = 0; i < gos.Length; i++)
            {
                var item = gos[i] as GameObject;
                // Filter non-prefab type,
                if (item && PrefabUtility.GetPrefabAssetType(item) != PrefabAssetType.NotAPrefab)
                {
                    var allChilds = item.GetComponentsInChildren<Transform>(true);
                    
                    for (int j = 0; j < allChilds.Length; j++)
                    {
                        GameObject child = allChilds[j].gameObject;
                        
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child);
                    }
                }
                
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> {0} Success. </color>", nameof(ClearMissScriptsInSelectionPrefab));
        }
        

        [MenuItem("Assets/Assets Cleaner/清理选中Prefab里没用到的Mesh", false, 1910)]
        public static void ClearParticleUnuseMeshInSelectionPrefab()
        {
            var gos = Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets)
                               .Select(x =>  x as GameObject)
                               .ToArray();
            for (int i = 0; i < gos.Length; i++)
            {
                var item = gos[i];
                if (item && PrefabUtility.GetPrefabAssetType(item) != PrefabAssetType.NotAPrefab)
                {
                    ParticleSystemRenderer[] particleSystemRenderers = 
                        item.GetComponentsInChildren<ParticleSystemRenderer>(true);

                    for (int j = 0; j < particleSystemRenderers.Length; j++)
                    {
                        ParticleSystemRenderer particle = particleSystemRenderers[j];
                        if (particle.renderMode != ParticleSystemRenderMode.Mesh)
                        {
                            particle.mesh = null;
                            EditorUtility.SetDirty(item);
                        }
                    }
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> {0} Success. </color>",
                            nameof(ClearParticleUnuseMeshInSelectionPrefab));
        }
        

        [MenuItem("Assets/Assets Cleaner/重新序列化选中Scene", false, 1920)]
        public static void ReserializeSelectionScene()
        {
            var gos = Selection.GetFiltered<SceneAsset>(SelectionMode.DeepAssets);
            for (int i = 0; i < gos.Length; i++)
            {
                var item = gos[i];
                if(item)
                {
                    EditorUtility.SetDirty(item);
                    // AssetDatabase.SaveAssetIfDirty(item);
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> Reserialize Selection {0} Success. </color>", nameof(SceneAsset));
        }
        
        
        [MenuItem("Assets/Assets Cleaner/重新序列化所有Prefab", false, 2000)]
        public static void ReserializeAllPrefab()
        {
            ReserializeAllAssets("prefab");
        }
        

        
        [MenuItem("Assets/Assets Cleaner/重新序列化所有Scene", false, 2010)]
        public static void ReserializeAllScene()
        {
            ReserializeAllAssets("scene");
        }
        
        
        private static void ReserializeAllAssets(string type)
        {
            AssetDatabase.StartAssetEditing();
            var paths = AssetDatabase.FindAssets($"t:{type}", new string[] { "Assets" })
                                      .Select(AssetDatabase.GUIDToAssetPath);

            //能去掉过时的字段，同时也会补上可空字段（带默认值），SetDirty也一样 罢了 就这样吧
            AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssets);
            
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> Reserialize All {0} Assets Success. </color>", type.FirstCharacterToUpper());
        }
        
        
        
        [MenuItem("Assets/Assets Cleaner/清理所有Material无用关键词与属性", false, 2020)]
        public static void CleanAllMaterials()
        {
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            
            List<string> materialPaths = AssetDatabase.FindAssets("t:material", new string[] { "Assets" })
                                                      .Select(AssetDatabase.GUIDToAssetPath)
                                                      .ToList();

            (int clearMaterialCount, int clearPropertyCount) = (0, 0);

            Material material;
            
            for (int i = 0; i < materialPaths.Count; i++)
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPaths[i]);

                if (CleanMaterialSerializedProperty(material, ref clearPropertyCount))
                    clearMaterialCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            stopwatch.Stop();
            Debug.LogFormat("<color={0}><{1}> ： 本次清理{2}个材质，共{3}个无效Property, 用时:{4}s.</color>",
                            clearMaterialCount > 0 ? "green" : "white",
                            nameof(CleanAllMaterials),
                            clearMaterialCount,
                            clearPropertyCount,
                            (stopwatch.ElapsedMilliseconds * 0.001).ToString("F3"));
        }
        
        private static bool CleanMaterialSerializedProperty(Material material, ref int propertyCount)
        {
            using SerializedObject so = new SerializedObject(material);

            SerializedProperty saveProperty = so.FindProperty("m_SavedProperties");

            bool result = false;

            //m_Ints不用管
            result |= RemoveElement(saveProperty, material, "m_TexEnvs", ref propertyCount);
            result |= RemoveElement(saveProperty, material, "m_Floats", ref propertyCount);
            result |= RemoveElement(saveProperty, material, "m_Colors", ref propertyCount);
            
            so.ApplyModifiedProperties();

            if (result)
                Debug.LogFormat("CleanMaterial: {0}", material.name);
            
            return result;
        }

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RemoveElement(SerializedProperty saveProperty, 
                                          Material material,
                                          string name, 
                                          ref int propertyCount)
        {
            SerializedProperty property = saveProperty.FindPropertyRelative(name);

            bool result = false;
            for (int i = property.arraySize - 1; i >= 0; i--)
            {
                var    prop         = property.GetArrayElementAtIndex(i);
                string propertyName = prop.displayName;
                
                if (!material.HasProperty(propertyName))
                {
                    if (!ExcudeMaterialTexProperties.Contains(propertyName))
                    {
                        property.DeleteArrayElementAtIndex(i);
                    }
                    else
                    {
                        property.GetArrayElementAtIndex(i)
                                .FindPropertyRelative("second")
                                .FindPropertyRelative("m_Texture")
                                .objectReferenceValue = null;
                    }

                    propertyCount++;
                    result = true;
                }
            }

            return result;
        }
    }
}