using UnityEngine;
using System.Collections.Generic;
//https://github.com/danbaidong1111/SmoothNormal
#if UNITY_EDITOR
namespace UnityEditor.SmoothNormalTool
{
    /// <summary>
    /// Hook into the import pipeline, compute smoothNormal and write it to mesh data.
    /// Note that we only changed UNITY's mesh data, the original modelfile has not changed.
    /// </summary>
    public class SmoothNormalPostprocessor : AssetPostprocessor
    {
        private static readonly int s_DISTANCE_THRESHOLD = Shader.PropertyToID("_DISTANCE_THRESHOLD");
        /// <summary>
        /// After importing model.
        /// </summary>
        /// <param name="gameObject"></param>
        private void OnPostprocessModel(GameObject gameObject)
        {
            SmoothNormalConfig config = SmoothNormalConfig.instance;

            // Matching file
            switch (config.matchingMethod)
            {
                case SmoothNormalConfig.MatchingMethod.NameSuffix:
                    if (!gameObject.name.Contains(config.matchingNameSuffix))
                        return;
                    break;
                case SmoothNormalConfig.MatchingMethod.FilePath:
                    string path = assetImporter.assetPath;
                    if (!path.Contains(config.matchingFilePath))
                        return;
                    break;
                default:
                    return;
            }

            ComputeShader smoothNormalCS = config.shaders.smoothNormalCS;
            smoothNormalCS.SetFloat(s_DISTANCE_THRESHOLD, config.vertDistThresold);

            bool useOctahedron = false;
            bool smoothNormalToTangentSpace = true;
            switch (config.writeTarget)
            {
                case SmoothNormalConfig.WriteTarget.VertexColorRG:
                case SmoothNormalConfig.WriteTarget.TangentXY:
                case SmoothNormalConfig.WriteTarget.UV0ZW:
                case SmoothNormalConfig.WriteTarget.UV1XY:
                case SmoothNormalConfig.WriteTarget.UV1ZW:
                    useOctahedron = true;
                    break;
                case SmoothNormalConfig.WriteTarget.NormalXYZ:
                    smoothNormalToTangentSpace = false;
                    break;
            }
            

            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            long lastTimeStamp = 0;

            List<Mesh> meshes = new List<Mesh>();
            // Get all meshes
            {
                MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>();
                foreach (MeshFilter meshFilter in meshFilters)
                {
                    meshes.Add(meshFilter.sharedMesh);
                }

                SkinnedMeshRenderer[] skinnedMeshs = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (SkinnedMeshRenderer skinnedMesh in skinnedMeshs)
                {
                    meshes.Add(skinnedMesh.sharedMesh);
                }
            }

            // Compute smoothNormals
            {
                foreach (Mesh mesh in meshes)
                {
                    // Init vert Color
                    Color[] vertexColors;
                    bool retainColorA = false;
                    if (mesh.colors != null && mesh.colors.Length != 0)
                    {
                        vertexColors = mesh.colors;
                        retainColorA = true;
                    }
                    else
                    {
                        vertexColors = new Color[mesh.vertexCount];
                    }
                    
                    Vector3[] smoothNormals = SmoothNormalHelper.ComputeSmoothNormal(mesh, smoothNormalCS, smoothNormalToTangentSpace, useOctahedron);
                    Vector3[] normals = mesh.normals;
                    Vector4[] tangents = mesh.tangents;
                    Vector2[] uv0 = mesh.uv;
                    Vector2[] uv1 = mesh.uv2;
                    switch (config.writeTarget)
                    {
                        case SmoothNormalConfig.WriteTarget.VertexColorRGB:
                            SmoothNormalHelper.CopyVector3NormalsToColorRGB(ref smoothNormals, ref vertexColors, vertexColors.Length, retainColorA);
                            mesh.SetColors(vertexColors);
                            break;
                        case SmoothNormalConfig.WriteTarget.VertexColorRG:
                            SmoothNormalHelper.CopyVector3NormalsToColorRG(ref smoothNormals, ref vertexColors, vertexColors.Length, retainColorA);
                            mesh.SetColors(vertexColors);
                            break;
                        case SmoothNormalConfig.WriteTarget.TangentXYZ:
                            SmoothNormalHelper.CopyVector3NormalsToTangentXYZ(ref smoothNormals, ref tangents, vertexColors.Length);
                            mesh.SetTangents(tangents);
                            break;
                        case SmoothNormalConfig.WriteTarget.TangentXY:
                            SmoothNormalHelper.CopyVector3NormalsToTangentXY(ref smoothNormals, ref tangents, vertexColors.Length);
                            mesh.SetTangents(tangents);
                            break;
                        case SmoothNormalConfig.WriteTarget.NormalXYZ:
                            SmoothNormalHelper.CopyVector3NormalsToNormalXYZ(ref smoothNormals, ref normals, vertexColors.Length);
                            mesh.SetNormals(normals);
                            break;
                        case SmoothNormalConfig.WriteTarget.UV0ZW:
                            List<Vector4> uv0zw = new List<Vector4>(uv0.Length);
                            SmoothNormalHelper.CopyVector3NormalsToUVZW(ref smoothNormals, ref uv0zw, ref uv0, uv0.Length);
                            mesh.SetUVs(0, uv0zw);
                            break;
                        case SmoothNormalConfig.WriteTarget.UV1XY:
                            SmoothNormalHelper.CopyVector3NormalsToUVXY(ref smoothNormals, ref uv1, uv1.Length);
                            mesh.SetUVs(1, uv1);
                            break;
                        case SmoothNormalConfig.WriteTarget.UV1XYZ:
                            List<Vector3> uv1xyz = new List<Vector3>(uv1.Length);
                            SmoothNormalHelper.CopyVector3NormalsToUV1XYZ(ref smoothNormals, ref uv1xyz, uv1.Length);
                            mesh.SetUVs(1, uv1xyz);
                            break;
                        case SmoothNormalConfig.WriteTarget.UV1ZW:
                            List<Vector4> uv1zw = new List<Vector4>(uv1.Length);
                            SmoothNormalHelper.CopyVector3NormalsToUVZW(ref smoothNormals, ref uv1zw, ref uv1, uv0.Length);
                            mesh.SetUVs(1, uv1zw);
                            break;
                    }

                    EditorUtility.SetDirty(mesh);
                }
            }

            stopwatch.Stop();
            Debug.Log("Generate " + gameObject.name + " smoothNormal use: " + ((stopwatch.ElapsedMilliseconds - lastTimeStamp) * 0.001).ToString("F3") + "s");
            
        }
    }
    
    
}
#endif