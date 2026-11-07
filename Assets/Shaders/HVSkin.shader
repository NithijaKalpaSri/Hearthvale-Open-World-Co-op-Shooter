// Hearthvale living-things shader: skin, cloth, hair, scales, hide.
//  - triplanar fine detail (pores / weave / wrinkles) taken from the bind pose, so it never stretches when limbs bend
//  - soft subsurface look: light wraps around the terminator and glows warm in thin / shadowed areas
//  - baked ambient occlusion in vertex colour (creases go dark), gentle fresnel sheen, optional wetness
Shader "Hearthvale/Skin"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1,1,1,1)
        _BaseMap ("Albedo (triplanar, optional)", 2D) = "white" {}
        _DetailMap ("Detail normal", 2D) = "bump" {}
        _DetailScale ("Detail tiling", Float) = 18
        _DetailStrength ("Detail strength", Float) = 0.6
        _Smoothness ("Smoothness", Range(0,1)) = 0.35
        _Wet ("Wet sheen", Range(0,1)) = 0
        [HDR] _SssColor ("Subsurface colour", Color) = (0.9,0.3,0.2,1)
        _SssAmount ("Subsurface amount", Range(0,1)) = 0.5
        _Wrap ("Light wrap", Range(0,1)) = 0.5
        _Rim ("Fresnel sheen", Range(0,1)) = 0.2
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,0)
        _Alpha ("Alpha", Range(0,1)) = 1
        _EmissionMap ("Emission map (triplanar)", 2D) = "white" {}
        _MapScale ("Albedo tiling", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _SssColor, _EmissionColor;
            half _DetailScale, _DetailStrength, _Smoothness, _Wet, _SssAmount, _Wrap, _Rim, _Alpha, _MapScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float3 bindN : TEXCOORD2; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv0 : TEXCOORD2; float2 uv1 : TEXCOORD3;
                float3 bindN : TEXCOORD4; half4 color : TEXCOORD5; half fogFactor : TEXCOORD6;
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz); VertexNormalInputs n = GetVertexNormalInputs(i.normalOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = n.normalWS;
                o.uv0 = i.uv0; o.uv1 = i.uv1; o.bindN = i.bindN; o.color = i.color; o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 bw = pow(abs(normalize(i.bindN)), 4.0); bw /= (bw.x + bw.y + bw.z + 1e-5);
                // triplanar detail normal: perturbation expressed in bind-space axes, rotated to world with the object matrix
                float2 dz = UnpackNormal(SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uv0 * _DetailScale)).xy;
                float2 dx = UnpackNormal(SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.uv1 * _DetailScale)).xy;
                float2 dy = UnpackNormal(SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, float2(i.uv0.x, i.uv1.x) * _DetailScale)).xy;
                float3 P = bw.z * float3(dz.x, dz.y, 0) + bw.x * float3(0, dx.y, dx.x) + bw.y * float3(dy.x, 0, dy.y);
                float3 Pw = mul((float3x3)unity_ObjectToWorld, P);
                float3 N = normalize(i.normalWS); N = normalize(N + (Pw - N * dot(Pw, N)) * _DetailStrength);
                half3 albedo = _BaseColor.rgb * i.color.rgb;
                if (_MapScale > 0.001) { half3 tx = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv0 * _MapScale).rgb * bw.z + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv1 * _MapScale).rgb * bw.x + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, float2(i.uv0.x, i.uv1.x) * _MapScale).rgb * bw.y; albedo *= tx; }

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.normalWS = N; d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS); d.fogCoord = i.fogFactor; d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1,1,1,1); d.bakedGI = SampleSH(N);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = 0; s.smoothness = saturate(_Smoothness + _Wet * 0.5); s.occlusion = lerp(1.0, i.color.r, 0.5); s.alpha = 1; s.normalTS = half3(0,0,1); s.emission = _EmissionColor.rgb * (SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv0 * 3.0).rgb * bw.z + SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv1 * 3.0).rgb * bw.x + SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, float2(i.uv0.x, i.uv1.x) * 3.0).rgb * bw.y);
                half4 c = UniversalFragmentPBR(d, s);

                // subsurface: light wraps past the terminator and glows in the shadowed side; thin parts catch light from behind
                Light L = GetMainLight(d.shadowCoord);
                half ndl = dot(N, L.direction);
                half wrapD = saturate((ndl + _Wrap) / (1.0 + _Wrap)) - saturate(ndl);
                half back = saturate(dot(d.viewDirectionWS, -L.direction)) * saturate(-ndl * 0.5 + 0.5);
                c.rgb += albedo * _SssColor.rgb * (wrapD * 1.2 + back * 0.15) * L.color * L.shadowAttenuation * _SssAmount * i.color.r;
                half fres = pow(1.0 - saturate(dot(N, d.viewDirectionWS)), 3.0);
                c.rgb += fres * _Rim * (d.bakedGI * 0.6 + L.color * saturate(ndl) * 0.15) * (_Smoothness + 0.4);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                c.a = _Alpha; return c;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A i)
            {
                V o; float3 pw = TransformObjectToWorld(i.positionOS.xyz); float3 nw = TransformObjectToWorldNormal(i.normalOS);
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
                return o;
            }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
