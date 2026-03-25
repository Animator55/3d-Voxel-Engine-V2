// VoxelLit.fx

// ─────────────────────────────────────────────
//  Matrices
// ─────────────────────────────────────────────
float4x4 World;
float4x4 View;
float4x4 Projection;

// ─────────────────────────────────────────────
//  Sun / ambient
// ─────────────────────────────────────────────
float3 AmbientLightColor    = float3(0.5, 0.5, 0.5);
float3 DirLight0Direction   = float3(0.57, -0.57, 0.57);
float3 DirLight0Diffuse     = float3(0.8, 0.8, 0.8);
bool   DirLight0Enabled     = true;

// ─────────────────────────────────────────────
//  Camera / player point light
// ─────────────────────────────────────────────
float3 CameraPosition       = float3(0, 0, 0);
bool   CameraLightEnabled   = true;
float  CameraLightRadius    = 18.0;
float  CameraLightIntensity = 1.4;
float3 CameraLightColor     = float3(1.0, 0.92, 0.75);

// ─────────────────────────────────────────────
//  Emissive block point lights
// ─────────────────────────────────────────────
#define MAX_POINT_LIGHTS 8

float3 PointLightPos      [MAX_POINT_LIGHTS];
float3 PointLightColor    [MAX_POINT_LIGHTS];
float  PointLightRadius   [MAX_POINT_LIGHTS];
float  PointLightIntensity[MAX_POINT_LIGHTS];
int    PointLightCount = 0;

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
    float4 Color       : COLOR0;    // rgb = AO-tinted base color, a = skyLight (0..1)
    float3 WorldPos    : TEXCOORD0;
    float3 WorldNormal : TEXCOORD1;
    float  FogFactor   : TEXCOORD2;
};

// ─────────────────────────────────────────────
//  Helper
// ─────────────────────────────────────────────
float Attenuate(float dist, float radius)
{
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

    // Pasar color completo incluyendo alpha (skyLight) sin modificar
    output.Color = input.Color;

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
    float3 vertexRGB = input.Color.rgb;   // AO ya multiplicado en RGB

    // skyLight: 1.0 = expuesto al cielo, ~0.18 = interior de cueva
    // Solo afecta ambient + directional; las luces dinámicas lo ignoran.
    float skyLight = input.Color.a;

    // ── 1. Ambient (atenuado por skyLight) ────
    float3 skyLighting = AmbientLightColor * skyLight;

    // ── 2. Sun directional (atenuado por skyLight) ──
    if (DirLight0Enabled)
    {
        float NdotL = saturate(dot(N, -DirLight0Direction));
        skyLighting += DirLight0Diffuse * NdotL * skyLight;
    }

    // skyLighting ya está completo; ahora sumamos luces dinámicas SIN skyLight

    // ── 3. Camera / player point light ────────
    float3 dynamicLighting = float3(0, 0, 0);
    if (CameraLightEnabled)
    {
        float3 toLight = CameraPosition - input.WorldPos;
        float  dist    = length(toLight);
        float3 L       = toLight / (dist + 0.0001);
        float  NdotL   = saturate(dot(N, L));
        float  att     = Attenuate(dist, CameraLightRadius);
        dynamicLighting += CameraLightColor * (CameraLightIntensity * att * NdotL);
    }

    // ── 4. Emissive block point lights ────────
    for (int i = 0; i < PointLightCount; i++)
    {
        float3 toLight = PointLightPos[i] - input.WorldPos;
        float  dist    = length(toLight);
        float3 L       = toLight / (dist + 0.0001);
        float  NdotL   = saturate(dot(N, L));
        float  att     = Attenuate(dist, PointLightRadius[i]);
        dynamicLighting += PointLightColor[i] * (PointLightIntensity[i] * att * NdotL);
    }

    // ── Color final: base * (skyLighting + dynamicLighting) ──
    float3 finalColor = vertexRGB * (skyLighting + dynamicLighting);

    // ── 5. Fog ────────────────────────────────
    finalColor = lerp(finalColor, FogColor, input.FogFactor);

    // Alpha = 1 para opacos (el 'a' del vértice fue reutilizado para skyLight)
    return float4(finalColor, 1.0);
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