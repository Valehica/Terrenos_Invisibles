// Viento de los coigues y cipreses de Sur (shader Terrenos/ArbolViento, que es URP/Lit con esto en el vertice).
//
// Que tanto se mueve cada vertice viene horneado en el color de vertice al importar el FBX
// (Assets/Editor/ArbolesCoigueCipresSur.cs):
//   r = altura sobre la base del tronco / 40 m   (la flexion crece con r^2: la base no se mueve)
//   g = 1 en las hojas, 0 en la corteza          (solo las hojas aletean)
//   b = fase al azar por tarjeta de hojas        (para que no aleteen todas juntas)
//   a = distancia al eje del tronco / 15 m       (las puntas de la copa aletean mas)
//
// La intensidad es global (no del material, para no romper el SRP Batcher): la pone VientoArboles.cs.
// Sin ese componente en la escena todo vale 0 y los arboles quedan quietos.
//
// La fase de la oscilacion depende de la posicion en el mundo a lo largo del viento: son "olas" que cruzan el
// bosque, asi arboles vecinos se mueven parecido pero no igual, y el tronco y sus hojas (que estan en el mismo
// lugar) se mueven juntos sin datos por arbol.
#ifndef TERRENOS_ARBOL_VIENTO_INCLUDED
#define TERRENOS_ARBOL_VIENTO_INCLUDED

float4 _VientoDireccion;    // xy = direccion horizontal normalizada (x, z del mundo)
float4 _VientoParametros;   // x = flexion (m en la punta de un arbol de 40 m), y = velocidad, z = aleteo (m), w = escala de las olas (1/m)

float3 DesplazamientoViento(float3 posWS, float3 normalWS, float4 datos)
{
    float2 dir = _VientoDireccion.xy;
    float t = _Time.y * _VientoParametros.y;
    float s = dot(posWS.xz, dir) * _VientoParametros.w;

    // Rafagas lentas que pasan (empujan el arbol a favor del viento) y un vaiven encima.
    float rafaga = 0.55 + 0.45 * sin(t * 0.31 - s * 0.6);
    float vaiven = sin(t - s) * 0.55 + sin(t * 2.17 - s * 1.9 + 1.3) * 0.2;
    float flexion = datos.r * datos.r * _VientoParametros.x * rafaga * (1.0 + vaiven);
    float3 d = float3(dir.x, 0.0, dir.y) * flexion;
    d.y -= flexion * flexion * 0.02;   // al doblarse la copa baja un poco

    // Aleteo de las hojas: rapido, con fase por tarjeta, mas fuerte con rafaga.
    float aleteo = sin(t * 5.3 + datos.b * 6.2832 + s * 4.0) + 0.5 * sin(t * 8.9 + datos.b * 17.0);
    d += (normalWS * 0.6 + float3(0.0, 0.4, 0.0)) * aleteo * datos.g * (0.4 + datos.a) * rafaga * _VientoParametros.z;
    return d;
}

float3 PosicionConViento(float3 positionOS, float3 normalOS, float4 datos)
{
    float3 posWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);
    posWS += DesplazamientoViento(posWS, normalWS, datos);
    return TransformWorldToObject(posWS);
}

// ---------------------------------------------------------------- envolturas de los vertex de URP/Lit
// Cada pase de URP declara su propio Attributes sin color de vertice; estas versiones lo agregan, mueven la
// posicion y llaman a la funcion original.

#if defined(UNIVERSAL_FORWARD_LIT_PASS_INCLUDED) || defined(UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED)
struct AttributesViento
{
    float4 positionOS        : POSITION;
    float3 normalOS          : NORMAL;
    float4 tangentOS         : TANGENT;
    float2 texcoord          : TEXCOORD0;
    float2 staticLightmapUV  : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    float4 color             : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Attributes ConViento(AttributesViento v)
{
    Attributes a = (Attributes)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, a);
    a.positionOS = float4(PosicionConViento(v.positionOS.xyz, v.normalOS, v.color), 1.0);
    a.normalOS = v.normalOS;
    a.tangentOS = v.tangentOS;
    a.texcoord = v.texcoord;
    a.staticLightmapUV = v.staticLightmapUV;
    a.dynamicLightmapUV = v.dynamicLightmapUV;
    return a;
}
#endif

#if defined(UNIVERSAL_FORWARD_LIT_PASS_INCLUDED)
Varyings LitPassVertexViento(AttributesViento v) { return LitPassVertex(ConViento(v)); }
#endif

#if defined(UNIVERSAL_LIT_GBUFFER_PASS_INCLUDED)
Varyings LitGBufferPassVertexViento(AttributesViento v) { return LitGBufferPassVertex(ConViento(v)); }
#endif

#if defined(UNIVERSAL_SHADOW_CASTER_PASS_INCLUDED)
struct AttributesViento
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    float4 color      : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings ShadowPassVertexViento(AttributesViento v)
{
    Attributes a = (Attributes)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, a);
    a.positionOS = float4(PosicionConViento(v.positionOS.xyz, v.normalOS, v.color), 1.0);
    a.normalOS = v.normalOS;
    a.texcoord = v.texcoord;
    return ShadowPassVertex(a);
}
#endif

#if defined(UNIVERSAL_DEPTH_ONLY_PASS_INCLUDED)
struct AttributesViento
{
    float4 position : POSITION;
    float3 normalOS : NORMAL;
    float2 texcoord : TEXCOORD0;
    float4 color    : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings DepthOnlyVertexViento(AttributesViento v)
{
    Attributes a = (Attributes)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, a);
    a.position = float4(PosicionConViento(v.position.xyz, v.normalOS, v.color), 1.0);
    a.texcoord = v.texcoord;
    return DepthOnlyVertex(a);
}
#endif

#if defined(UNIVERSAL_FORWARD_LIT_DEPTH_NORMALS_PASS_INCLUDED)
struct AttributesViento
{
    float4 positionOS : POSITION;
    float4 tangentOS  : TANGENT;
    float2 texcoord   : TEXCOORD0;
    float3 normal     : NORMAL;
    float4 color      : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings DepthNormalsVertexViento(AttributesViento v)
{
    Attributes a = (Attributes)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, a);
    a.positionOS = float4(PosicionConViento(v.positionOS.xyz, v.normal, v.color), 1.0);
    a.tangentOS = v.tangentOS;
    a.texcoord = v.texcoord;
    a.normal = v.normal;
    return DepthNormalsVertex(a);
}
#endif

#endif
