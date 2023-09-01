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
using UnityEngine.Serialization;

namespace VolumetricLights
{
    [RequireComponent(typeof(Light))]
    public class VolumetricLight : MonoBehaviour
    {
        public event Action<VolumetricLightRenderer, VolumetricLight, CommandBuffer, Matrix4x4> CustomRenderEvent;

        private Light light;
        private Material material;
        private CommandBuffer commandBuffer;
        private CommandBuffer cascadeShadowCommandBuffer;

        [FormerlySerializedAs("SampleCount")]
        [Range(1, 64)]
        public int sampleCount = 8;

        [FormerlySerializedAs("ScatteringCoef")]
        [Range(0.0f, 1.0f)]
        public float scatteringCoef = 0.5f;

        [FormerlySerializedAs("ExtinctionCoef")]
        [Range(0.0f, 0.1f)]
        public float extinctionCoef = 0.01f;

        [FormerlySerializedAs("SkyboxExtinctionCoef")]
        [Range(0.0f, 1.0f)]
        public float skyboxExtinctionCoef = 0.9f;

        [FormerlySerializedAs("MieG")]
        [Range(0.0f, 0.999f)]
        public float mieG = 0.1f;

        [FormerlySerializedAs("HeightFog")]
        public bool heightFog = false;

        [FormerlySerializedAs("HeightScale")]
        [Range(0, 0.5f)]
        public float heightScale = 0.10f;

        [FormerlySerializedAs("GroundLevel")]
        public float groundLevel = 0;
        [FormerlySerializedAs("Noise")]
        public bool noise = false;
        [FormerlySerializedAs("NoiseScale")]
        public float noiseScale = 0.015f;
        [FormerlySerializedAs("NoiseIntensity")]
        public float noiseIntensity = 1.0f;
        [FormerlySerializedAs("NoiseIntensityOffset")]
        public float noiseIntensityOffset = 0.3f;
        [FormerlySerializedAs("NoiseVelocity")]
        public Vector2 noiseVelocity = new Vector2(3.0f, 3.0f);

        [FormerlySerializedAs("MaxRayLength")]
        [Tooltip("")]
        public float maxRayLength = 400.0f;

        public Light Light => light;

        public Material VolumetricMaterial => material;

        private bool reversedZ = false;

        private readonly Vector4[] frustumCorners = new Vector4[4];
        
        /// <summary>
        /// 
        /// </summary>
        private void Start()
        {
#if UNITY_5_5_OR_NEWER
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal || SystemInfo.graphicsDeviceType == GraphicsDeviceType.PlayStation4 ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan || SystemInfo.graphicsDeviceType == GraphicsDeviceType.XboxOne)
            {
                reversedZ = true;
            }
#endif

            commandBuffer = new CommandBuffer();
            commandBuffer.name = "Light Command Buffer";

            cascadeShadowCommandBuffer = new CommandBuffer();
            cascadeShadowCommandBuffer.name = "Dir Light Command Buffer";
            cascadeShadowCommandBuffer.SetGlobalTexture("_CascadeShadowMapTexture", new UnityEngine.Rendering.RenderTargetIdentifier(UnityEngine.Rendering.BuiltinRenderTextureType.CurrentActive));

            light = GetComponent<Light>();
            //_light.RemoveAllCommandBuffers();
            if (light.type == LightType.Directional)
            {
                light.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, commandBuffer);
                light.AddCommandBuffer(LightEvent.AfterShadowMap, cascadeShadowCommandBuffer);

            }
            else
                light.AddCommandBuffer(LightEvent.AfterShadowMap, commandBuffer);

            Shader shader = Shader.Find("Sandbox/VolumetricLight");
            if (shader == null)
                throw new Exception("Critical Error: \"Sandbox/VolumetricLight\" shader is missing. Make sure it is included in \"Always Included Shaders\" in ProjectSettings/Graphics.");
            material = new Material(shader); // new Material(VolumetricLightRenderer.GetLightMaterial());
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnEnable()
        {
            VolumetricLightRenderer.PreRenderEvent += VolumetricLightRenderer_PreRenderEvent;
        }

        /// <summary>
        /// 
        /// </summary>
        private void OnDisable()
        {
            VolumetricLightRenderer.PreRenderEvent -= VolumetricLightRenderer_PreRenderEvent;
        }

