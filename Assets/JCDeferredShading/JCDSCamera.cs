﻿using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;

namespace JCDeferredShading
{
    [RequireComponent(typeof(Camera))]
    public class JCDSCamera : MonoBehaviour
    {
        private static JCDSCamera s_instance = null;
        public static JCDSCamera instance
        {
            get
            {
                return s_instance;
            }
        }

        public bool debug = false;

        public Mesh pointLightMesh = null;

        private Camera cam = null;

        // 1 : diffuse(rgb) shininess(a)
        // 2 : normal(rgb)
        // 3 : position(rgb)
        private JCDSRenderTexture mrtGBuffer = null;

        // 1 : result (rgb) depth
        private JCDSRenderTexture resultRT = null;

        private Material compositeResultBufferMtrl = null;

        private int shaderPropId_diffuseBuffer = 0;
        private int shaderPropId_normalBuffer = 0;
        private int shaderPropId_positionBuffer = 0;
        private int shaderPropId_resultBuffer = 0;

        private int shaderPropId_dirLightDir = 0;
        private int shaderPropId_dirLightColor = 0;
        private int shaderPropId_dirLightIntensity = 0;

        private int shaderPropId_pointLightPos = 0;
        private int shaderPropId_pointLightColor = 0;
        private int shaderPropId_pointLightRange = 0;

        private Light[] dirLights = null;
        private Light[] pointLights = null;

        private void Awake()
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                Debug.LogError("Unsupported RenderTexture Format ARGBHalf");
                Destroy(this);
                return;
            }

            s_instance = this;

            cam = GetComponent<Camera>();

            compositeResultBufferMtrl = new Material(Shader.Find("Hidden/JCDeferredShading/CompositeResultBuffer"));

            shaderPropId_diffuseBuffer = Shader.PropertyToID("_DiffuseBuffer");
            shaderPropId_normalBuffer = Shader.PropertyToID("_NormalBuffer");
            shaderPropId_positionBuffer = Shader.PropertyToID("_PositionBuffer");
            shaderPropId_resultBuffer = Shader.PropertyToID("_ResultBuffer");

            shaderPropId_dirLightDir = Shader.PropertyToID("_DirLightDir");
            shaderPropId_dirLightColor = Shader.PropertyToID("_DirLightColor");

            shaderPropId_pointLightPos = Shader.PropertyToID("_PointLightPos");
            shaderPropId_pointLightColor = Shader.PropertyToID("_PointLightColor");
            shaderPropId_pointLightRange = Shader.PropertyToID("_PointLightRange");

            mrtGBuffer = new JCDSRenderTexture(
                3, Screen.width, Screen.height,
                JCDSRenderTexture.ValueToMask(null), 
                RenderTextureFormat.ARGBHalf, FilterMode.Point, false
            );

            resultRT = new JCDSRenderTexture(
                1, Screen.width, Screen.height,
                JCDSRenderTexture.ValueToMask(new bool[] { true }), 
                RenderTextureFormat.ARGB32, FilterMode.Point, false    
            );

