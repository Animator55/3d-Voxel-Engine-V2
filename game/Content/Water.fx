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
float WaterAlpha    = 1.0;
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
//  Vertex shader — geometría plana, sin waving
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
//  ComputeWaterNormal
//
//  Normal animada calculada íntegramente en PS.
//  perturbPlane : normal geométrica de la cara
//    - top  (geomN.y ~ 1) : ondas en plano XZ
//    - lado (geomN.y ~ 0) : ondas orientadas a lo largo de la cara
//  strength : escala global de perturbación
// ─────────────────────────────────────────────────────────────
float3 ComputeWaterNormal(float3 worldPos, float3 perturbPlane, float strength)
{
    float t = Time * WaveSpeed;

    // Capa 0: ondas primarias largas
    float2 uv0 = float2(worldPos.x * 0.08 + worldPos.z * 0.05,
                        worldPos.x * 0.04 - worldPos.z * 0.09);
    float dh0_dx = cos(uv0.x + t * 0.9)  * 0.55 * 0.08
                 + cos((uv0.x + uv0.y) * 0.6 + t * 0.7) * 0.15 * 0.6 * 0.08;
    float dh0_dz = cos(uv0.y + t * 1.3)  * 0.30 * 0.09
                 + cos((uv0.x + uv0.y) * 0.6 + t * 0.7) * 0.15 * 0.6 * 0.05;

    // Capa 1: ondas diagonales
    float2 uv1 = float2( worldPos.x * 0.18 - worldPos.z * 0.14,
                        -worldPos.x * 0.11 + worldPos.z * 0.20);
    float dh1_dx = cos(uv1.x + t * 1.6) * 0.35 * 0.18
                 + cos(uv1.y + t * 1.1) * 0.20 * (-0.11);
    float dh1_dz = cos(uv1.x + t * 1.6) * 0.35 * (-0.14)
                 + cos(uv1.y + t * 1.1) * 0.20 * 0.20;

    // Capa 2: micro-ripples de alta frecuencia
    float2 uv2 = float2(worldPos.x * 0.55 + worldPos.z * 0.40,
                        worldPos.x * 0.38 - worldPos.z * 0.55);
    float dh2_dx = cos(uv2.x + t * 2.4) * 0.12 * 0.55
                 + cos(uv2.y + t * 2.9) * 0.08 * 0.38;
    float dh2_dz = cos(uv2.x + t * 2.4) * 0.12 * 0.40
                 + cos(uv2.y + t * 2.9) * 0.08 * (-0.55);

    float scale = WaveHeight * strength * 4.0;
    float dHdx  = (dh0_dx + dh1_dx + dh2_dx) * scale;
    float dHdz  = (dh0_dz + dh1_dz + dh2_dz) * scale;

    // isLat: ~1 en caras laterales, ~0 en top
    float isLat = 1.0 - abs(perturbPlane.y);

    // Normal para top
    float3 topN = normalize(float3(-dHdx, 1.0, -dHdz));

    // Normal para laterales: reorienta el gradiente al plano de la cara
    float3 worldUp   = float3(0, 1, 0);
    float3 faceRight = normalize(cross(worldUp, perturbPlane + float3(0.001, 0, 0)));
    float3 faceUp2   = normalize(cross(perturbPlane, faceRight));
    float3 lateralN  = normalize(perturbPlane
                                 + faceRight * (-dHdx) * scale
                                 + faceUp2   * (-dHdz) * scale);

    return normalize(lerp(topN, lateralN, isLat));
}

