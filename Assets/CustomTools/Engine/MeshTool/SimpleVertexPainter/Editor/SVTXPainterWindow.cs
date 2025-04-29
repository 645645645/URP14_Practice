using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using SVTXPainter;

/*
 * reference : https://github.com/alpacasking/SimpleVertexPainter/tree/master
 */
namespace SVTXPainterEditor
{
    public class SVTXPainterWindow : EditorWindow
    {
        #region Variables

        private GUIStyle titleStyle;
        private bool allowPainting = false;
        private bool changingBrushValue = false;
        private bool allowSelect = false;
        private bool isPainting = false;
        private bool isRecord = false;
        private bool useColor32 = false;

        private Vector2 mousePos = Vector2.zero;
        private Vector2 lastMousePos = Vector2.zero;
        private RaycastHit curHit;


        private float brushSize = 0.1f;
        private float brushOpacity = 1f;
        private float brushFalloff = 0.1f;

        private Color brushColor;
        private float brushIntensity;

        private const float MinBrushSize = 0.001f;
        public const float MaxBrushSize = 1f;

        private PaintLayerType curLayerType = PaintLayerType.Color;
        private PaintChannelType curColorChannel = PaintChannelType.All;

        private Mesh curMesh;
        private SVTXObject m_target;
        private GameObject m_active;

        #endregion

        #region Main Method

        [MenuItem("Tools/MeshEditor/Simple Vertex Painter", false, 1100)]
        public static void LauchVertexPainter()
        {
            var window = EditorWindow.GetWindow<SVTXPainterWindow>();
            window.titleContent = new GUIContent("Simple Vertex Painter");
            window.Show();
            window.OnSelectionChange();
            window.GenerateStyles();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui -= this.OnSceneGUI;
            SceneView.duringSceneGui += this.OnSceneGUI;
            if (titleStyle == null)
            {
                GenerateStyles();
            }
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= this.OnSceneGUI;
        }

        private void OnSelectionChange()
        {
            m_active = null;
            curMesh = null;
            GameObject select = Selection.activeGameObject;
            if (select != null)
            {
                SVTXObject target = select.GetComponent<SVTXObject>();
                curMesh = SVTXPainterUtils.GetMesh(select);
                if (target != null)
                {
                    if (m_target && m_target != target)
                        m_target.RestoreView();
                }
                m_target = target;
                if (curMesh != null)
                {
                    m_active = select;
                }
                
            }
            else
            {
                if (m_target)
                    m_target.RestoreView();

                m_target = null;
            }

            allowSelect = (m_target == null);

            Repaint();
        }

        #endregion

        #region GUI Methods