            CollectLights();
        }

        private void OnDestroy()
        {
            if (mrtGBuffer != null)
            {
                mrtGBuffer.Destroy();
                mrtGBuffer = null;
            }
            if(resultRT != null)
            {
                resultRT.Destroy();
                resultRT = null;
            }

            s_instance = null;
        }

        private void OnPreRender()
        {
            mrtGBuffer.ResetSize(Screen.width, Screen.height);
            resultRT.ResetSize(Screen.width, Screen.height);

            JCDSRenderTexture.SetMultipleRenderTargets(cam, mrtGBuffer, resultRT, 0);
        }

        private void OnPostRender()
        {
            resultRT.SetActiveRenderTexture(0);
            JCDSRenderTexture.ClearActiveRenderTexture(true, true, Color.black, 0.0f);

            compositeResultBufferMtrl.SetTexture(shaderPropId_diffuseBuffer, mrtGBuffer.GetRenderTexture(0));
            compositeResultBufferMtrl.SetTexture(shaderPropId_normalBuffer, mrtGBuffer.GetRenderTexture(1));
            compositeResultBufferMtrl.SetTexture(shaderPropId_positionBuffer, mrtGBuffer.GetRenderTexture(2));
            compositeResultBufferMtrl.SetTexture(shaderPropId_resultBuffer, resultRT.GetRenderTexture(0));

            int numDirLights = dirLights == null ? 0 : dirLights.Length;
            for (int i = 0; i < numDirLights; ++i)
            {
                Light light = dirLights[i];
                if (light != null)
                {
                    Vector3 dir = -light.transform.forward;
                    compositeResultBufferMtrl.SetVector(shaderPropId_dirLightDir, new Vector4(dir.x, dir.y, dir.z, light.intensity));
                    compositeResultBufferMtrl.SetColor(shaderPropId_dirLightColor, light.color);
                    DrawScreenQuad(compositeResultBufferMtrl, 0, false, false);
                }
            }

            int numPointLights = pointLights == null ? 0 : pointLights.Length;
            for (int i = 0; i < numPointLights; ++i)
            {
                Light light = pointLights[i];
                if (light != null)
                {
                    compositeResultBufferMtrl.SetVector(shaderPropId_pointLightPos, light.transform.position);
                    compositeResultBufferMtrl.SetColor(shaderPropId_pointLightColor, light.color);
                    compositeResultBufferMtrl.SetVector(shaderPropId_pointLightRange, new Vector4(1.0f / light.range, light.intensity, 0, 0));
                    compositeResultBufferMtrl.SetPass(2);
                    Graphics.DrawMeshNow(pointLightMesh, Matrix4x4.TRS(light.transform.position, Quaternion.identity, Vector3.one * light.range * 2));
                }
            }

            JCDSRenderTexture.ResetActiveRenderTexture();
            DrawScreenQuad(compositeResultBufferMtrl, 3, true, true);
        }

        private void OnGUI()
        {
            if (debug)
            {
                int width = (int)(Screen.width * 0.25f);
                int height = (int)(Screen.height * 0.25f);
                int numRTs = mrtGBuffer.numRTs;
                Rect rect = new Rect(0, 0, width, height);
                for (int i = 0; i < numRTs; ++i)
                {
                    GUI.DrawTexture(rect, mrtGBuffer.GetRenderTexture(i), ScaleMode.ScaleToFit, false);
                    rect.y += height;
                }
            }
        }

        public void CollectLights()
        {
            dirLights = Light.GetLights(LightType.Directional, 0);
            pointLights = Light.GetLights(LightType.Point, 0);
        }

        private void DrawScreenQuad(Material mtrl, int pass, bool isScreen, bool clearScreen)
        {
            GraphicsDeviceType type = SystemInfo.graphicsDeviceType;
            bool isOpenGLLike =
                type == GraphicsDeviceType.OpenGL2 ||
                type == GraphicsDeviceType.OpenGLCore ||
                type == GraphicsDeviceType.OpenGLES2 ||
                type == GraphicsDeviceType.OpenGLES3;

            bool isUvUpsideDown = isOpenGLLike || isScreen;

            if (clearScreen)
            {
                GL.Clear(true, true, Color.black);
            }
            mtrl.SetPass(isUvUpsideDown ? pass : (pass + 1));
            GL.PushMatrix();
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0, 0);
            GL.Vertex3(-1, -1, 0);
            GL.TexCoord2(0, 1);
            GL.Vertex3(-1, 1, 0);
            GL.TexCoord2(1, 1);
            GL.Vertex3(1, 1, 0);
            GL.TexCoord2(1, 0);
            GL.Vertex3(1, -1, 0);
            GL.End();
            GL.PopMatrix();
        }
    }
}