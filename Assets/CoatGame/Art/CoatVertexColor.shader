// Lit, and tinted by the mesh's vertex colours.
//
// The limb colours live in a vertex colour attribute rather than in separate
// materials, because a per-face material boundary can only run along triangle
// edges and so staircases; a vertex colour interpolates across the triangle and
// the seam comes out smooth. URP's own Lit shader ignores vertex colours
// entirely, so the character would arrive plain grey without this.
//
// Deliberately simple lighting -- main light plus spherical harmonics ambient.
// It avoids UniversalFragmentPBR, whose SurfaceData/InputData layout shifts
// between URP versions and is the usual reason a hand-written URP shader stops
// compiling after an upgrade.
Shader "Coat/VertexColorLit"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Ambient("Extra ambient", Range(0,1)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Ambient;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndl = saturate(dot(n, mainLight.direction));
                half3 lit = mainLight.color * ndl * mainLight.shadowAttenuation;
                half3 ambient = SampleSH(n) + _Ambient.xxx;

                // The vertex colours were authored in sRGB and the project
                // renders in linear, so used raw they come out pale and washed
                // out -- a 0.90 red reads as though it were already linear and
                // arrives far too bright.
                half3 vertex = SRGBToLinear(i.color.rgb);

                half3 albedo = vertex * _BaseColor.rgb;
                return half4(albedo * (lit + ambient), 1.0);
            }
            ENDHLSL
        }

        // Borrowed whole rather than reimplemented: the shadow pass has to
        // match URP's own bias and normal-offset handling or the character
        // shadow-acnes against the floor.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
