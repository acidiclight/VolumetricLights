//  Copyright(c) 2016, Michal Skalsky
//  All rights reserved.
//
//  Redistribution and use in source and binary forms, with or without modification,
//  are permitted provided that the following conditions are met:
//
//  1. Redistributions of source code must retain the above copyright notice,
//     this list of conditions and the following disclaimer.
//
//  2. Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//
//  3. Neither the name of the copyright holder nor the names of its contributors
//     may be used to endorse or promote products derived from this software without
//     specific prior written permission.
//
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
//  EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
//  OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.IN NO EVENT
//  SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
//  SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT
//  OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
//  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
//  TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
//  EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.



using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;
using System;
using UnityEngine.Assertions;
using UnityEngine.Serialization;

namespace VolumetricLights
{
    [RequireComponent(typeof(Camera))]
    public class VolumetricLightRenderer : MonoBehaviour
    {
        private static VolumetricLightRenderer current = null;
        
        private enum VolumetricResolution
        {
            Full,
            Half,
            Quarter
        };

        public static event Action<VolumetricLightRenderer, Matrix4x4> PreRenderEvent;

        private static Mesh pointLightMesh;
        private static Mesh spotLightMesh;
        private static Material lightMaterial;

        private Camera cam;
        private CommandBuffer preLightPass;

        private Matrix4x4 viewProj;
        private Material blitAddMaterial;
        private Material bilateralBlurMaterial;

        private Shader blitAdd;
        private Shader bilateralBlur;
        private Shader volumetricLightShader;
        
        
        private RenderTexture volumeLightTexture;
        private RenderTexture halfVolumeLightTexture;
        private RenderTexture quarterVolumeLightTexture;

        private RenderTexture halfDepthBuffer;
        private RenderTexture quarterDepthBuffer;
        private VolumetricResolution currentResolution = VolumetricResolution.Half;
        private Texture2D ditheringTexture;
        private Texture3D noiseTexture;

        [SerializeField]
        [FormerlySerializedAs("Resolution")]
        private VolumetricResolution resolution = VolumetricResolution.Half;
        
        [SerializeField]
        [FormerlySerializedAs("DefaultSpotCookie")]
        private Texture defaultSpotCookie;

        public CommandBuffer GlobalCommandBuffer => preLightPass;

