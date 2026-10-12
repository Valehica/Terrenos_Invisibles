Shader "TENKOKU/moonsphere_shader_URP"
{
    Properties
    {
        _PrimaryTint ("Primary Tint", Color) = (1,1,1,1)
        _Color ("Main Color", Color) = (1,1,1,1)
        _AmbientTint ("Ambient Tint", Color) = (1,1,1,1)
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _BRDFTex ("BRDF", 2D) = "white" {}
        _overBright ("OverBright", Float) = 1.0
        _dispStrength ("Displace Amount", Range(0.0,3.0)) = 1.0
        _GlowColor ("Glow Color", Color) = (0.5,0.5,0.5,0.5)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background+1605" "RenderType" = "Opaque" }
        Cull Back
        ZWrite Off
        Offset 1, 993000

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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _PrimaryTint;
            float4 _Color;
            float4 _AmbientTint;
            float _overBright;
            float _dispStrength;
            float4 _GlowColor;
            CBUFFER_END

            // Set globally every frame by Tenkoku.Core.TenkokuModule
            float4 Tenkoku_MoonLightColor;
            float4 Tenkoku_MoonHorizColor;
            float3 Tenkoku_Vec_SunFwd;
            float4 _Tenkoku_overcastColor;
            float _Tenkoku_NightBright;
            float _Tenkoku_Ambient;
            float Tenkoku_MoonHFac;
            float _tenkokuIsLinear;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                tex.rgb *= clamp(lerp(-0.25, 3.0, tex.r), 0.0, 3.0);

                half3 albedo = tex.rgb * _PrimaryTint.rgb;

                float3 n = normalize(IN.normalWS);
                half alph = saturate(max(0.0, dot(n, Tenkoku_Vec_SunFwd)) * 2.0);

                half3 mTCol = lerp(half3(1,1,1), Tenkoku_MoonHorizColor.rgb * 2.0, Tenkoku_MoonHorizColor.a);
                albedo = albedo * lerp(mTCol, 1.0, max(Tenkoku_MoonHFac, _Tenkoku_Ambient));

                // Extend brightness range like the original
                albedo.r = lerp(0.0, 2.5, albedo.r);
                albedo.g = lerp(0.0, 2.1, albedo.g);
                albedo.b = lerp(0.0, 2.0, albedo.b);

                if (_tenkokuIsLinear == 0.0)
                    albedo = saturate(albedo * 0.4646);

                // Approximate night-sky base instead of sampling the legacy skybox RT
                half3 nightSky = half3(0.02, 0.03, 0.06) * max(0.15, _Tenkoku_NightBright);

                half3 lit = lerp(nightSky, albedo * max(0.5, Tenkoku_MoonLightColor.rgb) * lerp(2.0, 5.0, _AmbientTint.rgb), alph);
                lit = lerp(lit, nightSky, min(0.8, _AmbientTint.r));
                lit = max(nightSky, lit);

                half fade = saturate(lerp(1.0, -3.0, _Tenkoku_overcastColor.a));

                return half4(lit, fade);
            }
            ENDHLSL
        }
    }
}
