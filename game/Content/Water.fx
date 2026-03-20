// ── Matrices ──────────────────────────────────────────────────
float4x4 World;
float4x4 View;
float4x4 Projection;

// ── Time ──────────────────────────────────────────────────────
float Time;

// ── Directional light ─────────────────────────────────────────
float3 DirectionalLightDir;
float3 DirectionalLightColor;
float3 AmbientLightColor;
bool   DirectionalLightEnabled;

// ── Fog ───────────────────────────────────────────────────────
bool   FogEnabled;
float  FogStart;
float  FogEnd;
float3 FogColor;

// ── Camera ────────────────────────────────────────────────────
float3 CameraPosition;

// ── Water tweaks ──────────────────────────────────────────────
float WaveHeight    = 0.08;
float WaveSpeed     = 0.6;
float WaterAlpha    = 0.82;   // alpha para agua transparente (nivel del mar)
float SpecularPower = 120.0;
float SpecularStr   = 2.2;
float FresnelBias   = 0.08;
float FresnelScale  = 0.92;
float FresnelPow    = 3.0;

// ── Sky reflection ────────────────────────────────────────────
float3 SkyColorZenith;
float3 SkyColorHorizon;
float3 SunColor;
float  SunSharpness;
float  SunStr;

// ── Camera / player point light ───────────────────────────────
bool   CameraLightEnabled   = true;
float  CameraLightRadius    = 18.0;
float  CameraLightIntensity = 1.4;
float3 CameraLightColor     = float3(1.0, 0.92, 0.75);

// ── Point lights ──────────────────────────────────────────────
#define MAX_POINT_LIGHTS 8
float3 PointLightPositions [MAX_POINT_LIGHTS];
float3 PointLightColors    [MAX_POINT_LIGHTS];
float  PointLightRadii     [MAX_POINT_LIGHTS];
float  PointLightIntensities[MAX_POINT_LIGHTS];
int    PointLightCount = 0;

// ─────────────────────────────────────────────────────────────
struct VSIn
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
};

struct VSOut
{
    float4 Position  : SV_POSITION;
    float3 WorldPos  : TEXCOORD0;
    float3 Normal    : TEXCOORD1;
    float4 Color     : COLOR0;
    float  FogFactor : TEXCOORD2;
};

// ─────────────────────────────────────────────────────────────
VSOut VS_Water(VSIn input)
{
    VSOut output;

    float4 worldPos  = mul(input.Position, World);
    float4 viewPos   = mul(worldPos, View);

    output.Position  = mul(viewPos, Projection);
    output.WorldPos  = worldPos.xyz;
    output.Normal    = normalize(mul(float4(input.Normal, 0.0), World).xyz);
    output.Color     = input.Color;

    float dist = length(viewPos.xyz);
    float fogEnabled = FogEnabled ? 1.0 : 0.0;
    output.FogFactor = saturate((dist - FogStart) / max(FogEnd - FogStart, 0.001)) * fogEnabled;

    return output;
}

// ─────────────────────────────────────────────────────────────
float3 ComputeWaterNormal(float3 worldPos, float3 perturbPlane, float strength)
{
    float t = Time * WaveSpeed;

    float2 uv0 = float2(worldPos.x * 0.08 + worldPos.z * 0.05,
                        worldPos.x * 0.04 - worldPos.z * 0.09);
    float dh0_dx = cos(uv0.x + t * 0.9)  * 0.55 * 0.08
                 + cos((uv0.x + uv0.y) * 0.6 + t * 0.7) * 0.15 * 0.6 * 0.08;
    float dh0_dz = cos(uv0.y + t * 1.3)  * 0.30 * 0.09
                 + cos((uv0.x + uv0.y) * 0.6 + t * 0.7) * 0.15 * 0.6 * 0.05;

    float2 uv1 = float2( worldPos.x * 0.18 - worldPos.z * 0.14,
                        -worldPos.x * 0.11 + worldPos.z * 0.20);
    float dh1_dx = cos(uv1.x + t * 1.6) * 0.35 * 0.18
                 + cos(uv1.y + t * 1.1) * 0.20 * (-0.11);
    float dh1_dz = cos(uv1.x + t * 1.6) * 0.35 * (-0.14)
                 + cos(uv1.y + t * 1.1) * 0.20 * 0.20;

    float2 uv2 = float2(worldPos.x * 0.55 + worldPos.z * 0.40,
                        worldPos.x * 0.38 - worldPos.z * 0.55);
    float dh2_dx = cos(uv2.x + t * 2.4) * 0.12 * 0.55
                 + cos(uv2.y + t * 2.9) * 0.08 * 0.38;
    float dh2_dz = cos(uv2.x + t * 2.4) * 0.12 * 0.40
                 + cos(uv2.y + t * 2.9) * 0.08 * (-0.55);

    float scale = WaveHeight * strength * 4.0;
    float dHdx  = (dh0_dx + dh1_dx + dh2_dx) * scale;
    float dHdz  = (dh0_dz + dh1_dz + dh2_dz) * scale;

    float isLat = 1.0 - abs(perturbPlane.y);
    float3 topN = normalize(float3(-dHdx, 1.0, -dHdz));

    float3 worldUp   = float3(0, 1, 0);
    float3 faceRight = normalize(cross(worldUp, perturbPlane + float3(0.001, 0, 0)));
    float3 faceUp2   = normalize(cross(perturbPlane, faceRight));
    float3 lateralN  = normalize(perturbPlane
                                 + faceRight * (-dHdx) * scale
                                 + faceUp2   * (-dHdz) * scale);

    return normalize(lerp(topN, lateralN, isLat));
}

