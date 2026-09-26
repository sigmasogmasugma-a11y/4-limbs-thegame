// Toon shading for the characters, and only the characters: the disguise, the
// kids, the coat and the observers. The world stays on stock URP Lit.
//
// The Frog Sqwad look (see "The 4 Limbs Look" art direction page):
//   - two flat tones, lit and shadow, with a hard edge between them. The shadow
//     tone is its own colour (_ShadeColor), not the lit colour darkened, so the
//     palette decides it rather than the light;
//   - a small, hard-edged highlight instead of a soft glossy one;
//   - a thin outline in dark plum, never black.
//
// Colours land on screen close to their hex values: the lit tone is _BaseColor
// itself, only nudged by the sun's colour (_LightTint), never scaled by the
// light's intensity. That is what keeps the swatches in the doc honest.
//
// Every pass URP asks for is written out here, because the last custom shader in
// this project (CoatVertexColor) borrowed a depth pass that rejected it: it drew
// in the Scene view and nothing in the Game view. This renderer runs Forward+
// with screen-space ambient occlusion, which renders a DepthNormals prepass, so
// DepthOnly and DepthNormals have to be real. Test changes in the Game view.
//
// Hidden limbs are untouched: CoatSkin swaps their submesh to Coat/Hidden, and
// this shader is only ever the "shown" material.
Shader "Coat/Toon"
{
    Properties
    {
        _BaseColor ("Lit colour", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shadow colour", Color) = (0.6, 0.6, 0.6, 1)
        _ShadeThreshold ("Shadow edge position", Range(0, 1)) = 0.5
        _ShadeSoftness ("Shadow edge softness", Range(0.001, 0.2)) = 0.015
        _LightTint ("How much the sun's colour tints it", Range(0, 1)) = 0.25
        _HighlightColor ("Highlight (alpha = strength)", Color) = (1, 1, 1, 0.35)
        _HighlightSize ("Highlight size", Range(0, 0.2)) = 0.03
        _OutlineColor ("Outline colour", Color) = (0.184, 0.086, 0.263, 1)
        _OutlineWidth ("Outline width (pixels, 0 = none)", Range(0, 8)) = 2.2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Off for the open coat tube)", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // One material buffer, identical in every pass, so the SRP Batcher keeps
        // all the characters in one batch.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeColor;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _LightTint;
            half4 _HighlightColor;
            half _HighlightSize;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL

        // ---- the character ------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // With culling off (the coat tube) the inside is lit as its own
                // surface rather than as the far side of the outside.
                float3 n = normalize(input.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light light = GetMainLight(shadowCoord);

                // Lit or shadow, with a hard edge. Half-Lambert puts the edge at
                // the terminator when the threshold is 0.5. Cast shadows (the van
                // roof, another limb) snap to the shadow tone the same way.
                half ndl = dot(n, light.direction) * 0.5 + 0.5;
                half lit = smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, ndl);
                lit *= smoothstep(0.35, 0.65, light.shadowAttenuation);

                half3 colour = lerp(_ShadeColor.rgb, _BaseColor.rgb, lit);

                // A small hard highlight, only on the lit side.
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half nh = saturate(dot(n, normalize(light.direction + v)));
                half edge = 1.0 - _HighlightSize;
                half highlight = smoothstep(edge - 0.004, edge + 0.004, nh) * lit * step(0.0001, _HighlightSize);
                colour = lerp(colour, _HighlightColor.rgb, highlight * _HighlightColor.a);

                // The sun's hue, not its strength.
                half peak = max(max(light.color.r, light.color.g), max(light.color.b, 0.0001));
                colour *= lerp(half3(1.0, 1.0, 1.0), light.color / peak, _LightTint);

                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // ---- the outline ---------------------------------------------------------
        // The back faces, pushed out along their normals and drawn in plum. URP
        // draws SRPDefaultUnlit passes alongside UniversalForward, so the outline
        // needs no renderer feature and follows the material wherever it goes.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // Width 0 means no outline at all: collapse every triangle to one
                // point so nothing is drawn. Merely not pushing them out would still
                // paint the back faces plum wherever they show, such as inside the
                // open coat tube.
                if (_OutlineWidth <= 0.0)
                {
                    o.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                    return o;
                }

                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                float len = max(length(normalCS), 1e-5);

                // Width in pixels: clip space is 2 units across the screen, and
                // scaling by w keeps the line the same width at any distance, so
                // the small kids and the tall disguise get the same outline.
                positionCS.xy += (normalCS / len) * (_OutlineWidth * 2.0 / _ScreenParams.xy) * positionCS.w;

                o.positionCS = positionCS;
                o.fogFactor = ComputeFogFactor(positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        // ---- shadows the character casts -----------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP for the light being rendered, as its own ShadowCasterPass does.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ---- depth, for the depth prepass ------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half frag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ---- depth and normals, for screen-space ambient occlusion ------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    half3 packed = PackFloat2To888(saturate(oct * 0.5 + 0.5));
                    return half4(packed, 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
