Shader "BlockingKing/BoxGlass"
{
    Properties
    {
        _Color ("Color", Color) = (0.3, 0.8, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 0.24
        _EdgeDarkness ("Edge Darkness", Range(0, 1)) = 0.55
        _FaceTint ("Face Tint", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "GlassForward"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 _Color;
            float _Alpha;
            float _EdgeDarkness;
            float _FaceTint;

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 p = abs(input.positionOS);
                float edge = max(max(p.x, p.y), p.z);
                float edgeBand = smoothstep(0.36, 0.5, edge);
                float3 faceColor = _Color.rgb * _FaceTint;
                float3 edgeColor = _Color.rgb * (1.0 - _EdgeDarkness);
                float3 color = lerp(faceColor, edgeColor, edgeBand);
                float alpha = saturate(_Alpha + edgeBand * 0.08);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