// ─────────────────────────────────────────────────────────────
//  SampleEnvColor
//
//  Samplea el entorno completo usando el rayo de reflexión R.
//  No recibe parámetros de terreno — todo se infiere de R.y.
//
//  Lógica:
//    R.y > 0  → el rayo apunta al cielo   (cénit o horizonte)
//    R.y ~ 0  → rayo rasante              (horizonte)
//    R.y < 0  → el rayo apunta hacia abajo → "ve" terreno
//
//  Para R.y < 0 usamos el color de horizonte del cielo oscurecido
//  como proxy de terreno.  La transición R.y=0 es continua, así
//  que no hay línea visible entre cielo y "terreno".
//
//  La franja oscura en el horizonte aparece de forma natural porque
//  la normal perturbada hace que algunos píxeles tengan R.y ligeramente
//  negativo aunque la cámara esté casi cenital — igual que en el agua real.
// ─────────────────────────────────────────────────────────────
float3 SampleEnvColor(float3 R)
{
    // Mitad superior: cielo normal
    float  skyT   = saturate(R.y * 1.4 + 0.1);
    float3 skyCol = lerp(SkyColorHorizon, SkyColorZenith, skyT);

    // Mitad inferior: el rayo apunta hacia abajo.
    // "Terreno" = horizonte oscurecido.  El factor darkness
    // aumenta con la profundidad del ángulo (más |R.y| = más oscuro).
    float  belowT    = saturate(-R.y * 2.5);           // 0 en horizonte, 1 en -Y puro
    float3 terrProxy = SkyColorHorizon * lerp(0.55, 0.20, belowT);

    // Mezcla: R.y=0 usa skyCol, R.y<0 transiciona a terrProxy
    float  belowMask = saturate(-R.y * 8.0);           // transición corta alrededor del horizonte
    float3 envColor  = lerp(skyCol, terrProxy, belowMask);

    return envColor;
}

// ─────────────────────────────────────────────────────────────
//  Pixel shader
// ─────────────────────────────────────────────────────────────
float4 PS_Water(VSOut input) : SV_TARGET
{
    float3 geomN  = normalize(input.Normal);
    float  topMask  = saturate(geomN.y);
    float  sideMask = 1.0 - topMask;

    // Laterales usan perturbación más suave
    float perturbStrength = topMask + sideMask * 0.45;

    float3 N = ComputeWaterNormal(input.WorldPos, geomN, perturbStrength);

    float3 V = normalize(CameraPosition - input.WorldPos);
    float3 R = reflect(-V, N);

    // ── Environment color (cielo + terreno fake) ──────────────
    float3 envColor = SampleEnvColor(R);

    // ── Fresnel ───────────────────────────────────────────────
    // Caras laterales: bias más alto para garantizar reflection visible
    float fresnelBiasFace  = FresnelBias  + sideMask * 0.25;
    float fresnelScaleFace = FresnelScale - sideMask * 0.15;

    float NdotV  = saturate(dot(N, V));
    float fresnel = saturate(fresnelBiasFace + fresnelScaleFace * pow(1.0 - NdotV, FresnelPow));

    // Piso mínimo de reflection en cascadas
    fresnel = max(fresnel, sideMask * 0.20);

    // ── Especular ─────────────────────────────────────────────
    float dirEnabled = DirectionalLightEnabled ? 1.0 : 0.0;

    float3 H     = normalize(DirectionalLightDir + V);
    float  NdotH = saturate(dot(N, H));
    float  specWide  = pow(NdotH, SpecularPower * 0.4) * SpecularStr * 0.5;
    float  specSharp = pow(NdotH, SunSharpness) * SunStr;
    float3 specCol   = DirectionalLightColor * (specWide + specSharp) * dirEnabled;

    float RdotL = saturate(dot(R, DirectionalLightDir));
    specCol += SunColor * pow(RdotL, SunSharpness * 0.8) * SunStr * 0.6 * dirEnabled;

    // ── Difuso ────────────────────────────────────────────────
    float NdotL  = saturate(dot(N, DirectionalLightDir));
    float3 diffuse = AmbientLightColor + DirectionalLightColor * NdotL * 0.4 * dirEnabled;

    for (int i = 0; i < PointLightCount; i++)
    {
        float3 toLight = PointLightPositions[i] - input.WorldPos;
        float  dist    = length(toLight);
        float3 L       = toLight / max(dist, 0.001);
        float  atten   = saturate(1.0 - dist / PointLightRadii[i]);
        atten *= atten;
        float NdotLP   = saturate(dot(N, L));
        diffuse += PointLightColors[i] * NdotLP * atten * PointLightIntensities[i];
    }

    // ── Color base ────────────────────────────────────────────
    float3 baseCol = input.Color.rgb;
    baseCol = lerp(baseCol * float3(0.3, 0.5, 0.75), baseCol, topMask);

    float3 waterBody = baseCol * diffuse;

    // fresnel mezcla waterBody con envColor para top y laterales
    float3 litColor = lerp(waterBody, envColor, fresnel);
    litColor += specCol;

    litColor = lerp(litColor, FogColor, input.FogFactor);

    return float4(litColor, 1.0);
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