        public static VolumetricLightRenderer Current => current;
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static Material GetLightMaterial()
        {
            return lightMaterial;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static Mesh GetPointLightMesh()
        {
            return pointLightMesh;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static Mesh GetSpotLightMesh()
        {
            return spotLightMesh;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public RenderTexture GetVolumeLightBuffer()
        {
            return resolution switch
            {
                VolumetricResolution.Quarter => quarterVolumeLightTexture,
                VolumetricResolution.Half => halfVolumeLightTexture,
                _ => volumeLightTexture
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public RenderTexture GetVolumeLightDepthBuffer()
        {
            return resolution switch
            {
                VolumetricResolution.Quarter => quarterDepthBuffer,
                VolumetricResolution.Half => halfDepthBuffer,
                _ => null
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public Texture GetDefaultSpotCookie()
        {
            return defaultSpotCookie;
        }

        /// <summary>
        /// 
        /// </summary>
        private void Awake()
        {
            // Find dependencies.
            blitAdd = Shader.Find("Hidden/BlitAdd");
            bilateralBlur = Shader.Find("Hidden/BilateralBlur");
            volumetricLightShader = Shader.Find("Sandbox/VolumetricLight");
            cam = GetComponent<Camera>();
            
            // Null-checks
            Assert.IsNotNull(cam);
            Assert.IsNotNull(blitAdd);
            Assert.IsNotNull(bilateralBlur);
            Assert.IsNotNull(volumetricLightShader);
            
            // Create materials
            blitAddMaterial = new Material(blitAdd);
            bilateralBlurMaterial = new Material(bilateralBlur);
            
            
            if (cam.actualRenderingPath == RenderingPath.Forward)
                cam.depthTextureMode = DepthTextureMode.Depth;

            currentResolution = resolution;

            preLightPass = new CommandBuffer();
            preLightPass.name = "PreLight";

            ChangeResolution();

            if (pointLightMesh == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pointLightMesh = go.GetComponent<MeshFilter>().sharedMesh;
                Destroy(go);
            }

            if (spotLightMesh == null)
            {
                spotLightMesh = CreateSpotLightMesh();
            }
            
            lightMaterial = new Material(volumetricLightShader);

            LoadNoise3dTexture();
            GenerateDitherTexture();
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnEnable()
        {
            if (current != null)
            {
                Debug.LogWarning($"Two {nameof(VolumetricLightRenderer)} objects enabled at once. This is not supported.");
                this.enabled = false;
                return;
            }

            current = this;
            
            //_camera.RemoveAllCommandBuffers();
            cam.AddCommandBuffer(cam.actualRenderingPath == RenderingPath.Forward ? CameraEvent.AfterDepthTexture : CameraEvent.BeforeLighting, preLightPass);
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnDisable()
        {
            if (current == this)
                current = null;
            
            //_camera.RemoveAllCommandBuffers();
            cam.RemoveCommandBuffer(cam.actualRenderingPath == RenderingPath.Forward ? CameraEvent.AfterDepthTexture : CameraEvent.BeforeLighting, preLightPass);
        }

        /// <summary>
        /// 
        /// </summary>
        private void ChangeResolution()
        {
            int width = cam.pixelWidth;
            int height = cam.pixelHeight;

            if (volumeLightTexture != null)
                Destroy(volumeLightTexture);

            volumeLightTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf);
            volumeLightTexture.name = "VolumeLightBuffer";
            volumeLightTexture.filterMode = FilterMode.Bilinear;

            if (halfDepthBuffer != null)
                Destroy(halfDepthBuffer);
            if (halfVolumeLightTexture != null)
                Destroy(halfVolumeLightTexture);

            if (resolution == VolumetricResolution.Half || resolution == VolumetricResolution.Quarter)
            {
                halfVolumeLightTexture = new RenderTexture(width / 2, height / 2, 0, RenderTextureFormat.ARGBHalf);
                halfVolumeLightTexture.name = "VolumeLightBufferHalf";
                halfVolumeLightTexture.filterMode = FilterMode.Bilinear;

                halfDepthBuffer = new RenderTexture(width / 2, height / 2, 0, RenderTextureFormat.RFloat);
                halfDepthBuffer.name = "VolumeLightHalfDepth";
                halfDepthBuffer.Create();
                halfDepthBuffer.filterMode = FilterMode.Point;
            }

            if (quarterVolumeLightTexture != null)
                Destroy(quarterVolumeLightTexture);
            if (quarterDepthBuffer != null)
                Destroy(quarterDepthBuffer);

            if (resolution == VolumetricResolution.Quarter)
            {
                quarterVolumeLightTexture = new RenderTexture(width / 4, height / 4, 0, RenderTextureFormat.ARGBHalf);
                quarterVolumeLightTexture.name = "VolumeLightBufferQuarter";
                quarterVolumeLightTexture.filterMode = FilterMode.Bilinear;

                quarterDepthBuffer = new RenderTexture(width / 4, height / 4, 0, RenderTextureFormat.RFloat);
                quarterDepthBuffer.name = "VolumeLightQuarterDepth";
                quarterDepthBuffer.Create();
                quarterDepthBuffer.filterMode = FilterMode.Point;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void OnPreRender()
        {

            // use very low value for near clip plane to simplify cone/frustum intersection
            Matrix4x4 proj = Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, 0.01f, cam.farClipPlane);

#if UNITY_2017_2_OR_NEWER
            if (UnityEngine.XR.XRSettings.enabled)
            {
                // when using VR override the used projection matrix
                proj = Camera.current.projectionMatrix;
            }
#endif

            proj = GL.GetGPUProjectionMatrix(proj, true);
            viewProj = proj * cam.worldToCameraMatrix;

            preLightPass.Clear();

            bool dx11 = SystemInfo.graphicsShaderLevel > 40;

            if (resolution == VolumetricResolution.Quarter)
            {
                Texture nullTexture = null;
                // down sample depth to half res
                preLightPass.Blit(nullTexture, halfDepthBuffer, bilateralBlurMaterial, dx11 ? 4 : 10);
                // down sample depth to quarter res
                preLightPass.Blit(nullTexture, quarterDepthBuffer, bilateralBlurMaterial, dx11 ? 6 : 11);

                preLightPass.SetRenderTarget(quarterVolumeLightTexture);
            }
            else if (resolution == VolumetricResolution.Half)
            {
                Texture nullTexture = null;
                // down sample depth to half res
                preLightPass.Blit(nullTexture, halfDepthBuffer, bilateralBlurMaterial, dx11 ? 4 : 10);

                preLightPass.SetRenderTarget(halfVolumeLightTexture);
            }
            else
            {
                preLightPass.SetRenderTarget(volumeLightTexture);
            }

            preLightPass.ClearRenderTarget(false, true, new Color(0, 0, 0, 1));

            UpdateMaterialParameters();

            if (PreRenderEvent != null)
                PreRenderEvent(this, viewProj);
        }

        [ImageEffectOpaque]
        public void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (resolution == VolumetricResolution.Quarter)
            {
                RenderTexture temp = RenderTexture.GetTemporary(quarterDepthBuffer.width, quarterDepthBuffer.height, 0, RenderTextureFormat.ARGBHalf);
                temp.filterMode = FilterMode.Bilinear;

                // horizontal bilateral blur at quarter res
                Graphics.Blit(quarterVolumeLightTexture, temp, bilateralBlurMaterial, 8);
                // vertical bilateral blur at quarter res
                Graphics.Blit(temp, quarterVolumeLightTexture, bilateralBlurMaterial, 9);

                // upscale to full res
                Graphics.Blit(quarterVolumeLightTexture, volumeLightTexture, bilateralBlurMaterial, 7);

                RenderTexture.ReleaseTemporary(temp);
            }
            else if (resolution == VolumetricResolution.Half)
            {
                RenderTexture temp = RenderTexture.GetTemporary(halfVolumeLightTexture.width, halfVolumeLightTexture.height, 0, RenderTextureFormat.ARGBHalf);
                temp.filterMode = FilterMode.Bilinear;

                // horizontal bilateral blur at half res
                Graphics.Blit(halfVolumeLightTexture, temp, bilateralBlurMaterial, 2);

                // vertical bilateral blur at half res
                Graphics.Blit(temp, halfVolumeLightTexture, bilateralBlurMaterial, 3);

                // upscale to full res
                Graphics.Blit(halfVolumeLightTexture, volumeLightTexture, bilateralBlurMaterial, 5);
                RenderTexture.ReleaseTemporary(temp);
            }
            else
            {
                RenderTexture temp = RenderTexture.GetTemporary(volumeLightTexture.width, volumeLightTexture.height, 0, RenderTextureFormat.ARGBHalf);
                temp.filterMode = FilterMode.Bilinear;

                // horizontal bilateral blur at full res
                Graphics.Blit(volumeLightTexture, temp, bilateralBlurMaterial, 0);
                // vertical bilateral blur at full res
                Graphics.Blit(temp, volumeLightTexture, bilateralBlurMaterial, 1);
                RenderTexture.ReleaseTemporary(temp);
            }

            // add volume light buffer to rendered scene
            blitAddMaterial.SetTexture("_Source", source);
            Graphics.Blit(volumeLightTexture, destination, blitAddMaterial, 0);
        }

        private void UpdateMaterialParameters()
        {
            bilateralBlurMaterial.SetTexture("_HalfResDepthBuffer", halfDepthBuffer);
            bilateralBlurMaterial.SetTexture("_HalfResColor", halfVolumeLightTexture);
            bilateralBlurMaterial.SetTexture("_QuarterResDepthBuffer", quarterDepthBuffer);
            bilateralBlurMaterial.SetTexture("_QuarterResColor", quarterVolumeLightTexture);

            Shader.SetGlobalTexture("_DitherTexture", ditheringTexture);
            Shader.SetGlobalTexture("_NoiseTexture", noiseTexture);
        }

        /// <summary>
        /// 
        /// </summary>
        private void Update()
        {
            //#if UNITY_EDITOR
            if (currentResolution != resolution)
            {
                currentResolution = resolution;
                ChangeResolution();
            }

            if ((volumeLightTexture.width != cam.pixelWidth || volumeLightTexture.height != cam.pixelHeight))
                ChangeResolution();
            //#endif
        }

        /// <summary>
        /// 
        /// </summary>
        private void LoadNoise3dTexture()
        {
            // basic dds loader for 3d texture - !not very robust!

            TextAsset data = Resources.Load("NoiseVolume") as TextAsset;

            byte[] bytes = data.bytes;

            uint height = BitConverter.ToUInt32(data.bytes, 12);
            uint width = BitConverter.ToUInt32(data.bytes, 16);
            uint pitch = BitConverter.ToUInt32(data.bytes, 20);
            uint depth = BitConverter.ToUInt32(data.bytes, 24);
            uint formatFlags = BitConverter.ToUInt32(data.bytes, 20 * 4);
            //uint fourCC = BitConverter.ToUInt32(data.bytes, 21 * 4);
            uint bitdepth = BitConverter.ToUInt32(data.bytes, 22 * 4);
            if (bitdepth == 0)
                bitdepth = pitch / width * 8;


            // doesn't work with TextureFormat.Alpha8 for some reason
            noiseTexture = new Texture3D((int)width, (int)height, (int)depth, TextureFormat.RGBA32, false);
            noiseTexture.name = "3D Noise";

            Color[] c = new Color[width * height * depth];

            uint index = 128;
            if (data.bytes[21 * 4] == 'D' && data.bytes[21 * 4 + 1] == 'X' && data.bytes[21 * 4 + 2] == '1' && data.bytes[21 * 4 + 3] == '0' &&
                (formatFlags & 0x4) != 0)
            {
                uint format = BitConverter.ToUInt32(data.bytes, (int)index);
                if (format >= 60 && format <= 65)
                    bitdepth = 8;
                else if (format >= 48 && format <= 52)
                    bitdepth = 16;
                else if (format >= 27 && format <= 32)
                    bitdepth = 32;

                //Debug.Log("DXGI format: " + format);
                // dx10 format, skip dx10 header
                //Debug.Log("DX10 format");
                index += 20;
            }

            uint byteDepth = bitdepth / 8;
            pitch = (width * bitdepth + 7) / 8;

            for (int d = 0; d < depth; ++d)
            {
                //index = 128;
                for (int h = 0; h < height; ++h)
                {
                    for (int w = 0; w < width; ++w)
                    {
                        float v = (bytes[index + w * byteDepth] / 255.0f);
                        c[w + h * width + d * width * height] = new Color(v, v, v, v);
                    }

                    index += pitch;
                }
            }

            noiseTexture.SetPixels(c);
            noiseTexture.Apply();
        }

        /// <summary>
        /// 
        /// </summary>
        private void GenerateDitherTexture()
        {
            if (ditheringTexture != null)
            {
                return;
            }

            int size = 8;
#if DITHER_4_4
        size = 4;
#endif
            // again, I couldn't make it work with Alpha8
            ditheringTexture = new Texture2D(size, size, TextureFormat.Alpha8, false, true);
            ditheringTexture.filterMode = FilterMode.Point;
            Color32[] c = new Color32[size * size];

            byte b;
#if DITHER_4_4
        b = (byte)(0.0f / 16.0f * 255); c[0] = new Color32(b, b, b, b);
        b = (byte)(8.0f / 16.0f * 255); c[1] = new Color32(b, b, b, b);
        b = (byte)(2.0f / 16.0f * 255); c[2] = new Color32(b, b, b, b);
        b = (byte)(10.0f / 16.0f * 255); c[3] = new Color32(b, b, b, b);

        b = (byte)(12.0f / 16.0f * 255); c[4] = new Color32(b, b, b, b);
        b = (byte)(4.0f / 16.0f * 255); c[5] = new Color32(b, b, b, b);
        b = (byte)(14.0f / 16.0f * 255); c[6] = new Color32(b, b, b, b);
        b = (byte)(6.0f / 16.0f * 255); c[7] = new Color32(b, b, b, b);

        b = (byte)(3.0f / 16.0f * 255); c[8] = new Color32(b, b, b, b);
        b = (byte)(11.0f / 16.0f * 255); c[9] = new Color32(b, b, b, b);
        b = (byte)(1.0f / 16.0f * 255); c[10] = new Color32(b, b, b, b);
        b = (byte)(9.0f / 16.0f * 255); c[11] = new Color32(b, b, b, b);

        b = (byte)(15.0f / 16.0f * 255); c[12] = new Color32(b, b, b, b);
        b = (byte)(7.0f / 16.0f * 255); c[13] = new Color32(b, b, b, b);
        b = (byte)(13.0f / 16.0f * 255); c[14] = new Color32(b, b, b, b);
        b = (byte)(5.0f / 16.0f * 255); c[15] = new Color32(b, b, b, b);
#else
            int i = 0;
            b = (byte)(1.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(49.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(13.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(61.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(4.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(52.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(16.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(64.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);

            b = (byte)(33.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(17.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(45.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(29.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(36.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(20.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(48.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(32.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);

            b = (byte)(9.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(57.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(5.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(53.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(12.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(60.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(8.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(56.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);

            b = (byte)(41.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(25.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(37.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(21.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(44.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(28.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(40.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(24.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);

            b = (byte)(3.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(51.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(15.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(63.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(2.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(50.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(14.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(62.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);

            b = (byte)(35.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(19.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(47.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(31.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(34.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(18.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(46.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(30.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);

            b = (byte)(11.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(59.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(7.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(55.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(10.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(58.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(6.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(54.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);

            b = (byte)(43.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(27.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(39.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(23.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(42.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(26.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(38.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
            b = (byte)(22.0f / 65.0f * 255);
            c[i++] = new Color32(b, b, b, b);
#endif

            ditheringTexture.SetPixels32(c);
            ditheringTexture.Apply();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private Mesh CreateSpotLightMesh()
        {
            // copy & pasted from other project, the geometry is too complex, should be simplified
            Mesh mesh = new Mesh();

            const int segmentCount = 16;
            Vector3[] vertices = new Vector3[2 + segmentCount * 3];
            Color32[] colors = new Color32[2 + segmentCount * 3];

            vertices[0] = new Vector3(0, 0, 0);
            vertices[1] = new Vector3(0, 0, 1);

            float angle = 0;
            float step = Mathf.PI * 2.0f / segmentCount;
            float ratio = 0.9f;

            for (int i = 0; i < segmentCount; ++i)
            {
                vertices[i + 2] = new Vector3(-Mathf.Cos(angle) * ratio, Mathf.Sin(angle) * ratio, ratio);
                colors[i + 2] = new Color32(255, 255, 255, 255);
                vertices[i + 2 + segmentCount] = new Vector3(-Mathf.Cos(angle), Mathf.Sin(angle), 1);
                colors[i + 2 + segmentCount] = new Color32(255, 255, 255, 0);
                vertices[i + 2 + segmentCount * 2] = new Vector3(-Mathf.Cos(angle) * ratio, Mathf.Sin(angle) * ratio, 1);
                colors[i + 2 + segmentCount * 2] = new Color32(255, 255, 255, 255);
                angle += step;
            }

            mesh.vertices = vertices;
            mesh.colors32 = colors;

            int[] indices = new int[segmentCount * 3 * 2 + segmentCount * 6 * 2];
            int index = 0;

            for (int i = 2; i < segmentCount + 1; ++i)
            {
                indices[index++] = 0;
                indices[index++] = i;
                indices[index++] = i + 1;
            }

            indices[index++] = 0;
            indices[index++] = segmentCount + 1;
            indices[index++] = 2;

            for (int i = 2; i < segmentCount + 1; ++i)
            {
                indices[index++] = i;
                indices[index++] = i + segmentCount;
                indices[index++] = i + 1;

                indices[index++] = i + 1;
                indices[index++] = i + segmentCount;
                indices[index++] = i + segmentCount + 1;
            }

            indices[index++] = 2;
            indices[index++] = 1 + segmentCount;
            indices[index++] = 2 + segmentCount;

            indices[index++] = 2 + segmentCount;
            indices[index++] = 1 + segmentCount;
            indices[index++] = 1 + segmentCount + segmentCount;

            //------------
            for (int i = 2 + segmentCount; i < segmentCount + 1 + segmentCount; ++i)
            {
                indices[index++] = i;
                indices[index++] = i + segmentCount;
                indices[index++] = i + 1;

                indices[index++] = i + 1;
                indices[index++] = i + segmentCount;
                indices[index++] = i + segmentCount + 1;
            }

            indices[index++] = 2 + segmentCount;
            indices[index++] = 1 + segmentCount * 2;
            indices[index++] = 2 + segmentCount * 2;

            indices[index++] = 2 + segmentCount * 2;
            indices[index++] = 1 + segmentCount * 2;
            indices[index++] = 1 + segmentCount * 3;

            ////-------------------------------------
            for (int i = 2 + segmentCount * 2; i < segmentCount * 3 + 1; ++i)
            {
                indices[index++] = 1;
                indices[index++] = i + 1;
                indices[index++] = i;
            }

            indices[index++] = 1;
            indices[index++] = 2 + segmentCount * 2;
            indices[index++] = segmentCount * 3 + 1;

            mesh.triangles = indices;
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}