// A plain two-colour sky: Frog Sqwad's cyan at the top, fading to a pale
// horizon. Replaces Unity's default procedural skybox, whose sun disc and
// atmosphere pull the whole scene toward realism.
//
// Only what you see: ambient light comes from the scene's gradient ambient
// settings, not from this sky.
Shader "Coat/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.357, 0.722, 0.796, 1)
        _HorizonColor ("Horizon", Color) = (0.647, 0.847, 0.886, 1)
        _BottomColor ("Below the horizon", Color) = (0.647, 0.847, 0.886, 1)
        _Curve ("How quickly the top colour takes over", Range(0.1, 4)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half _Curve;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float up = normalize(input.direction).y;
                half3 colour = up >= 0.0
                    ? lerp(_HorizonColor.rgb, _TopColor.rgb, pow(saturate(up), _Curve))
                    : lerp(_HorizonColor.rgb, _BottomColor.rgb, saturate(-up * 4.0));
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
}
