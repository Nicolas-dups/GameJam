Shader "Custom/OutlineTransparent (URP)"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color (HDR)", Color) = (1, 0.5, 0, 1)
        _EmissionIntensity ("Emission Intensity", Range(0, 20)) = 1
        _OutlineWidth ("Outline Width (world units)", Float) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                half _EmissionIntensity;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = normalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _OutlineWidth;

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                return half4(_OutlineColor.rgb * _EmissionIntensity, _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
