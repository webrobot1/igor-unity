// Полупрозрачное «окно» в слоях карты, которые перекрывают своего игрока (кроны деревьев, крыши,
// верхние этажи): игрок, зашедший под такой слой, остаётся виден сквозь него.
//
// Слой перекрывает игрока ⟺ его порядок отрисовки больше порядка игрока (_LayerOrder > center.z).
// Порядок игрока — земля его этажа (UpdateController), то есть окно следует за этажностью: игрок
// поднялся на этаж — набор гасимых слоёв сузился сам.
//
// Центр окна — глобальный вектор _XrayCenter (мировые xy, z = порядок своего игрока, w = радиус в
// клетках; 0 — окна нет), пишет TilemapXray каждый кадр. Гасится только то, что реально нарисовано: на
// открытом месте у перекрывающих слоёв в этой клетке тайлов нет, поэтому проплешины не видно.
Shader "Mmogick/TilemapXray"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        // Порядок отрисовки слоя — пишется в экземпляр материала, общий на пару «порядок и оттенок»
        // (TilemapXray.Instance); почему не MaterialPropertyBlock — там же.
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

            float4 _XrayCenter;
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

            // Множитель альфы пикселя: 1 вне окна, _XrayMinAlpha в его центре.
            float XrayAlpha(float2 world)
            {
                float4 c = _XrayCenter;

                // окна нет либо слой ниже игрока или на его уровне — он игрока не перекрывает, гасить нечего
                if (c.w <= 0 || _LayerOrder <= c.z)
                    return 1;

                return lerp(_XrayMinAlpha, 1, smoothstep(c.w - _XraySoftness, c.w, distance(world, c.xy)));
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
