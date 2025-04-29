using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.VisualScripting;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SVTXPainter
{
    [ExecuteInEditMode]
    public class SVTXObject : MonoBehaviour
    {
#if UNITY_EDITOR
        [Serializable]
        public class Record
        {
            public PaintLayerType layer = 0;
            public bool useColor32 = false;

            // for undo
            public int index = 0;
            public Color[] colors;
            public List<Vector4> f4;
        }

        [SerializeField] Record m_record = new Record();
        int m_historyIndex = 0;
        PaintLayerType m_historyLayer = 0;
        bool m_useColor32 = false;

        public void PushUndo(PaintLayerType layer, bool useColor32)
        {
            var mesh = SVTXPainterUtils.GetMesh(gameObject);
            if (mesh != null)
            {
                m_record.index = m_historyIndex;
                m_historyIndex++;
                m_record.layer = m_historyLayer;
                m_historyLayer = layer;
                m_record.useColor32 = m_useColor32;
                m_useColor32 = useColor32;
                switch (layer)
                {
                    case PaintLayerType.Color:
                        Color[] colors = mesh.colors;
                        m_record.colors = new Color[colors.Length];
                        Array.Copy(colors, m_record.colors, colors.Length);
                        colors = null;
                        break;
                    default:
                        List<Vector4> uv = new List<Vector4>();
                        mesh.GetUVs((int)layer, uv);
                        if (m_record.f4 != null)
                        {
                            m_record.f4.Clear();
                            m_record.f4 = null;
                        }
                        m_record.f4 = uv;
                        break;
                }

                Undo.RegisterCompleteObjectUndo(this, "Simple Vertex Painter [" + m_record.index + "]");
            }
        }

        //撤回总是把Paint/preView状态取消，还有点逻辑bug，不管了 能用
        public void OnUndoRedo()
        {
            var mesh = SVTXPainterUtils.GetMesh(gameObject);
            if (mesh == null)
            {
                return;
            }

            if (m_historyIndex != m_record.index)
            {
                m_historyIndex = m_record.index;
                m_useColor32 = m_record.useColor32;
                m_historyLayer = m_record.layer;
                switch (m_historyLayer)
                {
                    case PaintLayerType.Color:
                        if (m_record.colors != null && mesh.colors != null && m_record.colors.Length == mesh.colors.Length)
                        {
                            if (m_useColor32)
                            {
                                mesh.SetColors(Array.ConvertAll(m_record.colors, input => (Color32)input));
                            }
                            else
                            {
                                mesh.SetColors(m_record.colors);
                            }
                            //Debug.Log("UndoRedo");
                        }

                        break;
                    default:
                        int layer = (int)m_historyLayer;
                        List<Vector4> uv = new List<Vector4>();
                        mesh.GetUVs(layer, uv);
                        if (m_record.f4 != null && uv.Count > 0 && m_record.f4.Count == uv.Count)
                        {
                            mesh.SetUVs(layer, m_record.f4);
                        }

                        uv.Clear();
                        uv = null;
                        break;
                }
            }
        }

        private void OnEnable()
        {
            UnityEditor.Undo.undoRedoPerformed += OnUndoRedo;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            InitMat();
        }

        private void OnDisable()
        {
            UnityEditor.Undo.undoRedoPerformed -= OnUndoRedo;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            UnInitMat();
        }

        private bool isPaintView = false;
        private Material paintViewMat;
        private Material srcMat;
        private Renderer _renderer;

        void InitMat()
        {
            _renderer = gameObject.GetComponent<Renderer>();
            srcMat = _renderer.sharedMaterial;
            paintViewMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/CustomTools/Engine/MeshTool/SimpleVertexPainter/PaintPreviewMaterial.mat");
            isPaintView = srcMat == paintViewMat;
        }

        void UnInitMat()
        {
            _renderer.material = srcMat;
            // srcMat = null;
            paintViewMat = null;
            _renderer = null;
        }

        public void ChanggeView()
        {
            if (_renderer && srcMat && paintViewMat)
            {
                switch (isPaintView)
                {
                    case true:
                        _renderer.material = srcMat;
                        break;
                    case false:
                        _renderer.material = paintViewMat;
                        break;
                }

                isPaintView = !isPaintView;
            }
        }

        public void RestoreView()
        {
            isPaintView = true;
            ChanggeView();
        }
#endif
    }
}