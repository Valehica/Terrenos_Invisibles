Shader "TENKOKU/cloud_layer_URP"
{
    Properties
    {
        _CloudColor ("Cloud Color", Color) = (1,1,1,1)
        _ShadowColor ("Shadow Color", Color) = (0.55,0.58,0.65,1)
        _Coverage ("Coverage", Range(0,1)) = 0.35
        _Softness ("Edge Softness", Range(0.01,0.6)) = 0.25
        _Scale ("Noise Scale", Float) = 3.0
        _Speed ("Drift Speed (radians/sec)", Float) = 0.01
        _Opacity ("Max Opacity", Range(0,1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Background+1700" "RenderType" = "Transparent" }
        Cull Front
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _CloudColor;
            float4 _ShadowColor;
            float _Coverage;
            float _Softness;
            float _Scale;
            float _Speed;
            float _Opacity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                // Direction from the dome center to this vertex, on the unit sphere.
                // Using this (instead of a 2D world-XZ projection) for the noise avoids
                // any pole/seam singularity, so there's no radial streaking near the zenith.
                OUT.dirOS = normalize(IN.positionOS.xyz);
                return OUT;
            }

            float hash3(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash3(i + float3(0,0,0));
                float n100 = hash3(i + float3(1,0,0));
                float n010 = hash3(i + float3(0,1,0));
                float n110 = hash3(i + float3(1,1,0));
                float n001 = hash3(i + float3(0,0,1));
                float n101 = hash3(i + float3(1,0,1));
                float n011 = hash3(i + float3(0,1,1));
                float n111 = hash3(i + float3(1,1,1));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            float fbm3D(float3 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 5; i++)
                {
                    v += amp * noise3D(p);
                    p *= 2.02;
                    amp *= 0.5;
                }
                return v;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dirOS);

                // Slowly rotate the sampling direction around the up axis for a gentle drift
                // instead of translating a 2D UV (which is what caused the fast, streaky motion).
                float angle = _Time.y * _Speed;
                float s = sin(angle);
                float c = cos(angle);
                float3 rotated = float3(dir.x * c - dir.z * s, dir.y, dir.x * s + dir.z * c);

                float3 p = rotated * _Scale;
                float n = fbm3D(p);
                float density = smoothstep(1.0 - _Coverage - _Softness, 1.0 - _Coverage + _Softness, n);

                float shade = fbm3D(p * 2.3 + 7.0);
                half3 col = lerp(_ShadowColor.rgb, _CloudColor.rgb, saturate(shade + 0.35));

                half alpha = saturate(density) * _Opacity;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
