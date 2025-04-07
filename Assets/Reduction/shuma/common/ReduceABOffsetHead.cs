#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using System;

public class ReduceABOffsetHead : EditorWindow
{
    [MenuItem("Tools/ReduceAssetsBoundleOffsetHead")]
    private static void ShowWindow()
    {
        ReduceABOffsetHead window = EditorWindow.GetWindow<ReduceABOffsetHead>();
        window.Show();
    }

    public string assetsRootDir = "E:/AssetsStudioWorkSpace/baby/assets";

    // UnityFS. ab包特征head 与offset加密head一样..
    //private readonly char[] head = { (char)0x55, (char)0x6E, (char)0x69, (char)0x74, (char)0x79, (char)0x46, (char)0x53, (char)0x00};
    private readonly byte[] head = { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53};

    //offsetHead最大长度 一般不会太大 增大每个ab文件体积
    //读到超出这个长度 break
    public int maxHeadLen = 256;

    private string fileSuffix = ".ab";
    private string decodeEnd = "_decode";


    private void OnGUI()
    {
        assetsRootDir = EditorGUILayout.TextField(".ab资源文件根目录:", assetsRootDir);
        maxHeadLen = EditorGUILayout.IntSlider(maxHeadLen, 64, 1024);
        fileSuffix = EditorGUILayout.TextField("fileSuffix", fileSuffix);
        decodeEnd = EditorGUILayout.TextField("decodeEnd", decodeEnd);
        if (GUILayout.Button("去掉加密偏移头文件"))
        {
            if (!string.IsNullOrEmpty(assetsRootDir)) {
                ReduceAllHead(assetsRootDir);
            }
        }
        if (GUILayout.Button("删除原加密文件"))
        {
            if (!string.IsNullOrEmpty(assetsRootDir))
            {
                DeleteAllEncryptedFile(assetsRootDir);
            }
        }
    }

    /// <summary>
    /// 解密后删除原文件
    /// </summary>
    /// <param name="root"></param>
    private void DeleteAllEncryptedFile(string root)
    {
        abList = new List<string>();//所有.ab
        findFile(root, fileSuffix, decodeEnd + fileSuffix);//递归取所有绝对路径
        string tmpPath;
        for (int i = 0; i < abList.Count; i++)
        {
            EditorUtility.DisplayProgressBar("进度", "", 1f / abList.Count);
            tmpPath = abList[i];

            DeleteFile(tmpPath);
        }
        EditorUtility.ClearProgressBar();
        Debug.Log("delete finished! ~~");
    }

    private void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void ReduceAllHead(string root)
    {
        abList = new List<string>();//所有.ab
        findFile(root,fileSuffix);//递归取所有绝对路径
        string tmpPath;//遍历解析
        for (int i = 0; i < abList.Count; i++)
        {
            EditorUtility.DisplayProgressBar("进度", "", 1f / abList.Count);
            tmpPath = abList[i];
            if (!tmpPath.EndsWith(fileSuffix))
                Debug.LogWarning("has some warming, path = " + tmpPath);

            reduceABOffsetHead(tmpPath);
        }
        EditorUtility.ClearProgressBar();
        Debug.Log("decoding finished! ~~");
    }

    void reduceABOffsetHead(string path)
    {
        //string path = folderDir + "/fbx/role/dress/p_m_01_001_mmm.ab";
        int offset = -1;
        using (FileStream fs = new FileStream(path, FileMode.Open))
        {
            using (BinaryReader br = new BinaryReader(fs, System.Text.Encoding.ASCII))
            {
                offset = findSecondHeadIndex(br);
            }
        }
        //Debug.Log(offset);

        if (offset > 0)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open))
            {
                fs.Seek(offset + 1, SeekOrigin.Begin);
                using (BinaryReader br = new BinaryReader(fs, System.Text.Encoding.ASCII))
                {
                    string newPath = path.Insert(path.Length - 3, decodeEnd);
                    using (FileStream fs2 = new FileStream(newPath, FileMode.OpenOrCreate))
                    {
                        using (BinaryWriter bw = new BinaryWriter(fs2))
                        {
                            while (br.PeekChar() != -1)
                            {
                                byte tmp = br.ReadByte();
                                bw.Write(tmp);
                            }
                            //Debug.Log(" write finished! - PATH = " + path);
                        }
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("this ab assets no second head was found : " + path);
        }
    }

    List<string> abList;
    private void findFile(string sourcePath,string endstr)
    {
        //List<string> list = new List<string>();
        DirectoryInfo info = new DirectoryInfo(sourcePath);
        DirectoryInfo[] dirs = info.GetDirectories();
        FileInfo[] files = info.GetFiles();
        foreach (var i in dirs)
        {
            findFile(i.FullName, endstr);
        }
        foreach (var i in files)
        {
            if (i.FullName.EndsWith(endstr))
                abList.Add(i.FullName);
        }
    }
    private void findFile(string sourcePath, string endstr, string filter)
    {
        //List<string> list = new List<string>();
        DirectoryInfo info = new DirectoryInfo(sourcePath);
        DirectoryInfo[] dirs = info.GetDirectories();
        FileInfo[] files = info.GetFiles();
        foreach (var i in dirs)
        {
            findFile(i.FullName, endstr, filter);
        }
        foreach (var i in files)
        {
            if (i.FullName.EndsWith(endstr) && !i.FullName.EndsWith(filter))
                abList.Add(i.FullName);
        }
    }

    /// <summary>
    /// 找到第二个head标识的开始下标
    /// </summary>
    /// <returns></returns>
    private int findSecondHeadIndex(BinaryReader br)
    {
        int headIndex = 0, bodyIndex = -1, headLen = head.Length;
        int headNum = 0;
        byte read;
        while (br.PeekChar() != -1)
        {
            bodyIndex++;
            read = br.ReadByte();
            if (head[headIndex] == read)
            {
                headIndex++;
                //Debug.Log(" read = " +(char)read + " index = " + bodyIndex);
                if (headIndex >= headLen - 1)
                {
                    ++headNum;
                    //Debug.Log(headNum + " = head Num = " + headIndex);
                    headIndex = 0;
                    if (headNum >= 2)
                    {
                        //break; 
                        return bodyIndex - headLen + 1;
                    }
                }
            }
            else
            {
                headIndex = 0;
            }
            if (bodyIndex >= maxHeadLen) {
                //break;
                //Debug.Log("break " + headNum + " = head Num = " + headIndex);

                return -1;
            }
        }
        //if (headNum >= 2)
        //    return bodyIndex - headLen + 1;
        //else
            return -1;
    }
}

#endif