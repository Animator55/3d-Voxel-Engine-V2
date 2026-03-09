#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0
    #define PS_SHADERMODEL ps_4_0
#endif

float4x4 World;
float4x4 View;
float4x4 Projection;
float Time;
float TimeOfDay;
float3 SunDirection;
float3 MoonDirection;

struct VSIn  { float4 Position : POSITION0; };
struct VSOut { float4 Position : SV_POSITION; float3 SkyDir : TEXCOORD0; };

VSOut MainVS(VSIn input)
{
    VSOut o;
    float4 wp  = mul(input.Position, World);
    float4 vp  = mul(wp, View);
    o.Position = mul(vp, Projection);
    o.Position.z = o.Position.w * 0.999;
    o.SkyDir = input.Position.xyz;
    return o;
}

float hash3(float3 p)
{
    p = frac(p * float3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
}

float StarLayer(float3 p, float offset)
{
    float3 ip = floor(p);
    float3 fp = frac(p);
    float  h  = hash3(ip + offset);
    if (h <= 0.96) return 0.0;
    float2 sp   = float2(hash3(ip + 0.1 + offset), hash3(ip + 0.2 + offset));
    float  dist = length(fp.xy - sp);
    float  tw   = sin(Time * 2.5 + h * 80.0) * 0.25 + 0.75;
    return (1.0 - smoothstep(0.0, 0.025, dist)) * tw * h;
}

float Stars(float3 dir, float nightFactor)
{
    float s = 0.0;
    s += StarLayer(dir * 120.0,            0.0);
    s += StarLayer(dir * 120.0 * 2.3,     13.7);
    s += StarLayer(dir * 120.0 * 5.29,    27.4);
    return s * nightFactor;
}

float4 MainPS(VSOut input) : COLOR
{
    float3 dir     = normalize(input.SkyDir);
    float3 sunDir  = normalize(SunDirection);
    float3 moonDir = normalize(MoonDirection);

    float sunH = sunDir.y;

    float dayFactor    = smoothstep(-0.12, 0.18, sunH);
    float nightFactor  = 1.0 - smoothstep(-0.15, 0.10, sunH);
    float sunsetFactor = smoothstep(-0.20, 0.00, sunH)
                       * smoothstep( 0.30, 0.05, sunH);

    float3 dayZenith     = float3(0.10, 0.28, 0.72);
    float3 dayHorizon    = float3(0.38, 0.60, 0.95);
    float3 sunsetZenith  = float3(0.30, 0.18, 0.40);
    float3 sunsetHorizon = float3(1.00, 0.42, 0.18);
    float3 nightZenith   = float3(0.008, 0.008, 0.04);
    float3 nightHorizon  = float3(0.015, 0.015, 0.06);

    float h    = saturate(dir.y * 0.5 + 0.5);
    float hPow = pow(h, 0.75);

    float3 zenith  = lerp(nightZenith,  dayZenith,  dayFactor);
    float3 horizon = lerp(nightHorizon, dayHorizon, dayFactor);
    zenith  = lerp(zenith,  sunsetZenith,  sunsetFactor * 0.9);
    horizon = lerp(horizon, sunsetHorizon, sunsetFactor);

    float3 sky = lerp(horizon, zenith, hPow);

    float horizonGlow = pow(1.0 - abs(dir.y), 4.0) * sunsetFactor * 0.5;
    sky += float3(1.0, 0.35, 0.08) * horizonGlow;

    float sunDot  = max(dot(dir, sunDir), 0.0);
    float sunVis  = smoothstep(-0.02, 0.02, sunH);
    float sunDisc = pow(sunDot, 800.0) * sunVis;
    float sunHalo = pow(sunDot,   6.0) * 0.35 * smoothstep(-0.15, 0.15, sunH);
    float sunAtmo = pow(sunDot,   2.5) * 0.15 * dayFactor;

    float3 sunColor     = lerp(float3(1.0, 0.55, 0.15), float3(1.0, 0.97, 0.85), dayFactor);
    float3 sunHaloColor = lerp(float3(1.0, 0.40, 0.10), float3(1.0, 0.80, 0.60), dayFactor);

    sky += sunColor     * sunDisc;
    sky += sunHaloColor * sunHalo;
    sky += sunHaloColor * sunAtmo;

    float moonDot  = max(dot(dir, moonDir), 0.0);
    float moonDisc = pow(moonDot, 600.0) * nightFactor;
    float moonGlow = pow(moonDot,   6.0) * 0.15 * nightFactor;
    sky += float3(0.88, 0.93, 1.0) * (moonDisc + moonGlow);

    sky += float3(0.95, 0.95, 1.0) * Stars(dir, nightFactor) * 0.9;

    return float4(sky, 1.0);
}

technique SkyTechnique
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
}
