// Additive camera-facing glow sprite (fireflies, lamp halos). GPU instanced: position and size come from
// the instance matrix (translation, uniform scale); colour and flicker are computed here, so no
// per-instance property arrays are needed.
Shader "Hearthvale/Glow"
{
    Properties
    {
        _BaseMap ("Glow", 2D) = "white" {}
        [HDR] _Color ("Colour", Color) = (1,0.9,0.35,1)
        _Intensity ("Intensity", Float) = 3
        _Fade ("Fade", Range(0,1)) = 1
        _Flicker ("Flicker", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Glow"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Intensity, _Fade, _Flicker;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; half flick : TEXCOORD1; };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i);
                float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float size = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 pw = origin + (right * i.pos.x + up * i.pos.y) * size;
                o.cs = TransformWorldToHClip(pw); o.uv = i.uv;
                o.flick = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 3.0 + origin.x * 7.3 + origin.z * 3.1 + origin.y * 1.7));
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half d = length(i.uv - 0.5) * 2.0;
                half a = saturate(1.0 - d); a = a * a * (0.4 + a); // soft round glow, no texture needed
                return half4(_Color.rgb * (a * _Intensity * _Fade * i.flick), 1);
            }
            ENDHLSL
        }
    }
}
