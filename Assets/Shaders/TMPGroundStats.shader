Shader "BlockingKing/TMP Ground Stats"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1,1,1,1)
        _GrayMix ("Gray Mix", Range(0, 1)) = 0
        _AlphaScale ("Alpha Scale", Range(0, 1)) = 1
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0
        _OutlineAlpha ("Outline Alpha", Range(0, 1)) = 0
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _SdfSoftness ("SDF Softness", Range(0.001, 0.25)) = 0.06
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            ZTest [_ZTest]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _FaceColor;
            float _GrayMix;
            float _AlphaScale;
            float _OutlineWidth;
            float _OutlineAlpha;
            float4 _OutlineColor;
            float _SdfSoftness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 sample = tex2D(_MainTex, i.uv);
                float distance = sample.a > 0.99 ? sample.r : sample.a;
                float alpha = smoothstep(0.5 - _SdfSoftness, 0.5 + _SdfSoftness, distance);
                float outlineAlpha = smoothstep(
                    0.5 - _OutlineWidth - _SdfSoftness,
                    0.5 - _OutlineWidth + _SdfSoftness,
                    distance);
                float outlineRing = saturate(outlineAlpha - alpha) * _OutlineAlpha;

                fixed4 color = i.color * _FaceColor;
                float gray = dot(color.rgb, float3(0.299, 0.587, 0.114));
                color.rgb = lerp(color.rgb, gray.xxx, _GrayMix);
                color.a *= alpha * _AlphaScale;

                fixed4 outline = _OutlineColor;
                outline.a *= outlineRing;

                fixed4 outputColor;
                outputColor.rgb = lerp(outline.rgb, color.rgb, color.a);
                outputColor.a = saturate(color.a + outline.a * (1.0 - color.a));
                return outputColor;
            }
            ENDCG
        }
    }
}
