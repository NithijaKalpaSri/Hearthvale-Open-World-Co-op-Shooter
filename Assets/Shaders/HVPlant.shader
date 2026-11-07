// Hearthvale plants: trees, bushes, flowers, grass, rocks.
//  - GPU instancing with a per-instance colour (_InstColor) so one mesh gives many tints
//  - wind sway driven by vertex-colour alpha (0 = rooted, 1 = free tip) and a global wind vector
//  - alpha-cutout leaf cards, two-sided lighting, soft "rounded crown" normals, light shining through leaves
//  - cheap: one texture lookup, no blending, shadows + depth passes with the same wind so shadows move
Shader "Hearthvale/Plant"
{
    Properties
    {
        _BaseMap ("Albedo (A = cutout)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.1
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _TintAmount ("Instance tint amount", Range(0,1)) = 1
        _WindAmp ("Wind amplitude", Float) = 0.25
        _Trans ("Light through leaves", Range(0,2)) = 0.6
        _NormalUp ("Soft up normals", Range(0,1)) = 0.0
        [Toggle(_NORMALMAP)] _UseNormal ("Use normal map", Float) = 0
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _UvScale ("UV tiling", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale, _Smoothness, _Cutoff, _TintAmount, _WindAmp, _Trans, _NormalUp, _UvScale;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstColor)
        UNITY_INSTANCING_BUFFER_END(Props)
        float4 _HvWind; // x,z = direction, y = strength (0..~3), w = time

        // world-space sway: whole plant leans with the gust, tips flutter on top
        float3 Wind(float3 posWS, float weight)
        {
            float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
            float phase = origin.x * 0.31 + origin.z * 0.23;
            float t = _HvWind.w;
            float gust = sin(t * 1.4 + phase) * 0.6 + sin(t * 0.55 + phase * 0.37) * 0.4;
            float flutter = sin(t * 4.3 + posWS.x * 1.7 + posWS.z * 1.1 + weight * 5.0) * 0.18;
            float amp = (0.55 + _HvWind.y * 0.28) * _WindAmp * weight;
            float2 dir = _HvWind.xz;
            posWS.xz += dir * (gust + 0.6) * amp;
            posWS.xz += float2(-dir.y, dir.x) * flutter * amp;
            posWS.y -= abs(gust) * amp * 0.12;
            return posWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2;
                half4 color : TEXCOORD3; half fogFactor : TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 pw = TransformObjectToWorld(i.positionOS.xyz);
                pw = Wind(pw, i.color.a);
                o.positionWS = pw;
                o.positionCS = TransformWorldToHClip(pw);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv * _UvScale;
                o.color = i.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 PerturbNormal(float3 N, float3 p, float2 uv, float3 tn)
            {
                float3 dp1 = ddx(p), dp2 = ddy(p); float2 duv1 = ddx(uv), duv2 = ddy(uv);
                float3 dp2perp = cross(dp2, N), dp1perp = cross(N, dp1);
                float3 T = dp2perp * duv1.x + dp1perp * duv2.x, B = dp2perp * duv1.y + dp1perp * duv2.y;
                float invmax = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
                return normalize(mul(tn, float3x3(T * invmax, B * invmax, N)));
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                #if defined(_ALPHATEST_ON)
                    clip(tex.a - _Cutoff);
                #endif
                float4 inst = UNITY_ACCESS_INSTANCED_PROP(Props, _InstColor);
                half3 tint = lerp(half3(1,1,1), inst.rgb, _TintAmount);
                half3 albedo = tex.rgb * _BaseColor.rgb * tint * i.color.rgb;

                float3 N = normalize(i.normalWS);
                N = IS_FRONT_VFACE(face, N, -N);
                #if defined(_NORMALMAP)
                    float3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                    N = PerturbNormal(N, i.positionWS, i.uv, tn);
                #endif
                N = normalize(lerp(N, float3(0,1,0), _NormalUp)); // soft, rounded foliage shading

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.normalWS = N;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1,1,1,1);
                d.bakedGI = SampleSH(N);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = 0; s.smoothness = _Smoothness; s.occlusion = lerp(0.65, 1.0, i.color.r); s.alpha = 1; s.normalTS = half3(0,0,1);
                half4 c = UniversalFragmentPBR(d, s);

                // sunlight shining through the leaves (strongest when looking toward the sun)
                if (_Trans > 0.001)
                {
                    Light ml = GetMainLight(d.shadowCoord);
                    half back = pow(saturate(dot(d.viewDirectionWS, -ml.direction)), 3.0);
                    half wrap = saturate(dot(-N, ml.direction) * 0.5 + 0.5);
                    c.rgb += albedo * ml.color * ml.shadowAttenuation * (back * 0.8 + wrap * 0.25) * _Trans * 0.35;
                }
                c.rgb = MixFog(c.rgb, d.fogCoord);
                c.a = 1;
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i);
                float3 pw = Wind(TransformObjectToWorld(i.positionOS.xyz), i.color.a);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - pw);
                #else
                    float3 ld = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(pw, nw, ld));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.uv = i.uv * _UvScale;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.positionCS = TransformWorldToHClip(Wind(TransformObjectToWorld(i.positionOS.xyz), i.color.a)); o.uv = i.uv * _UvScale; return o; }
            half frag(V i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
