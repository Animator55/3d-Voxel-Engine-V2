// VoxelLit.fx
// Custom shader that replaces BasicEffect for voxel chunks.
// Supports:
//   - Sun directional light (existing day/night cycle)
//   - Camera / player point light (torch-like, always on)
//   - Up to 8 emissive block point lights placed in the world
//   - Fog
//   - Vertex colour (baked AO already in vertex colour)

// ─────────────────────────────────────────────
//  Matrices
// ─────────────────────────────────────────────
float4x4 World;
float4x4 View;
float4x4 Projection;

// ─────────────────────────────────────────────
//  Sun / ambient  (mirrors BasicEffect fields)
// ─────────────────────────────────────────────
float3 AmbientLightColor    = float3(0.5, 0.5, 0.5);
float3 DirLight0Direction   = float3(0.57, -0.57, 0.57); // normalised, toward light
float3 DirLight0Diffuse     = float3(0.8, 0.8, 0.8);
bool   DirLight0Enabled     = true;

// ─────────────────────────────────────────────
//  Camera / player point light
// ─────────────────────────────────────────────
float3 CameraPosition       = float3(0, 0, 0);
bool   CameraLightEnabled   = true;
float  CameraLightRadius    = 18.0;    // world units
float  CameraLightIntensity = 1.4;
float3 CameraLightColor     = float3(1.0, 0.92, 0.75);   // warm torch

// ─────────────────────────────────────────────
//  Emissive block point lights  (world-space centres)
// ─────────────────────────────────────────────
#define MAX_POINT_LIGHTS 8

float3 PointLightPos[MAX_POINT_LIGHTS];
float3 PointLightColor[MAX_POINT_LIGHTS];
float  PointLightRadius[MAX_POINT_LIGHTS];
float  PointLightIntensity[MAX_POINT_LIGHTS];
int    PointLightCount = 0;       // how many are active

// ─────────────────────────────────────────────
//  Fog
// ─────────────────────────────────────────────
bool   FogEnabled  = true;
float  FogStart    = 200.0;
float  FogEnd      = 400.0;
float3 FogColor    = float3(0.62, 0.75, 0.95);

// ─────────────────────────────────────────────
//  Structs
// ─────────────────────────────────────────────
struct VSInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
};

struct VSOutput
{
    float4 Position    : POSITION0;
    float4 Color       : COLOR0;
    float3 WorldPos    : TEXCOORD0;   // world-space position for point-light calc
    float3 WorldNormal : TEXCOORD1;
    float  FogFactor   : TEXCOORD2;
};

// ─────────────────────────────────────────────
//  Helper: smooth attenuation
// ─────────────────────────────────────────────
float Attenuate(float dist, float radius)
{
    // Physically-inspired inverse-square with smooth cutoff
    float x = saturate(1.0 - (dist / radius));
    return x * x;
}

// ─────────────────────────────────────────────
//  Vertex Shader
// ─────────────────────────────────────────────
VSOutput VS(VSInput input)
{
    VSOutput output;

    float4 worldPos = mul(input.Position, World);
    float4 viewPos  = mul(worldPos, View);
    output.Position    = mul(viewPos, Projection);
    output.WorldPos    = worldPos.xyz;
    output.WorldNormal = normalize(mul(input.Normal, (float3x3)World));
    output.Color       = input.Color;   // vertex colour carries AO

    // Fog: linear in view-space depth
    float dist = length(viewPos.xyz);
    output.FogFactor = FogEnabled
        ? saturate((dist - FogStart) / (FogEnd - FogStart))
        : 0.0;

    return output;
}

// ─────────────────────────────────────────────
//  Pixel Shader
// ─────────────────────────────────────────────
float4 PS(VSOutput input) : COLOR0
{
    float3 N        = normalize(input.WorldNormal);
    float3 vertexRGB = input.Color.rgb;   // baked AO already multiplied in

    // ── 1. Ambient ────────────────────────────
    float3 lighting = AmbientLightColor;

    // ── 2. Sun directional light ──────────────
    if (DirLight0Enabled)
    {
        // DirLight0Direction points FROM surface TOWARD light (same convention as BasicEffect)
        float NdotL = saturate(dot(N, -DirLight0Direction));
        lighting += DirLight0Diffuse * NdotL;
    }

    // ── 3. Camera / player point light ────────
    if (CameraLightEnabled)
    {
        float3 toLight = CameraPosition - input.WorldPos;
        float  dist    = length(toLight);
        float3 L       = toLight / (dist + 0.0001);
        float  NdotL   = saturate(dot(N, L));
        float  att     = Attenuate(dist, CameraLightRadius);
        lighting += CameraLightColor * (CameraLightIntensity * att * NdotL);
    }

    // ── 4. Emissive block point lights ────────
    for (int i = 0; i < PointLightCount; i++)
    {
        float3 toLight = PointLightPos[i] - input.WorldPos;
        float  dist    = length(toLight);
        float3 L       = toLight / (dist + 0.0001);
        float  NdotL   = saturate(dot(N, L));
        float  att     = Attenuate(dist, PointLightRadius[i]);
        lighting += PointLightColor[i] * (PointLightIntensity[i] * att * NdotL);
    }

    float3 finalColor = vertexRGB * lighting;

    // ── 5. Fog ────────────────────────────────
    finalColor = lerp(finalColor, FogColor, input.FogFactor);

    return float4(finalColor, input.Color.a);
}

// ─────────────────────────────────────────────
//  Technique
// ─────────────────────────────────────────────
technique VoxelLit
{
    pass Pass0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
