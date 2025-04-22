using System.Linq;
using UnityEngine;
using UnityEditor;

//问题多 废弃
public class SmoothNormal
{

    const string FbxSuffix = ".fbx";

    static readonly string[] MeshReadPath = new[]
    {
        "Assets/Reduction/shuma/Monster/jinshujialulu",
    };

    // [MenuItem("网格工具/SoomthNormal", false, 120)]
    public static void SoomthNormalBySelect()
    {
        var meshPaths = AssetDatabase.FindAssets("t:mesh", MeshReadPath)
            .Select(x => AssetDatabase.GUIDToAssetPath(x)).ToArray();

        if (meshPaths.Length > 0)
        {
            // SoomthMesh(meshPaths);
        }
        else
        {
            Debug.LogFormat("{typeof(SmoothNormal)} ： 没有找到有效的fbx");
        }
        
    }

    //右键菜单以Assets开头，否则为顶栏
    // [MenuItem("Assets/网格工具/SoomthNormal", false, 130)]
    public static void SoomthNormal()
    {
        var meshPaths = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.DeepAssets)
            .Select(x => AssetDatabase.GetAssetPath(x))
            .Where(x => x.EndsWith(FbxSuffix)).ToArray();


        if (meshPaths.Length > 0)
        {
            // SoomthMesh(meshPaths);
        }
        else
        {
            Debug.LogFormat("{typeof(SmoothNormal)} ： 没有找到有效的fbx");
        }
    }
}