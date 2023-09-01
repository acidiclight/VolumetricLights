using UnityEngine;

namespace VolumetricLights
{
	public static class VolumetricLightingShaderConstants
	{
		public static readonly int CameraForward = Shader.PropertyToID("_CameraForward");
		public static readonly int SampleCount = Shader.PropertyToID("_SampleCount");
		public static readonly int NoiseVelocity = Shader.PropertyToID("_NoiseVelocity");
		public static readonly int NoiseData = Shader.PropertyToID("_NoiseData");
		public static readonly int MieG = Shader.PropertyToID("_MieG");
		public static readonly int VolumetricLight = Shader.PropertyToID("_VolumetricLight");
		public static readonly int CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
		public static readonly int ZTest = Shader.PropertyToID("_ZTest");
		public static readonly int HeightFog = Shader.PropertyToID("_HeightFog");
		public static readonly int WorldViewProj = Shader.PropertyToID("_WorldViewProj");
		public static readonly int WorldView = Shader.PropertyToID("_WorldView");
		public static readonly int LightPos = Shader.PropertyToID("_LightPos");
		public static readonly int LightColor = Shader.PropertyToID("_LightColor");
		public static readonly int MyLightMatrix0 = Shader.PropertyToID("_MyLightMatrix0");
		public static readonly int LightTexture0 = Shader.PropertyToID("_LightTexture0");
		public static readonly int PlaneD = Shader.PropertyToID("_PlaneD");
		public static readonly int CosAngle = Shader.PropertyToID("_CosAngle");
		public static readonly int ConeApex = Shader.PropertyToID("_ConeApex");
		public static readonly int ConeAxis = Shader.PropertyToID("_ConeAxis");
		public static readonly int MyWorld2Shadow = Shader.PropertyToID("_MyWorld2Shadow");
		public static readonly int LightDir = Shader.PropertyToID("_LightDir");
		public static readonly int MaxRayLength = Shader.PropertyToID("_MaxRayLength");
		public static readonly int FrustumCorners = Shader.PropertyToID("_FrustumCorners");
	}
}