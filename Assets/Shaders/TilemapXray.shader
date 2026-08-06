// Полупрозрачное «окно» в слоях карты, которые перекрывают сущность (кроны деревьев, крыши):
// сущность, зашедшая под такой слой, остаётся видна сквозь него.
//
// Слой перекрывает сущность ⟺ его порядок отрисовки больше порядка сущности (_LayerOrder > center.z).
// Порядок сущности — spawn_sort + серверный sort (UpdateController), то есть окно следует за
// этажностью: сервер поднял сущность на этаж (больший sort) — набор гасимых слоёв сузился сам.
//
// Центры окон — глобальный массив _XrayCenters (мировые xy, z = порядок сущности, w = радиус в
// клетках), заполняет TilemapXray каждый кадр. Гасится только то, что реально нарисовано: на
// открытом месте у перекрывающих слоёв в этой клетке тайлов нет, поэтому проплешины не видно.
Shader "Mmogick/TilemapXray"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        // Порядок отрисовки слоя — ставится на рендерер слоя через MaterialPropertyBlock
        // (материал общий на все слои, батчинг сохраняется).
        [PerRendererData] _LayerOrder ("Layer Order", Float) = 0
        _XrayMinAlpha ("Прозрачность в центре окна", Range(0,1)) = 0.35
        _XraySoftness ("Ширина мягкого края (клетки)", Float) = 0.8
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            // Держать синхронно с TilemapXray.MaxCenters — размер массива задаётся здесь,
            // C# шлёт ровно столько элементов (Unity фиксирует длину глобального массива).
            #define XRAY_MAX 16

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float2 world : TEXCOORD1;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _RendererColor;
            float _LayerOrder;

            float4 _XrayCenters[XRAY_MAX];
            int    _XrayCount;
            float  _XrayMinAlpha;
            float  _XraySoftness;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            // Множитель альфы пикселя: 1 вне окон, _XrayMinAlpha в центре ближайшего окна.
            // Из нескольких перекрывающихся окон берём самое сильное (min).
            float XrayAlpha(float2 world)
            {
                float a = 1;

                for (int k = 0; k < _XrayCount; k++)
                {
                    float4 c = _XrayCenters[k];

                    // слой ниже сущности либо на её уровне — он её не перекрывает, гасить нечего
                    if (_LayerOrder <= c.z)
                        continue;

                    float d = distance(world, c.xy);
                    a = min(a, lerp(_XrayMinAlpha, 1, smoothstep(c.w - _XraySoftness, c.w, d)));
                }

                return a;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.a *= XrayAlpha(i.world);
                c.rgb *= c.a;   // premultiplied — под Blend One OneMinusSrcAlpha
                return c;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
