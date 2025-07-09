using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

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

        enum ToolsOrder
        {
            ClearMissScriptsInSelectionPrefab       = 1000,
            ClearParticleUnuseMeshInSelectionPrefab = 1010,
            ReserializeSelectionPrefab              = 1011,
            ReserializeSelectionScene               = 1020,

            ReserializeAllPrefab              = 2000,
            ReserializeAllScene               = 2010,
            ClearParticleUnuseMeshInAllPrefab = 2048,
            CleanAllMaterials                 = 2050,
            
            Last                              = 3001,
        }
        
        //ClearMaterialProperties 不删除_MainTex(而是置空)，UI.maintexture需要
        private static readonly string[] ExcudeMaterialTexProperties = { "_MainTex" };

        // Shader Keywords缓存(Key: shaderName; Value: shader Keywords)
        private static Dictionary<string, string[]> shaderKeywordsCache;

        [MenuItem("GameObject/当前场景 清理 MissScripts", false, (int)ToolsOrder.Last)]
        public static void ClearMissScriptsInCurrentScene()
        {
            Scene        currentScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects  = currentScene.GetRootGameObjects();

            int        missCount = 0;
            GameObject root, child;
            
            for (int i = 0; i < rootObjects.Length; i++)
            {
                root = rootObjects[i];
                
                var allChilds = root.GetComponentsInChildren<Transform>(true);

                for (int j = 0; j < allChilds.Length; j++)
                {
                    child = allChilds[j].gameObject;
                    
                    missCount += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child);
                }
            }

            if (missCount > 0)
                EditorSceneManager.SaveScene(currentScene);

            Debug.LogFormat("currentScene: <{0}> Found and removed {1} missing scripts.",
                            currentScene.name,
                            missCount);
        }


        
        [MenuItem("Assets/Assets Cleaner/选中Prefab 清理 MissScripts", false, (int)ToolsOrder.ClearMissScriptsInSelectionPrefab)]
        public static void ClearMissScriptsInSelectionPrefab()
        {
            var gos = Selection.GetFiltered<GameObject>(SelectionMode.DeepAssets)
                               .ToArray();
            
            GameObject item;
            for (int i = 0; i < gos.Length; i++)
            {
                item = gos[i];
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
        
        
        [MenuItem("Assets/Assets Cleaner/选中Prefab 重新序列化", false, (int)ToolsOrder.ReserializeSelectionPrefab)]
        public static void ReserializeSelectionPrefab()
        {
            var gos = Selection.GetFiltered<Object>(SelectionMode.DeepAssets)
                               .ToArray();
            
            Object item;
            for (int i = 0; i < gos.Length; i++)
            {
                item = gos[i];
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
        

        [MenuItem("Assets/Assets Cleaner/选中Prefab 清理Particle没用到的Mesh", false, (int)ToolsOrder.ClearParticleUnuseMeshInSelectionPrefab)]
        public static void ClearParticleUnuseMeshInSelectionPrefab()
        {
            var gos = Selection.GetFiltered<GameObject>(SelectionMode.DeepAssets)
                               .ToArray();
            
            ParticleSystemRenderer[] renderers;
            
            ParticleSystemRenderer   particle;
            
            for (int i = 0; i < gos.Length; i++)
            {
                var item = gos[i];
                if (item && PrefabUtility.GetPrefabAssetType(item) != PrefabAssetType.NotAPrefab)
                {
                    renderers = item.GetComponentsInChildren<ParticleSystemRenderer>(true);

                    for (int j = 0; j < renderers.Length; j++)
                    {
                        particle = renderers[j];
                        if (particle.renderMode != ParticleSystemRenderMode.Mesh)
                        {
                            particle.mesh = null;
                            EditorUtility.SetDirty(item);
                            
                            Debug.LogFormat("ClearParticleUnuseMesh : name = {0}, particle Node = {1}", gos[i].name, particle.name);
                        }
                    }
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> {0} Success. </color>",
                            nameof(ClearParticleUnuseMeshInSelectionPrefab));
        }
        

        [MenuItem("Assets/Assets Cleaner/选中Scene 重新序列化", false, (int)ToolsOrder.ReserializeSelectionScene)]
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
        
        
        
        
        

        [MenuItem("Assets/Assets Cleaner/所有Prefab 清理Particle没用到的Mesh", false, (int)ToolsOrder.ClearParticleUnuseMeshInAllPrefab)]
        public static void ClearParticleUnuseMeshInAllPrefab()
        {
            var paths = AssetDatabase.FindAssets("t:prefab", new string[] { "Assets" })
                                     .Select(AssetDatabase.GUIDToAssetPath)
                                     .ToArray();

            GameObject prefab;

            ParticleSystemRenderer[] renderers;
            
            ParticleSystemRenderer   particle;

            float percent = 1f / paths.Length;

            for (int i = 0; i < paths.Length; i++)
            {
                EditorUtility.DisplayProgressBar($"{nameof(ClearParticleUnuseMeshInAllPrefab)}", $"进度：{i + 1} / {paths.Length}", i * percent);
                
                prefab    = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                renderers = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
                
                for (int j = 0; j < renderers.Length; j++)
                {
                    particle = renderers[j];
                    if (particle.renderMode != ParticleSystemRenderMode.Mesh)
                    {
                        particle.mesh = null;
                        EditorUtility.SetDirty(prefab);
                        
                        Debug.LogFormat("ClearParticleUnuseMesh : path = {0}, particle = {1}", paths[i], particle.name);
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> {0} Success. </color>",
                            nameof(ClearParticleUnuseMeshInAllPrefab));
        }
        
        
        [MenuItem("Assets/Assets Cleaner/所有Prefab 重新序列化", false, (int)ToolsOrder.ReserializeAllPrefab)]
        public static void ReserializeAllPrefab()
        {
            ReserializeAllAssets("prefab");
        }
        

        
        [MenuItem("Assets/Assets Cleaner/所有Scene 重新序列化", false, (int)ToolsOrder.ReserializeAllScene)]
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
        
        
        
        [MenuItem("Assets/Assets Cleaner/所有Material 清理无用关键词与属性", false, (int)ToolsOrder.CleanAllMaterials)]
        public static void CleanAllMaterials()
        {
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            
            if (shaderKeywordsCache == null)
            {
                shaderKeywordsCache = new Dictionary<string, string[]>();
            }
            else
            {
                shaderKeywordsCache.Clear();
            }
            
            List<string> materialPaths = AssetDatabase.FindAssets("t:material", new string[] { "Assets" })
                                                      .Select(AssetDatabase.GUIDToAssetPath)
                                                      .ToList();

            (int clearMaterialCount, int clearPropertyCount, int clearKeyWorldCount) = (0, 0, 0);

            Object     obj;
            Material[] materials;
            Material material;

            string path;

            float percent = 1f / materialPaths.Count;
            
            for (int i = 0; i < materialPaths.Count; i++)
            {
                EditorUtility.DisplayProgressBar($"{nameof(CleanAllMaterials)}", $"进度：{i + 1} / {materialPaths.Count}", i * percent);
                
                
                path = materialPaths[i];

                //某些prefab里存多个material
                if (path.EndsWith(".prefab") || path.EndsWith(".fbx"))
                {
                    obj = AssetDatabase.LoadAssetAtPath(path, typeof(Object));
                    materials = AssetDatabase.LoadAllAssetsAtPath(path)
                                             .Select(x=> x as Material)
                                             .ToArray();

                    bool isDirty = false;
                    for (int j = 0; j < materials.Length; j++)
                    {
                        material = materials[j];
                        if (material && CleanMaterialSerializedProperty(material, ref clearPropertyCount, ref clearKeyWorldCount))
                        {
                            clearMaterialCount++;

                            EditorUtility.SetDirty(material);

                            isDirty = true;
                            
                            Debug.LogFormat("CleanMaterial: prefab = {0}, material = {1}", path, material.name);
                        }
                    }

                    if (isDirty) EditorUtility.SetDirty(obj);
                }
                else
                {
                    material = AssetDatabase.LoadAssetAtPath<Material>(path);

                    if (CleanMaterialSerializedProperty(material, ref clearPropertyCount, ref clearKeyWorldCount))
                    {
                        clearMaterialCount++;

                        EditorUtility.SetDirty(material);

                        Debug.LogFormat("CleanMaterial: {0}", path);
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            stopwatch.Stop();
            Debug.LogFormat("<color={0}><{1}> ： 本次清理{2}个材质，共{3}个Property, {4}个keyword, 用时:{5}s.</color>",
                            clearMaterialCount > 0 ? "green" : "white",
                            nameof(CleanAllMaterials),
                            clearMaterialCount,
                            clearPropertyCount,
                            clearKeyWorldCount,
                            (stopwatch.ElapsedMilliseconds * 0.001).ToString("F3"));
        }
        
        private static bool CleanMaterialSerializedProperty(Material material, ref int propertyCount, ref int keyWorldCount)
        {
            
            using SerializedObject so = new SerializedObject(material);

            SerializedProperty saveProperty = so.FindProperty("m_SavedProperties");

            bool result = false;

            result |= RemoveKeyWord(material, ref keyWorldCount);

            //m_Ints不用管
            result |= RemoveElement(saveProperty, material, "m_TexEnvs", ref propertyCount);
            result |= RemoveElement(saveProperty, material, "m_Floats", ref propertyCount);
            result |= RemoveElement(saveProperty, material, "m_Colors", ref propertyCount);
            
            so.ApplyModifiedProperties();
            
            return result;
        }

        private static bool RemoveKeyWord(Material material, ref int keyWorldCount)
        {
            bool result = false;

            Shader shader = material.shader;
            
            if (shader)
            {
                //能处理Resources/unity_builtin_extra
                bool isRecord = shaderKeywordsCache.TryGetValue(shader.name, out var shaderKeywords);

                if (!isRecord)
                {
                    shaderKeywords = GetLocalShaderKeywords(shader);
                    shaderKeywordsCache.Add(shader.name, shaderKeywords);
                }

                if (shaderKeywords != null)
                {
                    List<string> matKeywords = new List<string>(material.shaderKeywords);
                    for (int i = matKeywords.Count - 1; i >= 0; i--)
                    {
                        if (shaderKeywords.Contains(matKeywords[i]))
                            continue;

                        matKeywords.Remove(matKeywords[i]);
                        keyWorldCount++;
                        result = true;
                    }

                    if (result)
                    {
                        material.shaderKeywords = matKeywords.ToArray();
                    }
                }
            }

            return result;
        }
        
        private static string[] GetGlobalShaderKeywords(Shader shader)
        {
            return ShaderVariant.Utils.RflxStaticCall(
                                                      typeof(ShaderUtil),
                                                      "GetShaderGlobalKeywords",
                                                      new object[] { shader }) as string[];
        }
        
        private static string[] GetLocalShaderKeywords(Shader shader)
        {
            return ShaderVariant.Utils.RflxStaticCall(
                                                      typeof(ShaderUtil),
                                                      "GetShaderLocalKeywords",
                                                      new object[] { shader }) as string[];
        }

        public static ulong GetVariantCount(Shader s, bool usedBySceneOnly)
        {
            return (ulong)ShaderVariant.Utils.RflxStaticCall(
                                                             typeof(ShaderUtil),
                                                             "GetVariantCount", 
                                                             new object[] { s, usedBySceneOnly });
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
                        
                        propertyCount++;
                        result = true;
                    }
                    else
                    {
                        property.GetArrayElementAtIndex(i)
                                .FindPropertyRelative("second")
                                .FindPropertyRelative("m_Texture")
                                .objectReferenceValue = null;
                    }

                }
                
            }

            return result;
        }
    }
}