// Fondo lejano de la escena Sur (Puerto Eden).
//
// La malla que usa este shader sale del DEM real (SRTM 1 arcsec) en un anillo
// que arranca justo en el borde del Terrain y llega a 30 km. No lleva colisor
// ni sombras: es paisaje, no terreno jugable.
//
// El color no viene de texturas sino de la altura, porque a esa distancia lo
// unico que se lee son bandas tonales: agua, bosque oscuro, roca, nieve. La
// perspectiva aerea NO se aplica aca — la malla es opaca y escribe profundidad,
// asi que el pase de pantalla (RenderShaderURP) ya la tine junto con el resto.
Shader "Terrenos/MontanasLejanas"
{
    Properties
    {
        _ColorAgua   ("Agua",            Color) = (0.28, 0.33, 0.37, 1)
        _ColorBosque ("Bosque",          Color) = (0.15, 0.19, 0.16, 1)
        _ColorMatorral("Matorral",       Color) = (0.24, 0.26, 0.21, 1)
        _ColorRoca   ("Roca",            Color) = (0.30, 0.30, 0.32, 1)
        _ColorNieve  ("Nieve",           Color) = (0.87, 0.89, 0.93, 1)

        _AlturaBosque("Tope del bosque (m)", Float) = 230
        _AlturaRoca  ("Roca desnuda (m)",    Float) = 430
        _LineaNieve  ("Linea de nieve (m)",  Float) = 360
        _AnchoNieve  ("Ancho de la linea",   Float) = 340
        _NievePendiente("Nieve: corte por pendiente", Range(0.1, 1)) = 0.62

        _RuidoEscala ("Escala de ruido",  Float) = 0.0035
        _RuidoFuerza ("Fuerza de ruido",  Range(0, 1)) = 0.40
        _Wrap        ("Wrap de la luz",   Range(0, 1)) = 0.60
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorAgua, _ColorBosque, _ColorMatorral, _ColorRoca, _ColorNieve;
                float  _AlturaBosque, _AlturaRoca, _LineaNieve, _AnchoNieve, _NievePendiente;
                float  _RuidoEscala, _RuidoFuerza, _Wrap;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Ruido(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i),              Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + 1),          u.x), u.y);
            }

            float Fbm(float2 p)
            {
                return Ruido(p) * 0.55 + Ruido(p * 2.3) * 0.29 + Ruido(p * 5.1) * 0.16;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float  h = i.positionWS.y;
                float  ruido = Fbm(i.positionWS.xz * _RuidoEscala) - 0.5;

                // Desplazar las cotas con ruido: si no, las bandas se ven como
                // curvas de nivel de mapa y delatan que el color viene de la altura.
                float hr = h + ruido * _RuidoFuerza * 260.0;

                // Vertical = 0, plano = 1. La nieve solo cuaja en lo tendido.
                float tendido = saturate((n.y - (1.0 - _NievePendiente)) / max(_NievePendiente, 0.01));

                float3 col = _ColorBosque.rgb;
                col = lerp(col, _ColorMatorral.rgb, smoothstep(_AlturaBosque * 0.45, _AlturaBosque, hr));
                col = lerp(col, _ColorRoca.rgb,     smoothstep(_AlturaBosque, _AlturaRoca, hr));

                float nieve = smoothstep(_LineaNieve, _LineaNieve + _AnchoNieve, hr)
                            * (0.35 + 0.65 * tendido);
                col = lerp(col, _ColorNieve.rgb, saturate(nieve));

                // El agua es plana y esta justo bajo el nivel del mar.
                col = lerp(col, _ColorAgua.rgb, saturate((0.1 - h) * 4.0));

                Light L = GetMainLight();
                float ndl = saturate((dot(n, L.direction) + _Wrap) / (1.0 + _Wrap));
                float3 lit = col * (L.color * ndl + SampleSH(n));

                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // Sin sombras ni depth prepass propios: el fondo no proyecta nada.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct AD { float4 positionOS : POSITION; };
            struct VD { float4 positionCS : SV_POSITION; };
            VD vertD(AD v) { VD o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half fragD(VD i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