// ─────────────────────────────────────────────────────────────
float3 SampleEnvColor(float3 R)
{
    float  skyT   = saturate(R.y * 1.4 + 0.1);
    float3 skyCol = lerp(SkyColorHorizon, SkyColorZenith, skyT);

    float  belowT    = saturate(-R.y * 2.5);
    float3 terrProxy = SkyColorHorizon * lerp(0.55, 0.20, belowT);

    float  belowMask = saturate(-R.y * 8.0);
    return lerp(skyCol, terrProxy, belowMask);
}

// ─────────────────────────────────────────────────────────────
float4 PS_Water(VSOut input) : SV_TARGET
{
    float3 geomN    = normalize(input.Normal);
    float  topMask  = saturate(geomN.y);
    float  sideMask = 1.0 - topMask;

    float perturbStrength = topMask + sideMask * 0.45;
    float3 N = ComputeWaterNormal(input.WorldPos, geomN, perturbStrength);

    float3 V = normalize(CameraPosition - input.WorldPos);
    float3 R = reflect(-V, N);

    float3 envColor = SampleEnvColor(R);

    // ── Fresnel ───────────────────────────────────────────────
    float fresnelBiasFace  = FresnelBias  + sideMask * 0.25;
    float fresnelScaleFace = FresnelScale - sideMask * 0.15;
    float NdotV  = saturate(dot(N, V));
    float fresnel = saturate(fresnelBiasFace + fresnelScaleFace * pow(1.0 - NdotV, FresnelPow));
    fresnel = max(fresnel, sideMask * 0.20);

    // ── Especular solar ───────────────────────────────────────
    float dirEnabled = DirectionalLightEnabled ? 1.0 : 0.0;
    float3 H      = normalize(DirectionalLightDir + V);
    float  NdotH  = saturate(dot(N, H));
    float  specWide  = pow(NdotH, SpecularPower * 0.4) * SpecularStr * 0.5;
    float  specSharp = pow(NdotH, SunSharpness) * SunStr;
    float3 specCol   = DirectionalLightColor * (specWide + specSharp) * dirEnabled;

    float RdotSun = saturate(dot(R, DirectionalLightDir));
    specCol += SunColor * pow(RdotSun, SunSharpness * 0.8) * SunStr * 0.6 * dirEnabled;

    // ── Difuso ────────────────────────────────────────────────
    float  NdotL  = saturate(dot(N, DirectionalLightDir));
    float3 diffuse = AmbientLightColor + DirectionalLightColor * NdotL * 0.4 * dirEnabled;

    for (int i = 0; i < PointLightCount; i++)
    {
        float3 plToL  = PointLightPositions[i] - input.WorldPos;
        float  plDist = length(plToL);
        float3 plL    = plToL / max(plDist, 0.001);
        float  plAtt  = saturate(1.0 - plDist / PointLightRadii[i]);
        plAtt *= plAtt;
        diffuse += PointLightColors[i] * saturate(dot(N, plL)) * plAtt * PointLightIntensities[i];
    }

    // ── Camera light ──────────────────────────────────────────
    {
        float  camEnabled = CameraLightEnabled ? 1.0 : 0.0;
        float3 camToL   = CameraPosition - input.WorldPos;
        float  camDist  = length(camToL);
        float3 camL     = camToL / max(camDist, 0.001);
        float  camX     = saturate(1.0 - camDist / CameraLightRadius);
        float  camAtt   = camX * camX;
        float  camLit   = camAtt * CameraLightIntensity * camEnabled;

        diffuse += CameraLightColor * saturate(dot(N, camL)) * camAtt * CameraLightIntensity * camEnabled;

        float3 camHc    = normalize(camL + V);
        float  camNdotH = saturate(dot(N, camHc));
        specCol += CameraLightColor * pow(camNdotH, SpecularPower * 0.25) * SpecularStr * 0.4 * camLit;

        float camRdotL = saturate(dot(R, camL));
        envColor += CameraLightColor * pow(camRdotL, 32.0) * camLit * 0.25;
    }

    // ── Color final ───────────────────────────────────────────
    float3 baseCol   = input.Color.rgb;
    baseCol          = lerp(baseCol * float3(0.3, 0.5, 0.75), baseCol, topMask);

    float3 waterBody = baseCol * diffuse;
    float3 litColor  = lerp(waterBody, envColor, fresnel);
    litColor        += specCol;
    litColor         = lerp(litColor, FogColor, input.FogFactor);

    // ── Alpha ─────────────────────────────────────────────────
    // El mesher pone alpha=255 en vértices de agua por encima del nivel del mar
    // (ríos, cascadas) y alpha=0 en agua del océano.
    // Usamos ese valor para elegir entre opaco y semitransparente.
    float vertexAlpha = input.Color.a;   // 0..1 tras normalización de COLOR0

    // vertexAlpha ~ 1  →  agua de río/cascada: completamente opaca
    // vertexAlpha ~ 0  →  océano: semitransparente con fresnel
    float specLum    = dot(specCol, float3(0.299, 0.587, 0.114));
    float alphaBoost = saturate(specLum * 2.0);
    float transAlpha = saturate(WaterAlpha + fresnel * (1.0 - WaterAlpha) + alphaBoost);

    float alpha = lerp(transAlpha, 1.0, vertexAlpha);

    return float4(litColor, alpha);
}

// ─────────────────────────────────────────────────────────────
technique Water
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS_Water();
        PixelShader  = compile ps_3_0 PS_Water();
    }
}
