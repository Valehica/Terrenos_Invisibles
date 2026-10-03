Shader "Terrenos/OceanWater"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor ("Shallow Tint", Color) = (0.55, 0.92, 0.9, 1)
        _DeepColor ("Deep Color", Color) = (0.02, 0.14, 0.26, 1)
        _DepthMaxDistance ("Depth Fade Distance", Float) = 10

        [Header(Surface Detail)]
        [Normal] _NormalMap ("Normal Map (olas rodantes)", 2D) = "bump" {}
        [Normal] _NormalMap2 ("Normal Map 2 (ondas calmas)", 2D) = "bump" {}
        _NormalScale ("Normal Strength", Range(0, 2)) = 0.7
        _NormalTiling ("Normal Tiling (per meter)", Float) = 0.06
        _NormalSpeed ("Normal Scroll Speed", Float) = 0.04
        _Refraction ("Refraction", Range(0, 0.2)) = 0.04

        [Header(Waves  xy dir  z steepness  w wavelength)]
        _WaveA ("Wave A", Vector) = (-0.5, 1, 0.07, 60)
        _WaveB ("Wave B", Vector) = (-1, 0.4, 0.06, 35)
        _WaveC ("Wave C", Vector) = (-0.2, 1, 0.05, 18)
        _WaveD ("Wave D", Vector) = (-1, -0.2, 0.04, 10)
        _WaveSpeed ("Wave Speed", Float) = 1

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamTex ("Foam Mask (RGB = patrones)", 2D) = "white" {}
        _ShoreFoamDistance ("Shore Foam Distance", Float) = 3
        _FoamNoiseScale ("Foam Noise Scale", Float) = 0.35
        _CrestFoamHeight ("Crest Foam Height", Float) = 0.7

        [Header(Causticas)]
        _CausticTex ("Caustic Frames", 2DArray) = "" {}
        _CausticFrames ("Cantidad de cuadros", Float) = 32
        _CausticFPS ("Cuadros por segundo", Float) = 20
        _CausticScale ("Escala (por metro)", Float) = 0.25
        _CausticStrength ("Intensidad", Float) = 1.2
        _CausticMaxDepth ("Profundidad máxima visible", Float) = 4

        [Header(Reflection)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _FresnelPower ("Fresnel Power", Float) = 5
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.8
        _SpecularStrength ("Sun Specular", Float) = 3

        [Header(Shore  bakeado del terreno)]
        _ShoreHeightMap ("Terrain Height Map (R = metros)", 2D) = "black" {}
        _ShoreMapOrigin ("Terrain Origin XZ", Vector) = (0, 0, 0, 0)
        _ShoreMapSize ("Terrain Size XZ", Vector) = (1000, 800, 0, 0)
        _WaveShoreFade ("Profundidad para ola completa", Float) = 2.5
        _EdgeFade ("Suavizado del borde de agua", Float) = 0.4
        _FarDepth ("Profundidad fuera del terreno", Float) = 30
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_NormalMap2); SAMPLER(sampler_NormalMap2);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D_ARRAY(_CausticTex); SAMPLER(sampler_CausticTex);
            TEXTURE2D(_ShoreHeightMap); SAMPLER(sampler_ShoreHeightMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthMaxDistance;
                float4 _NormalMap_ST;
                float _NormalScale, _NormalTiling, _NormalSpeed, _Refraction;
                float4 _WaveA, _WaveB, _WaveC, _WaveD;
                float _WaveSpeed;
                float _ShoreFoamDistance, _FoamNoiseScale, _CrestFoamHeight;
                float _Smoothness, _FresnelPower, _ReflectionStrength, _SpecularStrength;
                float4 _ShoreMapOrigin, _ShoreMapSize;
                float _WaveShoreFade, _EdgeFade, _FarDepth;
                float4 _FoamTex_ST;
                float _CausticFrames, _CausticFPS, _CausticScale, _CausticStrength, _CausticMaxDepth;
            CBUFFER_END

            // Profundidad del agua en un punto: nivel del mar (origen del objeto)
            // menos la altura del terreno bakeada en el mapa.
            float WaterDepthAt(float2 worldXZ, float seaLevel)
            {
                float2 uv = (worldXZ - _ShoreMapOrigin.xy) / max(_ShoreMapSize.xy, 1e-4);
                float terrainH = SAMPLE_TEXTURE2D_LOD(_ShoreHeightMap, sampler_ShoreHeightMap, saturate(uv), 0).r;
                float dentro = seaLevel - terrainH;
                // Fuera del terreno no hay dato de altura. Saltar de golpe a _FarDepth
                // dibujaba una costura recta justo sobre el borde del mapa, asi que la
                // profundidad se mezcla en una franja de borde.
                float2 dBorde = min(uv, 1 - uv);
                float borde = saturate(min(dBorde.x, dBorde.y) / 0.03);
                return lerp(_FarDepth, dentro, borde);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float3 bitangentWS: TEXCOORD3;
                float  eyeDepth   : TEXCOORD4;
                float  waveHeight : TEXCOORD5;
                float  fogFactor  : TEXCOORD6;
                float  shoreDepth : TEXCOORD7;
            };

            float3 Gerstner(float4 wave, float3 p, inout float3 tangent, inout float3 binormal)
            {
                float steepness = wave.z;
                float k = TWO_PI / max(wave.w, 0.01);
                float c = sqrt(9.8 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * _Time.y * _WaveSpeed);
                float a = steepness / k;
                float s = sin(f), co = cos(f);

                tangent  += float3(-d.x * d.x * steepness * s, d.x * steepness * co, -d.x * d.y * steepness * s);
                binormal += float3(-d.x * d.y * steepness * s, d.y * steepness * co, -d.y * d.y * steepness * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 gridWS = TransformObjectToWorld(input.positionOS.xyz);

                // Las olas se aplanan al llegar a lo poco profundo: sin esto, en una
                // playa tendida la cresta inunda metros de arena y deja parches sueltos.
                float seaLevel = unity_ObjectToWorld._m13;
                float depth = WaterDepthAt(gridWS.xz, seaLevel);
                float waveFade = saturate((depth - 0.1) / max(_WaveShoreFade, 0.01));
                waveFade *= waveFade;

                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float3 offset = 0;
                offset += Gerstner(_WaveA, gridWS, tangent, binormal);
                offset += Gerstner(_WaveB, gridWS, tangent, binormal);
                offset += Gerstner(_WaveC, gridWS, tangent, binormal);
                offset += Gerstner(_WaveD, gridWS, tangent, binormal);

                float3 p = gridWS + offset * waveFade;
                tangent = lerp(float3(1, 0, 0), tangent, waveFade);
                binormal = lerp(float3(0, 0, 1), binormal, waveFade);
                o.shoreDepth = depth;

                o.positionWS = p;
                o.normalWS = normalize(cross(binormal, tangent));
                o.tangentWS = normalize(tangent);
                o.bitangentWS = normalize(binormal);
                o.waveHeight = offset.y * waveFade;
                o.positionCS = TransformWorldToHClip(p);
                o.eyeDepth = -TransformWorldToView(p).z;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // Detail normals: two scrolling layers
                float2 uv1 = i.positionWS.xz * _NormalTiling + t * _NormalSpeed * float2(0.8, 0.6);
                float2 uv2 = i.positionWS.xz * _NormalTiling * 1.7 + t * _NormalSpeed * float2(-0.5, 0.9);
                float3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1), _NormalScale);
                float3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap2, sampler_NormalMap2, uv2), _NormalScale);
                float3 nTS = normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
                float3 N = normalize(i.tangentWS * nTS.x + i.bitangentWS * nTS.y + i.normalWS * nTS.z);
                if (!isFrontFace) N = -N;

                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // Water depth behind this pixel
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float depth = max(sceneEye - i.eyeDepth, 0);

                // Refraction (reject samples from objects in front of the water)
                float2 refrUV = screenUV + nTS.xy * _Refraction * saturate(depth * 0.5);
                float refrEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                if (refrEye < i.eyeDepth) { refrUV = screenUV; refrEye = sceneEye; }
                float refrDepth = max(refrEye - i.eyeDepth, 0);
                half3 sceneColor = SampleSceneColor(refrUV);

                // Cáusticas sobre el fondo: se reconstruye el punto del fondo a lo largo
                // del rayo de vista y se proyecta la animación en su posición XZ.
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float rayScale = 1.0 / max(dot(-V, camFwd), 0.1);
                float3 floorWS = i.positionWS - V * (refrDepth * rayScale);
                float frame = floor(fmod(_Time.y * _CausticFPS, max(_CausticFrames, 1)));
                float2 cuv = floorWS.xz * _CausticScale + nTS.xy * 0.05;
                float caustic = SAMPLE_TEXTURE2D_ARRAY(_CausticTex, sampler_CausticTex, cuv, frame).r;
                caustic *= SAMPLE_TEXTURE2D_ARRAY(_CausticTex, sampler_CausticTex, cuv * 0.63 + 0.37, fmod(frame + 11, max(_CausticFrames, 1))).r * 1.6;
                float causticFade = saturate(1 - refrDepth / max(_CausticMaxDepth, 0.01)) * saturate(refrDepth * 4);
                Light sun = GetMainLight();
                sceneColor += caustic * causticFade * _CausticStrength * sun.color * saturate(sun.direction.y);

                // Absorption
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float NdotL = saturate(dot(N, mainLight.direction));
                half3 deep = _DeepColor.rgb * (mainLight.color * (0.35 + 0.65 * NdotL) + SampleSH(N));
                float absorb = 1 - exp(-refrDepth / max(_DepthMaxDistance, 0.01) * 3);
                half3 col = lerp(sceneColor * _ShallowColor.rgb, deep, absorb);

                // Reflection + fresnel
                float3 R = reflect(-V, N);
                half3 refl = GlossyEnvironmentReflection(R, 1 - _Smoothness, 1);
                float fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(N, V)), _FresnelPower);
                col = lerp(col, refl, fresnel * _ReflectionStrength);

                // Sun glint
                float3 H = normalize(mainLight.direction + V);
                col += pow(saturate(dot(N, H)), 400) * mainLight.color * _SpecularStrength * mainLight.shadowAttenuation;

                // Luces extra (por ejemplo la linterna del jugador)
                #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; li++)
                {
                    Light extra = GetAdditionalLight(li, i.positionWS);
                    float atten = extra.distanceAttenuation;
                    float3 Hx = normalize(extra.direction + V);
                    col += extra.color * atten * (saturate(dot(N, extra.direction)) * 0.25
                         + pow(saturate(dot(N, Hx)), 120) * 1.5);
                }
                #endif

                // Foam: shoreline bands + wave crests
                // Espuma con la máscara de SUIMONO: dos capas que se deslizan en
                // direcciones distintas, cada una con un canal del patrón.
                float2 foamUV = i.positionWS.xz * _FoamNoiseScale * 0.12;
                float foamA = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV + t * float2(0.012, 0.02)).r;
                float foamB = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV * 1.9 - t * float2(0.018, 0.009)).g;
                float noise = 1 - saturate(foamA * 0.6 + foamB * 0.6);
                float shore = 1 - saturate(depth / max(_ShoreFoamDistance, 0.01));
                float bands = 0.5 + 0.5 * sin(depth / max(_ShoreFoamDistance, 0.01) * 12 - t * 2.5);
                float shoreFoam = saturate(smoothstep(noise, noise + 0.15, shore * (0.75 + 0.5 * bands)));
                shoreFoam *= saturate(i.shoreDepth / 0.12);   // sin espuma sobre la arena seca
                float crest = saturate((i.waveHeight - _CrestFoamHeight) * 2);
                float crestFoam = smoothstep(0.55, 0.75, noise) * crest;
                float foam = saturate(max(shoreFoam, crestFoam));
                col = lerp(col, _FoamColor.rgb * (mainLight.color * NdotL * 0.7 + SampleSH(float3(0, 1, 0))), foam * _FoamColor.a);

                col = MixFog(col, i.fogFactor);

                // El borde del agua se desvanece en los últimos centímetros de
                // profundidad: así la línea de costa no muestra el borde de los
                // triángulos ni parches sueltos al subir la marea.
                float alpha = saturate(i.shoreDepth / max(_EdgeFade, 0.01));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