        /// <summary>
        /// 
        /// </summary>
        public void OnDestroy()
        {
            Destroy(material);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="renderer"></param>
        /// <param name="viewProj"></param>
        private void VolumetricLightRenderer_PreRenderEvent(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
        {
            // light was destroyed without deregistring, deregister now
            if (light == null || light.gameObject == null)
            {
                VolumetricLightRenderer.PreRenderEvent -= VolumetricLightRenderer_PreRenderEvent;
            }

            if (!light.gameObject.activeInHierarchy || light.enabled == false)
                return;

            material.SetVector(VolumetricLightingShaderConstants.CameraForward, Camera.current.transform.forward);

            material.SetInt(VolumetricLightingShaderConstants.SampleCount, sampleCount);
            material.SetVector(VolumetricLightingShaderConstants.NoiseVelocity, new Vector4(noiseVelocity.x, noiseVelocity.y) * noiseScale);
            material.SetVector(VolumetricLightingShaderConstants.NoiseData, new Vector4(noiseScale, noiseIntensity, noiseIntensityOffset));
            material.SetVector(VolumetricLightingShaderConstants.MieG, new Vector4(1 - (mieG * mieG), 1 + (mieG * mieG), 2 * mieG, 1.0f / (4.0f * Mathf.PI)));
            material.SetVector(VolumetricLightingShaderConstants.VolumetricLight, new Vector4(scatteringCoef, extinctionCoef, light.range, 1.0f - skyboxExtinctionCoef));

            material.SetTexture(VolumetricLightingShaderConstants.CameraDepthTexture, renderer.GetVolumeLightDepthBuffer());

            //if (renderer.Resolution == VolumetricLightRenderer.VolumtericResolution.Full)
            {
                //_material.SetFloat("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                //_material.DisableKeyword("MANUAL_ZTEST");            
            }
            //else
            {
                material.SetFloat(VolumetricLightingShaderConstants.ZTest, (int)UnityEngine.Rendering.CompareFunction.Always);
                // downsampled light buffer can't use native zbuffer for ztest, try to perform ztest in pixel shader to avoid ray marching for occulded geometry 
                //_material.EnableKeyword("MANUAL_ZTEST");
            }

            if (heightFog)
            {
                material.EnableKeyword("HEIGHT_FOG");

                material.SetVector(VolumetricLightingShaderConstants.HeightFog, new Vector4(groundLevel, heightScale));
            }
            else
            {
                material.DisableKeyword("HEIGHT_FOG");
            }

            if (light.type == LightType.Point)
            {
                SetupPointLight(renderer, viewProj);
            }
            else if (light.type == LightType.Spot)
            {
                SetupSpotLight(renderer, viewProj);
            }
            else if (light.type == LightType.Directional)
            {
                SetupDirectionalLight(renderer, viewProj);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="renderer"></param>
        /// <param name="viewProj"></param>
        private void SetupPointLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
        {
            commandBuffer.Clear();

            var pass = 0;
            if (!IsCameraInPointLightBounds())
                pass = 2;

            material.SetPass(pass);

            Mesh mesh = VolumetricLightRenderer.GetPointLightMesh();

            float scale = light.range * 2.0f;
            Matrix4x4 world = Matrix4x4.TRS(transform.position, light.transform.rotation, new Vector3(scale, scale, scale));

            material.SetMatrix(VolumetricLightingShaderConstants.WorldViewProj, viewProj * world);
            material.SetMatrix(VolumetricLightingShaderConstants.WorldView, Camera.current.worldToCameraMatrix * world);

            if (noise)
                material.EnableKeyword("NOISE");
            else
                material.DisableKeyword("NOISE");

            material.SetVector(VolumetricLightingShaderConstants.LightPos, new Vector4(light.transform.position.x, light.transform.position.y, light.transform.position.z, 1.0f / (light.range * light.range)));
            material.SetColor(VolumetricLightingShaderConstants.LightColor, light.color * light.intensity);

            if (light.cookie == null)
            {
                material.EnableKeyword("POINT");
                material.DisableKeyword("POINT_COOKIE");
            }
            else
            {
                Matrix4x4 view = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one).inverse;
                material.SetMatrix(VolumetricLightingShaderConstants.MyLightMatrix0, view);

                material.EnableKeyword("POINT_COOKIE");
                material.DisableKeyword("POINT");

                material.SetTexture(VolumetricLightingShaderConstants.LightTexture0, light.cookie);
            }

            bool forceShadowsOff = (light.transform.position - Camera.current.transform.position).magnitude >= QualitySettings.shadowDistance;

            if (light.shadows != LightShadows.None && forceShadowsOff == false)
            {
                material.EnableKeyword("SHADOWS_CUBE");
                commandBuffer.SetGlobalTexture("_ShadowMapTexture", BuiltinRenderTextureType.CurrentActive);
                commandBuffer.SetRenderTarget(renderer.GetVolumeLightBuffer());

                commandBuffer.DrawMesh(mesh, world, material, 0, pass);

                if (CustomRenderEvent != null)
                    CustomRenderEvent(renderer, this, commandBuffer, viewProj);
            }
            else
            {
                material.DisableKeyword("SHADOWS_CUBE");
                renderer.GlobalCommandBuffer.DrawMesh(mesh, world, material, 0, pass);

                if (CustomRenderEvent != null)
                    CustomRenderEvent(renderer, this, renderer.GlobalCommandBuffer, viewProj);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="renderer"></param>
        /// <param name="viewProj"></param>
        private void SetupSpotLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
        {
            commandBuffer.Clear();

            var pass = 1;
            if (!IsCameraInSpotLightBounds())
            {
                pass = 3;
            }

            Mesh mesh = VolumetricLightRenderer.GetSpotLightMesh();

            float scale = light.range;
            float angleScale = Mathf.Tan((light.spotAngle + 1) * 0.5f * Mathf.Deg2Rad) * light.range;

            Matrix4x4 world = Matrix4x4.TRS(transform.position, transform.rotation, new Vector3(angleScale, angleScale, scale));

            Matrix4x4 view = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one).inverse;

            Matrix4x4 clip = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.0f), Quaternion.identity, new Vector3(-0.5f, -0.5f, 1.0f));
            Matrix4x4 proj = Matrix4x4.Perspective(light.spotAngle, 1, 0, 1);

            material.SetMatrix(VolumetricLightingShaderConstants.MyLightMatrix0, clip * proj * view);

            material.SetMatrix(VolumetricLightingShaderConstants.WorldViewProj, viewProj * world);

            material.SetVector(VolumetricLightingShaderConstants.LightPos, new Vector4(light.transform.position.x, light.transform.position.y, light.transform.position.z, 1.0f / (light.range * light.range)));
            material.SetVector(VolumetricLightingShaderConstants.LightColor, light.color * light.intensity);


            Vector3 apex = transform.position;
            Vector3 axis = transform.forward;
            // plane equation ax + by + cz + d = 0; precompute d here to lighten the shader
            Vector3 center = apex + axis * light.range;
            float d = -Vector3.Dot(center, axis);

            // update material
            material.SetFloat(VolumetricLightingShaderConstants.PlaneD, d);
            material.SetFloat(VolumetricLightingShaderConstants.CosAngle, Mathf.Cos((light.spotAngle + 1) * 0.5f * Mathf.Deg2Rad));

            material.SetVector(VolumetricLightingShaderConstants.ConeApex, new Vector4(apex.x, apex.y, apex.z));
            material.SetVector(VolumetricLightingShaderConstants.ConeAxis, new Vector4(axis.x, axis.y, axis.z));

            material.EnableKeyword("SPOT");

            if (noise)
                material.EnableKeyword("NOISE");
            else
                material.DisableKeyword("NOISE");

            if (light.cookie == null)
            {
                material.SetTexture(VolumetricLightingShaderConstants.LightTexture0, VolumetricLightRenderer.Current.GetDefaultSpotCookie());
            }
            else
            {
                material.SetTexture(VolumetricLightingShaderConstants.LightTexture0, light.cookie);
            }

            bool forceShadowsOff = (light.transform.position - Camera.current.transform.position).magnitude >= QualitySettings.shadowDistance;

            if (light.shadows != LightShadows.None && forceShadowsOff == false)
            {
                clip = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));

                proj = reversedZ ? Matrix4x4.Perspective(light.spotAngle, 1, light.range, light.shadowNearPlane) : Matrix4x4.Perspective(light.spotAngle, 1, light.shadowNearPlane, light.range);

                Matrix4x4 m = clip * proj;
                m[0, 2] *= -1;
                m[1, 2] *= -1;
                m[2, 2] *= -1;
                m[3, 2] *= -1;

                //view = _light.transform.worldToLocalMatrix;
                material.SetMatrix(VolumetricLightingShaderConstants.MyWorld2Shadow, m * view);
                material.SetMatrix(VolumetricLightingShaderConstants.WorldView, m * view);

                material.EnableKeyword("SHADOWS_DEPTH");
                commandBuffer.SetGlobalTexture("_ShadowMapTexture", BuiltinRenderTextureType.CurrentActive);
                commandBuffer.SetRenderTarget(renderer.GetVolumeLightBuffer());

                commandBuffer.DrawMesh(mesh, world, material, 0, pass);

                if (CustomRenderEvent != null)
                    CustomRenderEvent(renderer, this, commandBuffer, viewProj);
            }
            else
            {
                material.DisableKeyword("SHADOWS_DEPTH");
                renderer.GlobalCommandBuffer.DrawMesh(mesh, world, material, 0, pass);

                if (CustomRenderEvent != null)
                    CustomRenderEvent(renderer, this, renderer.GlobalCommandBuffer, viewProj);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="renderer"></param>
        /// <param name="viewProj"></param>
        private void SetupDirectionalLight(VolumetricLightRenderer renderer, Matrix4x4 viewProj)
        {
            commandBuffer.Clear();

            var pass = 4;

            material.SetPass(pass);

            if (noise)
                material.EnableKeyword("NOISE");
            else
                material.DisableKeyword("NOISE");

            material.SetVector(VolumetricLightingShaderConstants.LightDir, new Vector4(light.transform.forward.x, light.transform.forward.y, light.transform.forward.z, 1.0f / (light.range * light.range)));
            material.SetVector(VolumetricLightingShaderConstants.LightColor, light.color * light.intensity);
            material.SetFloat(VolumetricLightingShaderConstants.MaxRayLength, maxRayLength);

            if (light.cookie == null)
            {
                material.EnableKeyword("DIRECTIONAL");
                material.DisableKeyword("DIRECTIONAL_COOKIE");
            }
            else
            {
                material.EnableKeyword("DIRECTIONAL_COOKIE");
                material.DisableKeyword("DIRECTIONAL");

                material.SetTexture(VolumetricLightingShaderConstants.LightTexture0, light.cookie);
            }

            // setup frustum corners for world position reconstruction
            // bottom left
            frustumCorners[0] = Camera.current.ViewportToWorldPoint(new Vector3(0, 0, Camera.current.farClipPlane));
            // top left
            frustumCorners[2] = Camera.current.ViewportToWorldPoint(new Vector3(0, 1, Camera.current.farClipPlane));
            // top right
            frustumCorners[3] = Camera.current.ViewportToWorldPoint(new Vector3(1, 1, Camera.current.farClipPlane));
            // bottom right
            frustumCorners[1] = Camera.current.ViewportToWorldPoint(new Vector3(1, 0, Camera.current.farClipPlane));

            material.SetVectorArray(VolumetricLightingShaderConstants.FrustumCorners, frustumCorners);

            Texture nullTexture = null;
            if (light.shadows != LightShadows.None)
            {
                material.EnableKeyword("SHADOWS_DEPTH");
                commandBuffer.Blit(nullTexture, renderer.GetVolumeLightBuffer(), material, pass);

                if (CustomRenderEvent != null)
                    CustomRenderEvent(renderer, this, commandBuffer, viewProj);
            }
            else
            {
                material.DisableKeyword("SHADOWS_DEPTH");
                renderer.GlobalCommandBuffer.Blit(nullTexture, renderer.GetVolumeLightBuffer(), material, pass);

                if (CustomRenderEvent != null)
                    CustomRenderEvent(renderer, this, renderer.GlobalCommandBuffer, viewProj);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool IsCameraInPointLightBounds()
        {
            float distanceSqr = (light.transform.position - Camera.current.transform.position).sqrMagnitude;
            float extendedRange = light.range + 1;
            return distanceSqr < (extendedRange * extendedRange);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private bool IsCameraInSpotLightBounds()
        {
            // check range
            float distance = Vector3.Dot(light.transform.forward, (Camera.current.transform.position - light.transform.position));
            float extendedRange = light.range + 1;
            if (distance > (extendedRange)) return false;
            float cosAngle = Vector3.Dot(transform.forward, (Camera.current.transform.position - light.transform.position).normalized);
            return !((Mathf.Acos(cosAngle) * Mathf.Rad2Deg) > (light.spotAngle + 3) * 0.5f);
        }
    }
}