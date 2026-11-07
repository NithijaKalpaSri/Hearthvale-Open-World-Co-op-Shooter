// Hearthvale sky (port of sky.js): horizon/zenith gradient driven by the day/night cycle,
// sun disc + glow, moon, 3000-ish stars, drifting clouds and an overcast layer for storms.
// One cheap full-sky pass: no textures, no extra meshes (good for integrated graphics).
Shader "Hearthvale/Sky"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.2,0.4,0.7,1)
        _HorizonColor ("Horizon", Color) = (0.8,0.8,0.75,1)
        _SunDir ("Sun direction", Vector) = (0,1,0,0)
        _SunColor ("Sun colour", Color) = (1,0.95,0.85,1)
        _Night ("Night factor", Range(0,1)) = 0
        _CloudColor ("Cloud colour", Color) = (1,1,1,1)
        _CloudCover ("Cloud cover", Range(0,1)) = 0.45
        _Overcast ("Overcast (storm)", Range(0,1)) = 0
        _OvercastColor ("Overcast colour", Color) = (0.6,0.62,0.66,1)
        _CloudTime ("Cloud time", Float) = 0
        _CloudOctaves ("Cloud octaves", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor, _HorizonColor, _SunColor, _CloudColor, _OvercastColor;
                float4 _SunDir;
                half _Night, _CloudCover, _Overcast, _CloudTime, _CloudOctaves;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i) { V o; o.cs = TransformObjectToHClip(i.pos.xyz); o.dir = i.pos.xyz; return o; }

            float hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                float a = hash2(i), b = hash2(i + float2(1, 0)), c = hash2(i + float2(0, 1)), d = hash2(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float fbm(float2 p, int oct)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 4; k++) { if (k >= oct) break; s += vnoise(p) * a; p = p * 2.03 + 11.7; a *= 0.5; }
                return s;
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 sunDir = normalize(_SunDir.xyz);

                // gradient: zenith -> horizon, dusty ground colour below the horizon
                float t = pow(saturate(h), 0.5);
                float3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, t);
                col = lerp(col, _HorizonColor.rgb * 0.7, saturate(-h * 6.0));
                // brighter band toward the sun (scattering)
                float cs = dot(d, sunDir);
                col += _SunColor.rgb * pow(saturate(cs), 6.0) * 0.18 * (1 - _Night);

                // sun disc + glow
                float disc = smoothstep(0.99955, 0.99985, cs);
                col += _SunColor.rgb * (disc * 14.0 + pow(saturate(cs), 220.0) * 1.2 + pow(saturate(cs), 40.0) * 0.25) * (1 - _Night) * (1 - _Overcast * 0.85);

                // moon (opposite the sun) with a soft halo, and stars
                float3 moonDir = -sunDir;
                float cm = dot(d, moonDir);
                float moon = smoothstep(0.99965, 0.99982, cm);
                col += float3(0.93, 0.95, 1.0) * (moon * 1.5 + pow(saturate(cm), 120.0) * 0.12 + pow(saturate(cm), 16.0) * 0.02) * _Night;

                if (_Night > 0.02 && h > 0.0)
                {
                    float3 p = d * 150.0;
                    float3 cell = floor(p);
                    float3 f = frac(p) - 0.5;
                    float r = hash3(cell);
                    float star = step(0.972, r) * smoothstep(0.2, 0.0, length(f + (hash3(cell + 7.3) - 0.5) * 0.4));
                    float tw = 0.75 + 0.25 * sin(_CloudTime * 3.0 + r * 60.0);
                    col += star * tw * 1.6 * _Night * (1 - _Overcast) * smoothstep(0.0, 0.12, h);
                }

                // clouds: planar projection that drifts with the wind
                if (h > 0.01)
                {
                    float2 uv = d.xz / (h + 0.18) * 0.55 + float2(_CloudTime * 0.012, _CloudTime * 0.004);
                    float n = fbm(uv * 1.6, (int)_CloudOctaves);
                    float cover = saturate(_CloudCover + _Overcast * 0.6);
                    float c = smoothstep(1.0 - cover, 1.0 - cover + 0.22, n);
                    c *= smoothstep(0.01, 0.22, h);
                    float shade = saturate(0.78 + (n - 0.5) * 0.9);
                    float3 cc = lerp(_CloudColor.rgb, _OvercastColor.rgb, _Overcast) * shade;
                    col = lerp(col, cc, c * 0.92);
                }
                // storm: whole sky greys out
                col = lerp(col, _OvercastColor.rgb, _Overcast * 0.55 * saturate(0.4 + h));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
