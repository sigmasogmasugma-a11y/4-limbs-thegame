// Draws nothing.
//
// A limb nobody is wearing has to disappear. When the rig was capsules that was
// free -- SetPresent(false) switches the limb's GameObjects off and the
// renderer goes with them. A skinned mesh is ONE renderer for the whole body,
// so switching it off would take the whole disguise with it.
//
// The mesh is split into a submesh per limb though, so the limb can be hidden
// by giving its submesh a material that renders nothing. ColorMask 0 writes no
// colour, ZWrite Off writes no depth: the draw call still happens and costs
// approximately nothing, and nothing appears.
//
// Collapsing the limb's bones to a point instead would pinch the shoulder,
// because the vertices around it are weighted partly to the torso and would get
// dragged in with it.
Shader "Coat/Hidden"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "Nothing"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