        private void OnGUI()
        {
            //Header
            GUILayout.BeginHorizontal();
            GUILayout.Box("Simple Vertex Painter", titleStyle, GUILayout.Height(60), GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            //Body
            GUILayout.BeginVertical(GUI.skin.box);

            if (m_target != null)
            {
                if (!m_target.isActiveAndEnabled)
                {
                    EditorGUILayout.LabelField("(Enable " + m_target.name + " to show Simple Vertex Painter)");
                }
                else
                {
                    //bool lastAP = allowPainting;
                    allowPainting = GUILayout.Toggle(allowPainting, "Paint Mode");

                    if (allowPainting)
                    {
                        //Selection.activeGameObject = null;
                        Tools.current = Tool.None;
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Paint Layer:", GUILayout.Width(90));
                    curLayerType = (PaintLayerType)EditorGUILayout.EnumPopup(curLayerType, GUILayout.Width(60));
                    if(curLayerType == PaintLayerType.Color)
                    {
                        GUILayout.Space(50);
                        useColor32 = EditorGUILayout.ToggleLeft("useColor32", useColor32);
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Paint Channel:", GUILayout.Width(90));
                    curColorChannel = (PaintChannelType)EditorGUILayout.EnumPopup(curColorChannel, GUILayout.Width(60));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (curColorChannel == PaintChannelType.All)
                    {
                        brushColor = EditorGUILayout.ColorField("Brush Color:", brushColor);
                    }
                    else
                    {
                        brushIntensity = EditorGUILayout.Slider("Intensity:", brushIntensity, 0, 1);
                    }

                    if (GUILayout.Button("Fill"))
                    {
                        FillVertexColor();
                    }

                    GUILayout.EndHorizontal();
                    brushSize = EditorGUILayout.Slider("Brush Size:", brushSize, MinBrushSize, MaxBrushSize);
                    brushOpacity = EditorGUILayout.Slider("Brush Opacity:", brushOpacity, 0, 1);
                    brushFalloff = EditorGUILayout.Slider("Brush Falloff:", brushFalloff, MinBrushSize, brushSize);

                    if (GUILayout.Button("Export .asset file") && curMesh != null)
                    {
                        string path = EditorUtility.SaveFilePanel("Export .asset file", "Assets", SVTXPainterUtils.SanitizeForFileName(curMesh.name), "asset");
                        if (path.Length > 0)
                        {
                            var dataPath = Application.dataPath;
                            if (!path.StartsWith(dataPath))
                            {
                                Debug.LogError("Invalid path: Path must be under " + dataPath);
                            }
                            else
                            {
                                path = path.Replace(dataPath, "Assets");
                                AssetDatabase.CreateAsset(Instantiate(curMesh), path);
                                Debug.Log("Asset exported: " + path);
                            }
                        }
                    }

                    Shader.SetGlobalInteger("_PreviewLayer", (int)curLayerType);
                    Shader.SetGlobalInteger("_PreviewChannel", (int)curColorChannel);
                    //Footer
                    GUILayout.Label("Key Z:Turn on or off\n" +
                                    "Right mouse button:Paint\n" +
                                    "Right mouse button+Shift:Opacity\n" +
                                    "Right mouse button+Ctrl:Size\n" +
                                    "Right mouse button+Shift+Ctrl:Falloff\n\n" +
                                    "single channel use gray view",
                        EditorStyles.helpBox);
                    
                    if (GUILayout.Button("ChangeView"))
                    {
                        if (m_target)
                        {
                            m_target.ChanggeView();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Delect Current UV.w"))
                    {
                        DelectUVLayerChannel(curLayerType, 1);
                    }

                    if (GUILayout.Button("Delect Current UV.zw"))
                    {
                        DelectUVLayerChannel(curLayerType, 2);
                    }

                    GUILayout.EndHorizontal();

                    GUILayout.Space(10);
                    if (GUILayout.Button("Delect Current Layer.(color or uv)"))
                    {
                        DelectLayerl(curLayerType);
                    }

                    GUILayout.Space(10);

                    Repaint();
                }
            }
            else if (m_active != null)
            {
                if (GUILayout.Button("Add SVTX Object to " + m_active.name))
                {
                    m_active.AddComponent<SVTXObject>();
                    OnSelectionChange();
                }
            }
            else
            {
                EditorGUILayout.LabelField("Please select a mesh or skinnedmesh.");
            }

            GUILayout.EndVertical();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (allowPainting)
            {
                bool isHit = false;
                if (!allowSelect)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                }

                Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePos);
                if (m_target != null && curMesh != null)
                {
                    Matrix4x4 mtx = m_target.transform.localToWorldMatrix;
                    RaycastHit tempHit;
                    isHit = RXLookingGlass.IntersectRayMesh(worldRay, curMesh, mtx, out tempHit);
                    if (isHit)
                    {
                        if (!changingBrushValue)
                        {
                            curHit = tempHit;
                        }

                        //Debug.Log("ray cast success");
                        if (isPainting && m_target.isActiveAndEnabled && !changingBrushValue)
                        {
                            PaintVertexColor();
                        }
                    }
                }

                if (isHit || changingBrushValue)
                {
                    Handles.color = getSolidDiscColor(curColorChannel);
                    Handles.DrawSolidDisc(curHit.point, curHit.normal, brushSize);
                    Handles.color = getWireDiscColor(curColorChannel);
                    Handles.DrawWireDisc(curHit.point, curHit.normal, brushSize);
                    Handles.DrawWireDisc(curHit.point, curHit.normal, brushFalloff);
                }
            }


            ProcessInputs();

            sceneView.Repaint();
        }

        private void OnInspectorUpdate()
        {
            OnSelectionChange();
        }

        #endregion

        #region TempPainter Method

        private Color32[] _color32 = new Color32[0];
        private Color[] _color = new Color[0];
        private List<Vector4> _uvs = new List<Vector4>();
        private Vector3[] _verts;

        void PaintVertexColor()
        {
            if (m_target && m_active)
            {
                curMesh = SVTXPainterUtils.GetMesh(m_active);
                if (curMesh)
                {
                    if (isRecord)
                    {
                        m_target.PushUndo(curLayerType, useColor32);
                        isRecord = false;
                    }

                    int layer = (int)curLayerType;
                    _verts = curMesh.vertices;
                    if (curLayerType == PaintLayerType.Color)
                    {
                        if (curMesh.colors.Length > 0)
                        {
                            if (useColor32)
                                _color32 = curMesh.colors32;
                            else
                                _color = curMesh.colors;
                        }
                        else
                        {
                            if (useColor32)
                                _color32 = new Color32[_verts.Length];
                            else
                                _color = new Color[_verts.Length];
                        }
                    }
                    else
                    {
                        _uvs.Clear();
                        if (curMesh.uv.Length > 0)
                        {
                            curMesh.GetUVs(layer, _uvs);
                        }

                        if (_uvs.Count < 1)
                        {
                            _uvs.AddRange(Enumerable.Repeat<Vector4>(getSolidDiscColor(curColorChannel), _verts.Length));
                            Debug.LogFormat("Creat uv layer succed.");
                        }
                    }

                    for (int i = 0; i < _verts.Length; i++)
                    {
                        Vector3 vertPos = m_target.transform.TransformPoint(_verts[i]);
                        float mag = (vertPos - curHit.point).magnitude;
                        if (mag > brushSize)
                        {
                            continue;
                        }

                        float falloff = SVTXPainterUtils.LinearFalloff(mag, brushSize);
                        falloff = Mathf.Pow(falloff, Mathf.Clamp01(1 - brushFalloff / brushSize)) * brushOpacity;
                        if (curLayerType == PaintLayerType.Color)
                        {
                            if (curColorChannel == PaintChannelType.All)
                            {
                                if(useColor32)
                                    _color32[i] = SVTXPainterUtils.VTXColorLerp(_color32[i], brushColor, falloff);
                                else
                                    _color[i] = SVTXPainterUtils.VTXColorLerp(_color[i], brushColor, falloff);
                            }
                            else
                            {
                                if(useColor32)
                                    _color32[i] = SVTXPainterUtils.VTXOneChannelLerp(_color32[i], (byte)(brushIntensity * byte.MaxValue), falloff, curColorChannel);
                                else
                                    _color[i] = SVTXPainterUtils.VTXOneChannelLerp(_color[i], brushIntensity, falloff, curColorChannel);
                            }
                        }
                        else
                        {
                            if (curColorChannel == PaintChannelType.All)
                            {
                                _uvs[i] = SVTXPainterUtils.VTXColorLerp(_uvs[i], brushColor, falloff);
                            }
                            else
                            {
                                _uvs[i] = SVTXPainterUtils.VTXOneChannelLerp(_uvs[i], brushIntensity, falloff, curColorChannel);
                            }
                        }
                        //Debug.Log("Blend");
                    }

                    switch (curLayerType)
                    {
                        case PaintLayerType.Color:
                            if (useColor32)
                                curMesh.SetColors(_color32);
                            else
                                curMesh.SetColors(_color);
                            break;
                        default:
                            curMesh.SetUVs(layer, _uvs);
                            break;
                    }
                }
                else
                {
                    OnSelectionChange();
                    Debug.LogWarning("Nothing to paint!");
                }
            }
            else
            {
                OnSelectionChange();
                Debug.LogWarning("Nothing to paint!");
            }
        }

        void FillVertexColor()
        {
            if (curMesh)
            {
                int layer = (int)curLayerType;
                _verts = curMesh.vertices;
                if (curLayerType == PaintLayerType.Color)
                {
                    if (curMesh.colors.Length > 0)
                    {
                        if (useColor32)
                            _color32 = curMesh.colors32;
                        else
                            _color = curMesh.colors;
                    }
                    else
                    {
                        if (useColor32)
                            _color32 = new Color32[_verts.Length];
                        else
                            _color = new Color[_verts.Length];
                    }
                }
                else
                {
                    _uvs.Clear();
                    if (curMesh.uv.Length > 0)
                    {
                        curMesh.GetUVs(layer, _uvs);
                    }

                    //说明没有对应uv
                    if (_uvs.Count < 1)
                    {
                        _uvs.AddRange(Enumerable.Repeat<Vector4>(getSolidDiscColor(curColorChannel), _verts.Length));
                        Debug.LogFormat("Creat uv layer succed.");
                    }
                }

                for (int i = 0; i < _verts.Length; i++)
                {
                    if (curLayerType == PaintLayerType.Color)
                    {
                        if (curColorChannel == PaintChannelType.All)
                        {
                            if (useColor32)
                                _color32[i] = brushColor;
                            else
                                _color[i] = brushColor;
                        }
                        else
                        {
                            if (useColor32)
                                _color32[i] = SVTXPainterUtils.VTXOneChannelLerp(_color32[i], (byte)(byte.MaxValue * brushIntensity), 1, curColorChannel);
                            else
                                _color[i] = SVTXPainterUtils.VTXOneChannelLerp(_color[i], brushIntensity, 1, curColorChannel);
                        }
                    }
                    else
                    {
                        if (curColorChannel == PaintChannelType.All)
                        {
                            _uvs[i] = brushColor;
                        }
                        else
                        {
                            _uvs[i] = SVTXPainterUtils.VTXOneChannelLerp(_uvs[i], brushIntensity, 1, curColorChannel);
                        }
                    }
                    //Debug.Log("Blend");
                }

                switch (curLayerType)
                {
                    case PaintLayerType.Color:
                        if (useColor32)
                            curMesh.SetColors(_color32);
                        else
                            curMesh.SetColors(_color);
                        break;
                    default:
                        curMesh.SetUVs(layer, _uvs);
                        break;
                }
            }
            else
            {
                Debug.LogWarning("Nothing to fill!");
            }
        }

        #endregion

        #region Utility Methods

        void ProcessInputs()
        {
            if (m_target == null)
            {
                return;
            }

            Event e = Event.current;
            mousePos = e.mousePosition;
            if (e.type == EventType.KeyDown)
            {
                if (e.isKey)
                {
                    if (e.keyCode == KeyCode.Z)
                    {
                        allowPainting = !allowPainting;
                        if (allowPainting)
                        {
                            Tools.current = Tool.None;
                        }
                    }
                }
            }

            if (e.type == EventType.MouseUp)
            {
                changingBrushValue = false;
                isPainting = false;
            }

            if (lastMousePos == mousePos)
            {
                isPainting = false;
            }

            if (allowPainting)
            {
                if (e.type == EventType.MouseDrag && e.control && e.button == 0 && !e.shift)
                {
                    brushSize += e.delta.x * 0.005f;
                    brushSize = Mathf.Clamp(brushSize, MinBrushSize, MaxBrushSize);
                    brushFalloff = Mathf.Clamp(brushFalloff, MinBrushSize, brushSize);
                    changingBrushValue = true;
                }

                if (e.type == EventType.MouseDrag && !e.control && e.button == 0 && e.shift)
                {
                    brushOpacity += e.delta.x * 0.005f;
                    brushOpacity = Mathf.Clamp01(brushOpacity);
                    changingBrushValue = true;
                }

                if (e.type == EventType.MouseDrag && e.control && e.button == 0 && e.shift)
                {
                    brushFalloff += e.delta.x * 0.005f;
                    brushFalloff = Mathf.Clamp(brushFalloff, MinBrushSize, brushSize);
                    changingBrushValue = true;
                }

                if ((e.type == EventType.MouseDrag || e.type == EventType.MouseDown) && !e.control && e.button == 0 && !e.shift && !e.alt)
                {
                    isPainting = true;
                    if (e.type == EventType.MouseDown)
                    {
                        isRecord = true;
                    }
                }
            }

            lastMousePos = mousePos;
        }

        void GenerateStyles()
        {
            titleStyle = new GUIStyle();
            titleStyle.border = new RectOffset(3, 3, 3, 3);
            titleStyle.margin = new RectOffset(2, 2, 2, 2);
            titleStyle.fontSize = 25;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleCenter;
        }

        Color getSolidDiscColor(PaintChannelType pt)
        {
            switch (pt)
            {
                case PaintChannelType.All:
                    return new Color(brushColor.r, brushColor.g, brushColor.b, brushOpacity);
                case PaintChannelType.R:
                    return new Color(brushIntensity, 0, 0, brushOpacity);
                case PaintChannelType.G:
                    return new Color(0, brushIntensity, 0, brushOpacity);
                case PaintChannelType.B:
                    return new Color(0, 0, brushIntensity, brushOpacity);
                case PaintChannelType.A:
                    return new Color(brushIntensity, brushIntensity, brushIntensity, brushOpacity);
            }

            return Color.white;
        }

        Color getWireDiscColor(PaintChannelType pt)
        {
            switch (pt)
            {
                case PaintChannelType.All:
                    return new Color(1 - brushColor.r, 1 - brushColor.g, 1 - brushColor.b, 1);
                case PaintChannelType.R:
                    return Color.white;
                case PaintChannelType.G:
                    return Color.white;
                case PaintChannelType.B:
                    return Color.white;
                case PaintChannelType.A:
                    return Color.white;
            }

            return Color.white;
        }

        #endregion

        #region DelectLayerChannel

        void DelectLayerl(PaintLayerType layerType)
        {
            if (curMesh != null)
            {
                int layer = (int)layerType;
                switch (layerType)
                {
                    case PaintLayerType.Color:
                        curMesh.SetColors(new Color[0]);
                        break;
                    default:
                        curMesh.SetUVs(layer, new Vector2[0]);
                        break;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="layerType"></param>
        /// <param name="channelCount">1:w, 2:zw</param>
        void DelectUVLayerChannel(PaintLayerType layerType, int channelCount)
        {
            if (null != curMesh)
            {
                if (channelCount == 1 || channelCount == 2)
                {
                    int layer = (int)layerType;

                    switch (layerType)
                    {
                        case PaintLayerType.Color:
                            Debug.LogFormat("oh!, Single channel can't be delected from Color layer.");
                            break;
                        default:
                            List<Vector4> uvs = new List<Vector4>();
                            curMesh.GetUVs(layer, uvs);
                            if (channelCount == 1)
                            {
                                List<Vector3> newUvList = new List<Vector3>();
                                foreach (var uv in uvs)
                                {
                                    var newUv = new Vector3(uv.x, uv.y, uv.z);
                                    newUvList.Add(newUv);
                                }

                                curMesh.SetUVs(layer, newUvList);
                                if (curColorChannel == PaintChannelType.A)
                                {
                                    curColorChannel = PaintChannelType.B;
                                }
                            }
                            else if (channelCount == 2)
                            {
                                List<Vector2> newUvList = new List<Vector2>();
                                foreach (var uv in uvs)
                                {
                                    var newUv = new Vector2(uv.x, uv.y);
                                    newUvList.Add(newUv);
                                }

                                curMesh.SetUVs(layer, newUvList);
                                if (curColorChannel == PaintChannelType.A ||
                                    curColorChannel == PaintChannelType.B)
                                {
                                    curColorChannel = PaintChannelType.G;
                                }
                            }


                            break;
                    }
                }
            }
        }

        #endregion
    }
}