Shader "Terrenos/OceanWaterRealista"
{
    // Agua realista: transparencia por absorcion fisica, olas de rompiente que
    // siguen la costa y teselacion cerca de la camara para poder dibujarlas.
    //
    // Hereda de OceanWaterStylized el oleaje de Gerstner con deformacion de dominio,
    // la profundidad bakeada del terreno (_ShoreHeightMap), la resaca, las causticas
    // y la perspectiva aerea del pase de estilo. Las propiedades que mueve
    // MareaController (_WaveA-D, _CrestFoamHeight, _ShoreFoamDistance) conservan el nombre.
    Properties
    {
        [Header(Transparencia  absorcion por metro de agua atravesada)]
        _Absorcion ("Absorcion por metro (RGB)", Vector) = (0.42, 0.10, 0.07, 0)
        _Turbidez ("Turbidez (extincion gris por metro)", Range(0, 0.5)) = 0.04
        _ColorDispersion ("Color del agua en volumen", Color) = (0.05, 0.19, 0.24, 1)
        _Refraction ("Refraccion", Range(0, 0.2)) = 0.05
        _Desaturation ("Desaturacion", Range(0, 1)) = 0

        [Header(Detalle de superficie)]
        [Normal] _NormalMap ("Normal Map (olas rodantes)", 2D) = "bump" {}
        [Normal] _NormalMap2 ("Normal Map 2 (ondas calmas)", 2D) = "bump" {}
        _NormalScale ("Normal Strength", Range(0, 2)) = 0.6
        _NormalTiling ("Normal Tiling (per meter)", Float) = 0.06
        _NormalSpeed ("Normal Scroll Speed", Float) = 0.04
        _NormalLejos ("Normal a 400 m (fraccion)", Range(0, 1)) = 0.35

        [Header(Oleaje  largos por encima del paso de la malla  8.13 m por quad)]
        _WaveA ("Wave A", Vector) = (-0.707, 0.707, 0.0314, 220)
        _WaveB ("Wave B", Vector) = (-0.940, 0.342, 0.0293, 150)
        _WaveC ("Wave C", Vector) = (-0.985, -0.174, 0.0251, 100)
        _WaveD ("Wave D", Vector) = (-0.819, -0.574, 0.0203, 68)
        _WaveSpeed ("Wave Speed", Float) = 1
        _WarpScale ("Escala de la deformacion", Float) = 0.004
        _WarpStrength ("Fuerza de la deformacion (m)", Float) = 26

        [Header(Rompientes  olas que siguen la costa)]
        _ShoreDistMap ("Distancia a la costa (Terrenos  Agua  Bakear)", 2D) = "black" {}
        _RompienteAltura ("Altura de ola al llegar (m)", Float) = 0.9
        _RompienteLargo ("Largo de onda (m)", Float) = 24
        _RompientePeriodo ("Periodo (s)", Float) = 8
        _RompienteZona ("Profundidad donde aparecen (m)", Float) = 9
        _RompienteAlcance ("Distancia maxima a la costa (m)", Float) = 200
        _RompienteVariacion ("Escala de la irregularidad (por metro)", Float) = 0.012
        _RompienteCurva ("Ondulacion de las crestas (ciclos)", Float) = 0.7
        _RompienteGrupos ("Variacion entre olas", Range(0, 1)) = 0.55
        _RompienteInclinacion ("Frente volcado", Range(0, 1.5)) = 0.7

        [Header(Resaca  el lavado de cada ola  va al ritmo de las rompientes)]
        _ResacaAmplitud ("Amplitud (m de profundidad)", Float) = 0.45
        _ResacaAlcance ("Hasta que profundidad llega (m)", Float) = 3.0

        [Header(Espuma)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamTex ("Foam Mask (RGB = patrones)", 2D) = "white" {}
        _FoamTiling ("Escala de la textura (por metro)", Float) = 0.12
        _EspumaRompiente ("Espuma de rompiente", Range(0, 2)) = 1
        _EspumaEstela ("Duracion de la estela (fraccion de periodo)", Range(0.05, 1.5)) = 0.5
        _EspumaContraste ("Nitidez del encaje", Range(1, 12)) = 4
        _ShoreFoamDistance ("Shore Foam Distance", Float) = 1.2
        _CrestFoamHeight ("Crest Foam Height", Float) = 2.4

        [Header(Causticas)]
        _CausticTex ("Caustic Frames", 2DArray) = "" {}
        _CausticFrames ("Cantidad de cuadros", Float) = 32
        _CausticFPS ("Cuadros por segundo", Float) = 20
        _CausticScale ("Escala (por metro)", Float) = 0.25
        _CausticStrength ("Intensidad", Float) = 0.9
        _CausticMaxDepth ("Profundidad maxima visible", Float) = 4

        [Header(Reflejo y sol)]
        _Smoothness ("Smoothness del reflejo", Range(0, 1)) = 0.9
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 1
        _ReflejoCielo ("Reflejo del cielo SH (0 = cubemap de la escena)", Range(0, 1)) = 1
        _SpecularStrength ("Destello del sol", Float) = 1.2
        _Rugosidad ("Rugosidad del destello", Range(0.02, 0.5)) = 0.07
        _SSS ("Luz a traves de las crestas", Float) = 1.0
        _SSSColor ("Color de la luz a traves", Color) = (0.12, 0.55, 0.50, 1)

        [Header(Shore  bakeado del terreno)]
        _ShoreHeightMap ("Terrain Height Map (R = metros)", 2D) = "black" {}
        _ShoreMapOrigin ("Terrain Origin XZ", Vector) = (0, 0, 0, 0)
        _ShoreMapSize ("Terrain Size XZ", Vector) = (1000, 800, 0, 0)
        _WaveShoreFade ("Profundidad para ola completa", Float) = 3.5
        _EdgeFade ("Suavizado del borde de agua", Float) = 0.35
        _FarDepth ("Profundidad fuera del terreno", Float) = 30

        [Header(Teselacion  solo en GPUs que la soportan)]
        _TessMax ("Subdivision maxima", Range(1, 32)) = 16
        _TessDistancia ("Distancia de subdivision plena (m)", Float) = 30
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

    TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
    TEXTURE2D(_NormalMap2); SAMPLER(sampler_NormalMap2);
    TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
    TEXTURE2D_ARRAY(_CausticTex); SAMPLER(sampler_CausticTex);
    TEXTURE2D(_ShoreHeightMap); SAMPLER(sampler_ShoreHeightMap);
    TEXTURE2D(_ShoreDistMap); SAMPLER(sampler_ShoreDistMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _Absorcion;
        half4 _ColorDispersion, _FoamColor, _SSSColor;
        float _Turbidez, _Refraction, _Desaturation;
        float4 _NormalMap_ST, _FoamTex_ST;
        float _NormalScale, _NormalTiling, _NormalSpeed, _NormalLejos;
        float4 _WaveA, _WaveB, _WaveC, _WaveD;
        float _WaveSpeed, _WarpScale, _WarpStrength;
        float _RompienteAltura, _RompienteLargo, _RompientePeriodo, _RompienteZona, _RompienteAlcance;
        float _RompienteVariacion, _RompienteCurva, _RompienteGrupos, _RompienteInclinacion;
        float _ResacaAmplitud, _ResacaAlcance;
        float _FoamTiling, _EspumaRompiente, _EspumaEstela, _EspumaContraste, _ShoreFoamDistance, _CrestFoamHeight;
        float _CausticFrames, _CausticFPS, _CausticScale, _CausticStrength, _CausticMaxDepth;
        float _Smoothness, _ReflectionStrength, _ReflejoCielo, _SpecularStrength, _Rugosidad, _SSS;
        float4 _ShoreMapOrigin, _ShoreMapSize;
        float _WaveShoreFade, _EdgeFade, _FarDepth;
        float _TessMax, _TessDistancia;
    CBUFFER_END

    // Globales que publica RenderShaderURP (perspectiva aerea). Si el feature no
    // esta, quedan en cero y no modifican nada.
    float4 _AireColorGlobal;
    float4 _AireParamsGlobal;   // x = dist inicio, y = dist plena, z = tinte, w = desaturacion

    // Donde el frente de la ola alcanza la cresta, en fraccion de largo de onda,
    // cuando el tren se lee como marejada simetrica. Al romper baja hacia 0.24.
    #define CRESTA_SUAVE 0.42
    #define CRESTA_ROTA  0.24
    // La ola rompe cuando su altura pasa 0.78 veces la profundidad (criterio de McCowan).
    #define MCCOWAN 0.78

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
        float4 datos      : TEXCOORD4;   // x = eyeDepth, y = altura de ola, z = fog, w = profundidad en costa
        float2 restXZ     : TEXCOORD5;   // posicion sin desplazar: la espuma no debe deslizarse con la ola
        float4 rompiente  : TEXCOORD6;   // x = fase continua, y = cuanto rompio, z = amplitud, w = empinamiento
    };

    float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
    float Hash11(float n) { return frac(sin(n * 78.233) * 43758.5453); }
    float ValueNoise(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
    }

    float2 ShoreUV(float2 worldXZ) { return (worldXZ - _ShoreMapOrigin.xy) / max(_ShoreMapSize.xy, 1e-4); }

    // Profundidad del agua: nivel del mar (origen del objeto) menos la altura del
    // terreno bakeada. Fuera del mapa se mezcla hacia _FarDepth en una franja de
    // borde, para no dibujar una costura recta.
    float WaterDepthAt(float2 worldXZ, float seaLevel)
    {
        float2 uv = ShoreUV(worldXZ);
        float terrainH = SAMPLE_TEXTURE2D_LOD(_ShoreHeightMap, sampler_ShoreHeightMap, saturate(uv), 0).r;
        float dentro = seaLevel - terrainH;
        float2 dBorde = min(uv, 1 - uv);
        float borde = saturate(min(dBorde.x, dBorde.y) / 0.03);
        return lerp(_FarDepth, dentro, borde);
    }

    // Distancia a la costa (R, metros) y direccion hacia ella (GB). Sin mapa, o
    // fuera de el, la direccion es cero y las rompientes se apagan solas.
    float3 ShoreDistAt(float2 worldXZ)
    {
        float2 uv = ShoreUV(worldXZ);
        float3 s = SAMPLE_TEXTURE2D_LOD(_ShoreDistMap, sampler_ShoreDistMap, saturate(uv), 0).rgb;
        float2 dBorde = min(uv, 1 - uv);
        float dentro = saturate(min(dBorde.x, dBorde.y) / 0.03);
        return float3(s.r, s.gb * dentro);
    }

    // Irregularidad a lo largo de la costa: corre la fase para que las crestas no
    // entren como lineas paralelas perfectas. La comparten rompientes y resaca.
    float RuidoCosta(float2 worldXZ)
    {
        float2 p = worldXZ * _RompienteVariacion;
        return ValueNoise(p) * 0.65 + ValueNoise(p * 2.7 + 19.3) * 0.35;
    }

    // Resaca: la lengua de agua que sube por la playa despues de cada ola. Va con
    // el periodo de las rompientes y arranca cuando la cresta llega a la orilla.
    float ResacaOffset(float2 worldXZ, float depth)
    {
        float fase = _Time.y / max(_RompientePeriodo, 0.1) + RuidoCosta(worldXZ) * _RompienteCurva - CRESTA_ROTA;
        float f = frac(fase);
        // Entra rapido y escurre despacio, como el agua real por la arena.
        float onda = f < 0.3 ? smoothstep(0.0, 0.3, f) : 1.0 - smoothstep(0.3, 1.0, f);
        onda = onda * 2.0 - 1.0;
        float cerca = saturate(1.0 - depth / max(_ResacaAlcance, 0.01));
        return onda * _ResacaAmplitud * cerca * cerca;
    }

    // Forma del perfil de rompiente en funcion de s = fase dentro del largo de onda.
    // s = 0 es el valle del lado de la costa; el frente (lado costa) sube hasta la
    // cresta en s = c con una potencia alta, o sea casi vertical al final, y la
    // espalda baja suave por el resto del largo.
    float PerfilRompiente(float s, float c, float pf, out float dPds)
    {
        if (s < c)
        {
            float u = s / c;
            dPds = pf * pow(max(u, 1e-4), pf - 1) / c;
            return pow(u, pf);
        }
        float u = 1 - (s - c) / (1 - c);
        dPds = -6 * u * (1 - u) / (1 - c);
        return u * u * (3 - 2 * u);
    }

    // Olas de rompiente. Avanzan por la distancia bakeada a la costa, asi que las
    // crestas se doblan con la forma de la playa como en una costa real. Crecen al
    // entrar en lo bajo (asomeramiento, H ~ h^-1/4) hasta que la altura pasa
    // MCCOWAN * profundidad: ahi la ola rompe y queda limitada por el fondo.
    //   Devuelve el desplazamiento; grad = pendiente de la superficie (dh/dx, dh/dz).
    float3 Rompiente(float2 xz, float depth, float3 sd, out float2 grad, out float4 info)
    {
        grad = 0;
        info = 0;
        float dist = sd.x;
        float2 dir = sd.yz;
        float activo = saturate(length(dir) * 1.5);
        if (activo <= 0 || _RompienteAltura <= 0) return 0;
        dir = normalize(dir);

        float L = max(_RompienteLargo, 1);
        float fase = dist / L + _Time.y / max(_RompientePeriodo, 0.1) + RuidoCosta(xz) * _RompienteCurva;
        float idx = floor(fase);
        float s = fase - idx;

        // Cada ola trae su propio tamano: el mar llega en series, no en clones. Y
        // a lo largo de la cresta la altura cambia por tramos, asi que una seccion
        // rompe antes que la de al lado en vez de toda la linea a la vez.
        float tramo = ValueNoise(xz * 0.021 + idx * 3.7) * 0.7 + ValueNoise(xz * 0.057 - idx * 1.3) * 0.3;
        float grupo = lerp(1, (0.5 + 1.0 * Hash11(idx)) * (0.55 + 0.9 * tramo), _RompienteGrupos);

        float zona = max(_RompienteZona, 0.1);
        float env = (1 - smoothstep(zona * 0.5, zona, depth))
                  * (1 - smoothstep(_RompienteAlcance * 0.6, _RompienteAlcance, dist))
                  * saturate(depth / 0.12) * activo;

        float asomera = clamp(0.8 * pow(zona / max(depth, 0.3), 0.25), 0.6, 1.6);
        float H = _RompienteAltura * grupo * asomera;
        float Hmax = MCCOWAN * max(depth, 0);
        float roto = saturate((H - Hmax) / max(0.35 * H, 1e-3));
        float empinada = saturate(H / max(Hmax, 1e-3));
        float Hf = min(H, Hmax + 0.03);

        float c = lerp(CRESTA_SUAVE, CRESTA_ROTA, empinada);
        float pf = lerp(1.6, 4.5, empinada * empinada);
        float dPds;
        float p = PerfilRompiente(s, c, pf, dPds);

        float amp = Hf * env;
        // El nivel medio se resta con la amplitud sin la serie (sin 'grupo'): asi el
        // valle vale lo mismo a los dos lados del salto de indice de ola.
        float ampSerie = min(_RompienteAltura * asomera, Hmax + 0.03) * env;
        float y = amp * p - ampSerie * 0.35;

        // El labio de la ola se adelanta hacia la costa justo antes de romper.
        float2 lean = dir * amp * _RompienteInclinacion * p * p * p * empinada;

        // d(dist)/dxz = -dir, y ds = d(dist) / L.
        grad = amp * dPds * (-dir / L);
        info = float4(fase, roto, amp, empinada);
        return float3(lean.x, y, lean.y);
    }

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

    // Todo el trabajo por vertice. Lo llama el vertex shader sin teselar, o el
    // domain shader con cada vertice nuevo.
    Varyings BuildVaryings(float3 gridWS)
    {
        Varyings o;
        float seaLevel = unity_ObjectToWorld._m13;
        float depth = WaterDepthAt(gridWS.xz, seaLevel);
        depth += ResacaOffset(gridWS.xz, depth);

        // Marejada de Gerstner: se aplana en lo bajo, donde la reemplazan las rompientes.
        float waveFade = saturate((depth - 0.1) / max(_WaveShoreFade, 0.01));
        waveFade *= waveFade;

        // Deformacion de dominio en dos octavas: sin ella cuatro senoides se leen
        // como una celosia.
        float2 warpA = float2(ValueNoise(gridWS.xz * _WarpScale),
                              ValueNoise(gridWS.xz * _WarpScale + 37.1)) - 0.5;
        float2 warpB = float2(ValueNoise(gridWS.xz * _WarpScale * 4.3),
                              ValueNoise(gridWS.xz * _WarpScale * 4.3 + 11.7)) - 0.5;
        float3 muestraWS = gridWS;
        muestraWS.xz += warpA * _WarpStrength + warpB * (_WarpStrength * 0.42);

        float3 tangent = float3(1, 0, 0);
        float3 binormal = float3(0, 0, 1);
        float3 offset = 0;
        offset += Gerstner(_WaveA, muestraWS, tangent, binormal);
        offset += Gerstner(_WaveB, muestraWS, tangent, binormal);
        offset += Gerstner(_WaveC, muestraWS, tangent, binormal);
        offset += Gerstner(_WaveD, muestraWS, tangent, binormal);
        offset *= waveFade;
        tangent = lerp(float3(1, 0, 0), tangent, waveFade);
        binormal = lerp(float3(0, 0, 1), binormal, waveFade);

        float2 gradR;
        float4 info;
        float3 rompe = Rompiente(gridWS.xz, depth, ShoreDistAt(gridWS.xz), gradR, info);

        float3 p = gridWS + offset + rompe;

        // Las dos superficies se suman como campos de altura: se suman sus pendientes.
        float3 Ng = normalize(cross(binormal, tangent));
        float ny = max(Ng.y, 0.2);
        float dhdx = -Ng.x / ny + gradR.x;
        float dhdz = -Ng.z / ny + gradR.y;
        o.normalWS = normalize(float3(-dhdx, 1, -dhdz));
        o.tangentWS = normalize(float3(1, dhdx, 0));
        o.bitangentWS = normalize(float3(0, dhdz, 1));

        o.positionWS = p;
        o.positionCS = TransformWorldToHClip(p);
        o.datos = float4(-TransformWorldToView(p).z, offset.y + rompe.y, ComputeFogFactor(o.positionCS.z), depth);
        o.restXZ = gridWS.xz;
        o.rompiente = info;
        return o;
    }

    half4 frag(Varyings i) : SV_Target
    {
        float t = _Time.y;
        float eyeDepth = i.datos.x;
        float waveHeight = i.datos.y;
        float shoreDepth = i.datos.w;
        float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

        // Normales de detalle: tres capas a escalas y ejes distintos. Si dos capas
        // repiten en el mismo eje y a multiplo exacto, el tiling se ve.
        float lejos = lerp(1, _NormalLejos, saturate(eyeDepth / 400));
        float escalaN = _NormalScale * lejos;
        float2 uv1 = i.positionWS.xz * _NormalTiling + t * _NormalSpeed * float2(0.8, 0.6);
        const float2 ejeB = float2(0.819, 0.574);
        float2 xzB = float2(dot(i.positionWS.xz, ejeB), dot(i.positionWS.xz, float2(-ejeB.y, ejeB.x)));
        float2 uv2 = xzB * _NormalTiling * 2.37 + t * _NormalSpeed * float2(-0.5, 0.9);
        const float2 ejeC = float2(-0.423, 0.906);
        float2 xzC = float2(dot(i.positionWS.xz, ejeC), dot(i.positionWS.xz, float2(-ejeC.y, ejeC.x)));
        float2 uv3 = xzC * _NormalTiling * 0.29 + t * _NormalSpeed * 0.4 * float2(0.3, -0.7);
        float3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1), escalaN);
        float3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap2, sampler_NormalMap2, uv2), escalaN);
        float3 n3 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv3), escalaN * 0.8);
        float3 nTS = normalize(float3(n1.xy + n2.xy + n3.xy, n1.z * n2.z * n3.z));
        float3 N = normalize(i.tangentWS * nTS.x + i.bitangentWS * nTS.y + i.normalWS * nTS.z);

        // Con camara bajo el agua se ve la cara de abajo. No se usa SV_IsFrontFace
        // porque el orden de los triangulos teselados no es el de la malla.
        bool debajo = _WorldSpaceCameraPos.y < i.positionWS.y - 0.02;
        if (debajo) N = -N;

        float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
        float NdotV = saturate(dot(N, V));

        // Refraccion (rechaza muestras de objetos que estan delante del agua)
        float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
        float depth = max(sceneEye - eyeDepth, 0);
        float2 refrUV = screenUV + nTS.xy * _Refraction * saturate(depth * 0.5);
        float refrEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
        if (refrEye < eyeDepth) { refrUV = screenUV; refrEye = sceneEye; }
        float refrDepth = max(refrEye - eyeDepth, 0);
        half3 sceneColor = SampleSceneColor(refrUV);

        // Largo del rayo dentro del agua: la diferencia de eyeDepth es sobre el eje
        // de la camara, hay que llevarla al rayo de vista.
        float3 camFwd = -UNITY_MATRIX_V[2].xyz;
        float rayScale = 1.0 / max(dot(-V, camFwd), 0.1);
        float camino = min(refrDepth * rayScale, 500);

        Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
        float3 L = mainLight.direction;

        // Causticas sobre el fondo, en el punto donde el rayo lo toca.
        float3 floorWS = i.positionWS - V * camino;
        float frame = floor(fmod(t * _CausticFPS, max(_CausticFrames, 1)));
        float2 cuv = floorWS.xz * _CausticScale + nTS.xy * 0.05;
        float caustic = SAMPLE_TEXTURE2D_ARRAY(_CausticTex, sampler_CausticTex, cuv, frame).r;
        caustic *= SAMPLE_TEXTURE2D_ARRAY(_CausticTex, sampler_CausticTex, cuv * 0.63 + 0.37, fmod(frame + 11, max(_CausticFrames, 1))).r * 1.6;
        float causticFade = saturate(1 - refrDepth / max(_CausticMaxDepth, 0.01)) * saturate(refrDepth * 4);
        sceneColor += caustic * causticFade * _CausticStrength * mainLight.color * saturate(L.y) * mainLight.shadowAttenuation;

        // Transparencia fisica (Beer-Lambert): cada metro de agua se come mas rojo
        // que verde y azul. En lo bajo se ve el fondo casi sin tinte; en lo hondo
        // queda solo la luz que el agua dispersa hacia la camara.
        float3 extincion = _Absorcion.rgb + _Turbidez;
        float3 T = exp(-extincion * camino);
        half3 luzAgua = mainLight.color * (0.35 * saturate(L.y) * mainLight.shadowAttenuation) + SampleSH(float3(0, 1, 0));
        half3 col = sceneColor * T + _ColorDispersion.rgb * luzAgua * (1 - T);

        // Luz a traves de las crestas: mirando hacia el sol, lo alto de la ola es
        // lo bastante fino para dejar pasar verde.
        float alto = saturate(waveHeight / 1.2 + i.rompiente.z * 0.6);
        float contraluz = pow(saturate(dot(V, -L) * 0.6 + 0.4), 4);
        col += _SSSColor.rgb * mainLight.color * alto * (contraluz + 0.15) * _SSS * saturate(L.y + 0.2);

        // Reflejo con Fresnel de Schlick para agua (F0 = 0.02).
        float fresnel = 0.02 + 0.98 * pow(1 - NdotV, 5);
        if (debajo) fresnel = 0;
        // Un rayo reflejado que apunta bajo el horizonte chocaria con el mismo mar:
        // se lo levanta para que siempre lea cielo.
        float3 R = reflect(-V, N);
        R.y = max(R.y, 0.03);
        R = normalize(R);
        // El cubemap de la escena es el Default-Skybox de Unity (suelo marron) si la
        // iluminacion no se bakeo con Tenkoku. El ambiente SH si sigue al cielo real.
        half3 refl = lerp(GlossyEnvironmentReflection(R, 1 - _Smoothness, 1), SampleSH(R), _ReflejoCielo);
        col = lerp(col, refl, fresnel * _ReflectionStrength);

        // Destello del sol: GGX con rugosidad baja, que da el camino de brillos.
        float3 H = normalize(L + V);
        float NdotH = saturate(dot(N, H));
        float NdotL = saturate(dot(N, L));
        float a2 = _Rugosidad * _Rugosidad;
        a2 *= a2;
        float dGGX = a2 / (PI * pow(NdotH * NdotH * (a2 - 1) + 1, 2));
        float fH = 0.02 + 0.98 * pow(1 - saturate(dot(V, H)), 5);
        float spec = min(dGGX * fH * NdotL / max(4 * NdotV * NdotL, 0.05), 40);
        col += spec * mainLight.color * _SpecularStrength * mainLight.shadowAttenuation;

        #ifdef _ADDITIONAL_LIGHTS
        uint lightCount = GetAdditionalLightsCount();
        for (uint li = 0u; li < lightCount; li++)
        {
            Light extra = GetAdditionalLight(li, i.positionWS);
            float3 Hx = normalize(extra.direction + V);
            col += extra.color * extra.distanceAttenuation * (saturate(dot(N, extra.direction)) * 0.15
                 + pow(saturate(dot(N, Hx)), 200) * 0.6);
        }
        #endif

        float lum = dot(col, float3(0.299, 0.587, 0.114));
        col = lerp(col, lum.xxx, _Desaturation);

        // ---------- Espuma ----------
        // Textura como umbral: cuanta mas espuma, mas bajo el corte. Asi la espuma
        // que se disuelve no se vuelve transparente sino que se abre en encaje.
        float2 fuv = i.restXZ * _FoamTiling;
        float fA = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv + t * float2(0.010, 0.016)).r;
        float fB = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, fuv * 2.13 + float2(0.31, 0.77) - t * float2(0.014, 0.007)).g;
        float fC = ValueNoise(i.restXZ * 0.35 + t * 0.02);
        float patron = saturate(fA * 0.55 + fB * 0.45 + (fC - 0.5) * 0.3);

        // Rompiente: blanco en el frente que cae, y una estela detras de la cresta
        // que se apaga con el tiempo desde que paso la ola.
        float s = frac(i.rompiente.x);
        float roto = i.rompiente.y;
        float empinada = i.rompiente.w;
        float c = lerp(CRESTA_SUAVE, CRESTA_ROTA, empinada);
        float frente = smoothstep(c - 0.16, c - 0.03, s) * (1 - smoothstep(c + 0.01, c + 0.05, s));
        float edad = (s - c) / (1 - c);
        float estela = s > c ? exp(-edad / max(_EspumaEstela, 0.01)) : 0;
        float labio = smoothstep(0.8, 1.0, empinada) * (1 - roto) * frente * 0.6;
        float hayOla = saturate(i.rompiente.z * 4);
        float espRompe = (roto * (frente + estela * 0.75 + 0.04) + labio) * hayOla * _EspumaRompiente;

        // Borde de orilla y crestas de mar adentro (las de antes).
        float orilla = smoothstep(0, 1, 1 - saturate(depth / max(_ShoreFoamDistance, 0.01)));
        orilla *= saturate(shoreDepth / 0.12);
        float cresta = saturate((waveHeight - _CrestFoamHeight) * 1.2) * 0.8;

        float cantidad = saturate(max(max(espRompe, orilla * 0.9), cresta));
        float foam = saturate((cantidad - (1 - patron)) * _EspumaContraste + cantidad * cantidad * 0.12);
        foam *= _FoamColor.a;

        half3 foamCol = _FoamColor.rgb * (mainLight.color * mainLight.shadowAttenuation * (0.45 + 0.55 * saturate(dot(N, L)))
                      + SampleSH(float3(0, 1, 0)));
        col = lerp(col, foamCol, foam);

        col = MixFog(col, i.datos.z);

        // Perspectiva aerea del pase de estilo; va a mano porque el agua no escribe profundidad.
        float tAire = saturate((eyeDepth - _AireParamsGlobal.x) / max(_AireParamsGlobal.y - _AireParamsGlobal.x, 0.01));
        tAire = tAire * tAire * (3.0 - 2.0 * tAire);
        float lAire = dot(col, float3(0.299, 0.587, 0.114));
        col = lerp(col, lAire.xxx, tAire * _AireParamsGlobal.w);
        col = lerp(col, _AireColorGlobal.rgb, tAire * _AireParamsGlobal.z);

        // Los ultimos centimetros sobre la arena se desvanecen: la orilla no muestra
        // el borde de los triangulos.
        float alpha = saturate(shoreDepth / max(_EdgeFade, 0.01));
        return half4(col, alpha);
    }
    ENDHLSL

    // Con teselacion: la malla del oceano tiene un vertice cada 8 m, y una ola de
    // rompiente de 24 m necesita vertices cada medio metro cerca de la camara.
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
            #pragma target 4.6
            #pragma require tessellation tessHW
            #pragma vertex vertTess
            #pragma hull hull
            #pragma domain domain
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            struct TessCP
            {
                float3 positionWS : INTERNALTESSPOS;
            };

            struct TessFactors
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            TessCP vertTess(Attributes v)
            {
                TessCP o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }

            // Factor por arista, desde su punto medio: dos triangulos que comparten
            // una arista calculan el mismo numero y no se abren grietas. Inverso a la
            // distancia, o sea triangulos de tamano parecido en pantalla.
            float FactorArista(float3 a, float3 b)
            {
                float d = distance((a + b) * 0.5, _WorldSpaceCameraPos);
                return clamp(_TessMax * _TessDistancia / max(d, _TessDistancia), 1, _TessMax);
            }

            bool FueraDeCuadro(float3 a, float3 b, float3 c)
            {
                float4 pa = TransformWorldToHClip(a), pb = TransformWorldToHClip(b), pc = TransformWorldToHClip(c);
                // Margen amplio: las olas corren los vertices varios metros despues.
                float m = 1.35;
                bool afuera = (pa.x >  pa.w * m && pb.x >  pb.w * m && pc.x >  pc.w * m)
                           || (pa.x < -pa.w * m && pb.x < -pb.w * m && pc.x < -pc.w * m)
                           || (pa.y >  pa.w * m && pb.y >  pb.w * m && pc.y >  pc.w * m)
                           || (pa.y < -pa.w * m && pb.y < -pb.w * m && pc.y < -pc.w * m)
                           || (pa.w < -12 && pb.w < -12 && pc.w < -12);
                // Cerca de la camara el margen proporcional es chico: no se arriesga.
                float cerca = min(min(distance(a, _WorldSpaceCameraPos), distance(b, _WorldSpaceCameraPos)),
                                  distance(c, _WorldSpaceCameraPos));
                return afuera && cerca > 40;
            }

            TessFactors PatchConstantes(InputPatch<TessCP, 3> p)
            {
                TessFactors f;
                if (FueraDeCuadro(p[0].positionWS, p[1].positionWS, p[2].positionWS))
                {
                    f.edge[0] = f.edge[1] = f.edge[2] = f.inside = 0;
                    return f;
                }
                f.edge[0] = FactorArista(p[1].positionWS, p[2].positionWS);
                f.edge[1] = FactorArista(p[2].positionWS, p[0].positionWS);
                f.edge[2] = FactorArista(p[0].positionWS, p[1].positionWS);
                f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantes")]
            [outputcontrolpoints(3)]
            TessCP hull(InputPatch<TessCP, 3> p, uint id : SV_OutputControlPointID)
            {
                return p[id];
            }

            [domain("tri")]
            Varyings domain(TessFactors f, OutputPatch<TessCP, 3> p, float3 b : SV_DomainLocation)
            {
                float3 pos = p[0].positionWS * b.x + p[1].positionWS * b.y + p[2].positionWS * b.z;
                return BuildVaryings(pos);
            }
            ENDHLSL
        }
    }

    // Sin teselacion (moviles, GL viejo): mismas olas, pero las rompientes solo se
    // ven en la espuma y el sombreado, porque la malla no tiene resolucion.
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

            Varyings vert(Attributes v)
            {
                return BuildVaryings(TransformObjectToWorld(v.positionOS.xyz));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
