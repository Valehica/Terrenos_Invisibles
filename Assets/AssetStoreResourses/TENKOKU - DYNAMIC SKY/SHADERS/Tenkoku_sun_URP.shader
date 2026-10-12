Shader "TENKOKU/sun_shader_URP"
{
    Properties
    {
        _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
        _CoronaColor ("Corona Color", Color) = (0.5,0.5,0.5,0.5)
        _MainTex ("BRDF", 2D) = "white" {}
        _overBright ("OverBright", Float) = 1.0
        _dispStrength ("Displace Amount", Range(0.0,10.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background+1604" "RenderType" = "Transparent" }
        Cull Front
        ZWrite Off
        Blend One One
        Offset 1, 995000

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _TintColor;
            float4 _CoronaColor;
            float _overBright;
            float _dispStrength;
            CBUFFER_END

            // Set globally every frame by Tenkoku.Core.TenkokuModule
            float4 _TenkokuSunColor;
            float4 _Tenkoku_overcastColor;
            float _Tenkoku_AmbientGI;
            float _Tenkoku_Ambient;
            float _Tenkoku_EclipseFactor;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz + (IN.normalOS * 0.75);
                float3 posWS = TransformObjectToWorld(posOS);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);
                float ndotv = dot(v, -n);

                half sSize = saturate(1.0 - saturate(_Tenkoku_overcastColor.a * 3));
                sSize *= saturate(lerp(-1.0, 1.0, _Tenkoku_Ambient));

                half a = 0;
                a += saturate(lerp(-0.5, 1, ndotv)) * 0.05;
                a += saturate(lerp(-1.0, 1, ndotv)) * 0.05;
                a += saturate(lerp(-2.0, 1, ndotv)) * 0.1;
                a += saturate(lerp(-3.0, 1, ndotv)) * 0.1;
                a += saturate(lerp(-6.0, 1, ndotv)) * 0.1;
                a += saturate(lerp(-2.0, 1, ndotv));
                a *= sSize * _Tenkoku_EclipseFactor;

                half3 col = lerp(_CoronaColor.rgb, _TintColor.rgb * _TenkokuSunColor.rgb, a);
                col += _overBright * saturate(ndotv) * saturate(lerp(0.0, 4.0, _Tenkoku_AmbientGI));

                half alpha = saturate(a - saturate(_Tenkoku_overcastColor.a * 3));
                alpha *= saturate(lerp(0.0, 4.0, _Tenkoku_Ambient));

                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
