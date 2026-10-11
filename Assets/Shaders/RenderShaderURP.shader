// Estilo general tipo Ghibli en espacio de pantalla para URP.
// Tres capas que se pueden apagar por separado desde el Renderer Feature:
//   1. Cuantización suave de luz: la luz se pinta en pocos valores, con el borde
//      entre ellos blando. Solo en los medios tonos.
//   2. Pigmento: manchas lentas de valor ancladas al mundo, como aguada sobre papel.
//   3. Perspectiva aérea: lo lejano palidece y se desatura hacia el color del aire.
// Pasada 0: el efecto.  Pasada 1: copia de vuelta al color de cámara.
Shader "Hidden/Terrenos/RenderShaderURP"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        float4 _AireColor;        // color del aire a la distancia
        float4 _AireParams;       // x = distancia de inicio, y = distancia de efecto pleno, z = tinte, w = desaturación
        float4 _PinturaParams;    // x = pasos de luz, y = suavidad del borde, z = fuerza
        float4 _PigmentoParams;   // x = escala (por metro), y = fuerza
        float4 _VegeParams;       // x = multiplicador de saturacion, y = matiz objetivo (grados), z = fuerza del corrimiento
        float4 _VegeRango;        // matices en grados: (sube desde, sube hasta, baja desde, baja hasta)
        float4 _ArenaParams;      // igual que _VegeParams pero para la arena
        float4 _ArenaRango;
        float4 _CieloParams;      // igual, pero para los azules: cielo y agua
        float4 _CieloRango;
        float _UsarMascara;       // 1 si RenderShaderURP armo la mascara de personajes
        TEXTURE2D_X(_MascaraPersonajes);

        static const float3 LUMA = float3(0.299, 0.587, 0.114);

        // URP TerrainLit ignora el diffuseRemap de las capas, asi que el color del
        // pasto no se puede corregir en el terreno: se corrige aca, por matiz.
        float3 RGBaHSV(float3 c)
        {
            float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
            float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
            float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
            float d = q.x - min(q.w, q.y);
            return float3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
        }

        float3 HSVaRGB(float3 c)
        {
            float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
            float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
            return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
        }

        // Sube la saturacion y corre el matiz de una banda de color, dejando el resto
        // intacto. Se usa tres veces: pasto, arena y azules (cielo y agua), que estan
        // en matices distintos y necesitan objetivos distintos.
        // par = (multiplicador de saturacion, matiz objetivo, fuerza del corrimiento, multiplicador de brillo)
        // La saturacion puede ser menor que 1: el altiplano necesita bajarla, no subirla.
        // mascara (0..1) limita donde actua la banda: las de suelo no tocan cielo ni agua.
        // techo: saturacion maxima a la que puede subir la banda (nunca baja lo que ya habia).
        void AjustarBanda(inout float3 hsv, float4 par, float4 rango, float mascara, float techo)
        {
            if (abs(par.x - 1.0) < 1e-4 && par.z <= 0.0 && abs(par.w - 1.0) < 1e-4) return;

            // El matiz es circular pero la ventana se escribe como un intervalo
            // creciente: sin esto, una ventana que cruza el 0 (los rojizos de la tierra,
            // que caen en 350-360 tanto como en 0-20) deja afuera media banda y el
            // suelo queda a dos colores. Se lleva el matiz al entorno del centro de la
            // ventana, que puede escribirse con extremos negativos: (-14, 2, 40, 52).
            float centro = (rango.y + rango.z) * 0.5;
            float grados = hsv.x * 360.0 - centro;
            grados -= 360.0 * floor(grados / 360.0 + 0.5);
            grados += centro;

            float peso = smoothstep(rango.x, rango.y, grados)
                       * (1.0 - smoothstep(rango.z, rango.w, grados))
                       * smoothstep(0.02, 0.10, hsv.y)
                       * mascara;
            hsv.y = min(saturate(hsv.y * lerp(1.0, par.x, peso)), max(hsv.y, techo));

            // Igual que arriba: de matiz 355 a matiz 33 el camino corto son 38 grados
            // hacia adelante, no 322 hacia atras pasando por el verde y el azul.
            float delta = par.y - hsv.x * 360.0;
            delta -= 360.0 * floor(delta / 360.0 + 0.5);
            hsv.x = frac(hsv.x + delta / 360.0 * peso * par.z);

            hsv.z = hsv.z * lerp(1.0, par.w, peso);
        }

        float GhibliHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

        float GhibliNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(GhibliHash(i), GhibliHash(i + float2(1, 0)), u.x),
                        lerp(GhibliHash(i + float2(0, 1)), GhibliHash(i + 1), u.x), u.y);
        }
        ENDHLSL

        Pass
        {
            Name "RenderShaderURP"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool esCielo = rawDepth < 0.000001;
                #else
                    bool esCielo = rawDepth > 0.999999;
                #endif

                // --- 1. Cuantización suave de luz ---
                // El efecto corre antes del tonemapping, así que la luminancia puede
                // pasar de 1. Se trabaja sobre lum/(1+lum), que comprime el rango a
                // 0..1 sin recortar: cuantizar directo en HDR apagaría el efecto en
                // todo lo que esté sobreexpuesto, que es media imagen a pleno sol.
                // Personajes y animales: ni cuantizacion ni bandas de color, que estan
                // calibradas para el paisaje (ver MascaraPersonajes.shader).
                float libre = 1.0;
                if (_UsarMascara > 0.5)
                    libre = 1.0 - SAMPLE_TEXTURE2D_X(_MascaraPersonajes, sampler_PointClamp, uv).r;

                float lum = dot(col, LUMA);
                float lt = lum / (1.0 + lum);

                float pasos = max(_PinturaParams.x, 1.0);
                float esc = lt * pasos;
                float piso = floor(esc);
                float resto = esc - piso;
                float borde = smoothstep(0.5 - _PinturaParams.y, 0.5 + _PinturaParams.y, resto);
                float ltQ = (piso + borde) / pasos;

                // Solo los medios: las sombras profundas y las luces quemadas se dejan
                // continuas, porque ahí el escalón se ve como error y no como pincelada.
                float peso = _PinturaParams.z * libre
                           * smoothstep(0.03, 0.20, lt)
                           * smoothstep(0.98, 0.80, lt);
                float ltN = lerp(lt, ltQ, peso);
                float lumN = ltN / max(1.0 - ltN, 1e-4);
                col *= lumN / max(lum, 1e-4);

                // --- Vegetacion, arena y azules ---
                // Tres bandas de matiz con objetivos propios, que no se pisan entre si.
                // El agua cae en la banda del cielo: es transparente y no
                // escribe profundidad, asi que para este pase es cielo. Es lo que se
                // quiere — el fiordo de Sur y el mar de Centro se pintan aca.
                //
                // Las bandas de arena y pasto solo actuan sobre suelo. Al atardecer el
                // resplandor del cielo y el reflejo del sol en el agua caen en el matiz
                // de la arena (40-50): saturarlos antes del tonemapping los llevaba a
                // (1,1,0) recortado, amarillo limon. No se puede cortar por brillo HDR:
                // a mediodia el suelo al sol ya pasa de 1 y la banda se apagaba entera.
                {
                    float3 hsv = RGBaHSV(col);
                    // Los techos cubren lo mismo sobre suelo: arena al sol rasante ya viene
                    // calida y x1.6 encima la moteaba de amarillo; el pasto x1.45 dejaba
                    // el 21% de las hojas recortadas en (1,1,0). Medido en Centro: techo
                    // 0.88 da pasto sat 0.35 sin recorte; 0.6 ya lo deja gris.
                    float suelo = esCielo ? 0.0 : libre;
                    AjustarBanda(hsv, _ArenaParams, _ArenaRango, suelo, 0.35);
                    AjustarBanda(hsv, _VegeParams, _VegeRango, suelo, 0.88);
                    AjustarBanda(hsv, _CieloParams, _CieloRango, libre, 1.0);
                    col = HSVaRGB(hsv);
                }

                if (!esCielo)
                {
                    float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                    // --- 2. Pigmento ---
                    // El ruido va en coordenadas de mundo, no de pantalla: en pantalla
                    // se movería con la cámara y leería como suciedad en el lente.
                    if (_PigmentoParams.y > 0.0)
                    {
                        float n = GhibliNoise(posWS.xz * _PigmentoParams.x) * 0.65
                                + GhibliNoise(posWS.xz * _PigmentoParams.x * 2.7) * 0.35;
                        col *= 1.0 + (n - 0.5) * 2.0 * _PigmentoParams.y;
                    }

                    // --- 3. Perspectiva aérea ---
                    // Los fondos de Ghibli pierden color y se van al tono del aire mucho
                    // antes de lo que haría una niebla física. Es lo que más separa un
                    // fondo pintado de un render con fog lineal.
                    float dist = LinearEyeDepth(rawDepth, _ZBufferParams);
                    float t = saturate((dist - _AireParams.x) / max(_AireParams.y - _AireParams.x, 0.01));
                    t = t * t * (3.0 - 2.0 * t);
                    float l2 = dot(col, LUMA);
                    col = lerp(col, l2.xxx, t * _AireParams.w);
                    col = lerp(col, _AireColor.rgb, t * _AireParams.z);
                }

                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Copia"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
