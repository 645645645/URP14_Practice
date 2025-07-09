using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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
        
        //ClearMaterialProperties 不删除_MainTex(而是置空)，UI.maintexture需要
        private static readonly string[] ExcudeMaterialTexProperties = { "_MainTex" };
        
        // 没有CGPROGRAM/ENDCG部分的Pass代码不会有keyword定义，因此可忽略
        private static string codePattern = @"Pass[\r\n\s\t]*\{[\r\n\s\t]*(?<passInfo>\S((?!ENDCG)[\s\S])*)CGPROGRAM(\r\n|\r|\n)(?<passCode>((?!ENDCG)[\s\S])+)ENDCG[\r\n\s\t]*\}";
        
        // 提取multi_compile keyword
        private static string multiCompilePattern = @"#pragma multi_compile\s+([_]+\s+)?(?<multiCompile>[a-zA-Z0-9_]+(\s+[a-zA-Z0-9_]+)*)";
        
        // 提取shader_feature keyword
        private static string shaderFeaturePattern = @"#pragma shader_feature\s+([_]+\s+)?(?<shaderFeature>[a-zA-Z0-9_]+(\s+[a-zA-Z0-9_]+)*)";

        // Shader Keywords缓存(Key: shaderPath; Value: shader Keywords)
        private static Dictionary<string, HashSet<string>> shaderKeywordsCache;//to File

        [MenuItem("GameObject/清理当前场景中的 MissScripts", false, 2000)]
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


        
        [MenuItem("Assets/Assets Cleaner/清理选中Prefab里的 MissScripts", false, 1900)]
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
        
        
        [MenuItem("Assets/Assets Cleaner/重新序列化选中Prefab", false, 1901)]
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
        

        [MenuItem("Assets/Assets Cleaner/清理选中Prefab里没用到的Mesh", false, 1910)]
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


        [MenuItem("Assets/Assets Cleaner/清理所有Prefab里没用到的Mesh", false, 2001)]
        public static void ClearParticleUnuseMeshInAllPrefab()
        {
            var paths = AssetDatabase.FindAssets("t:prefab", new string[] { "Assets" })
                                     .Select(AssetDatabase.GUIDToAssetPath)
                                     .ToArray();

            GameObject prefab;

            ParticleSystemRenderer[] renderers;
            
            ParticleSystemRenderer   particle;

            for (int i = 0; i < paths.Length; i++)
            {
                prefab    = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                renderers = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
                
                for (int j = 0; j < renderers.Length; j++)
                {
                    particle = renderers[j];
                    if (particle.renderMode != ParticleSystemRenderMode.Mesh)
                    {
                        particle.mesh = null;
                        EditorUtility.SetDirty(prefab);
                    }
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.LogFormat("<color=green> {0} Success. </color>",
                            nameof(ClearParticleUnuseMeshInAllPrefab));
        }
        
        
        
        [MenuItem("Assets/Assets Cleaner/清理所有Material无用关键词与属性", false, 2020)]
        public static void CleanAllMaterials()
        {
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            
            if (shaderKeywordsCache == null)
            {
                shaderKeywordsCache = new Dictionary<string, HashSet<string>>();
            }
            else
            {
                shaderKeywordsCache.Clear();
            }
            
            List<string> materialPaths = AssetDatabase.FindAssets("t:material", new string[] { "Assets" })
                                                      .Select(AssetDatabase.GUIDToAssetPath)
                                                      .ToList();

            (int clearMaterialCount, int clearPropertyCount, int clearKeyWorldCount) = (0, 0, 0);

            Material material;

            string path;
            
            for (int i = 0; i < materialPaths.Count; i++)
            {
                path = materialPaths[i];
                
                material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (CleanMaterialSerializedProperty(material, ref clearPropertyCount, ref clearKeyWorldCount))
                {
                    clearMaterialCount++;

                    Debug.LogFormat("CleanMaterial: {0}", path);
                }
            }

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
            if (material.shader)
            {
                string shaderPath = AssetDatabase.GetAssetPath(material.shader);

                bool isRecord = shaderKeywordsCache.TryGetValue(shaderPath, out var shaderKeywords);
                
                if (!isRecord)
                {
                    shaderKeywords = GetShaderKeywords(shaderPath);
                    shaderKeywordsCache.Add(shaderPath, shaderKeywords);
                }
                
                if (shaderKeywords != null)
                {
                    List<string> matKeywords = new List<string>(material.shaderKeywords);
                    for (int i = matKeywords.Count - 1; i >= 0; --i)
                    {
                        if (shaderKeywords.Contains(matKeywords[i]))
                            continue;

                        matKeywords.Remove(matKeywords[i]);
                        keyWorldCount++;
                        result = true;
                    }
                    if(result)
                    {
                        material.shaderKeywords = matKeywords.ToArray();
                        EditorUtility.SetDirty(material);
                        AssetDatabase.SaveAssetIfDirty(material);
                    }
                }
            }

            return result;
        }

        private static HashSet<string> GetShaderKeywords(string shaderPath)
        {
            HashSet<string> totalKeywords = new HashSet<string>();

            try
            {
                StreamReader sr         = new StreamReader(shaderPath, Encoding.Default);
                string       shaderCode = sr.ReadToEnd();

                MatchCollection codeMatches;
                MatchCollection multiCompileMatches;
                MatchCollection shaderFeatureMatches;
                codeMatches = Regex.Matches(shaderCode, codePattern);

                string passCode;
                string multiCompileKeywords;
                string shaderFeatureKeywords;

                // 分析每一个Pass
                for (int i = 0; i < codeMatches.Count; ++i)
                {
                    passCode = codeMatches[i].Groups["passCode"].Value;

                    multiCompileMatches = Regex.Matches(passCode, multiCompilePattern);
                    for (int j = 0; j < multiCompileMatches.Count; ++j)
                    {
                        multiCompileKeywords = multiCompileMatches[j].Groups["multiCompile"].Value;
                        if (!string.IsNullOrEmpty(multiCompileKeywords))
                        {
                            totalKeywords.AddRange(multiCompileKeywords.Split(' '));
                        }
                    }

                    shaderFeatureMatches = Regex.Matches(passCode, shaderFeaturePattern);
                    for (int k = 0; k < shaderFeatureMatches.Count; ++k)
                    {
                        shaderFeatureKeywords = shaderFeatureMatches[k].Groups["shaderFeature"].Value;
                        if (!string.IsNullOrEmpty(shaderFeatureKeywords))
                        {
                            totalKeywords.AddRange(shaderFeatureKeywords.Split(' '));
                        }
                    }
                }

            }
            catch
            {
            }

            return totalKeywords;
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