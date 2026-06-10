// Обводка силуэта спрайта (Built-in RP). Рисует ТОЛЬКО контур по краю непрозрачных пикселей
// заданным цветом — внутри силуэта прозрачен, поэтому оригинал спрайта (рисуется отдельным
// SpriteRenderer'ом сверху/рядом) не затрагивается. Используется EquipableGroundMarker для
// Diablo-подобной подсветки предметов, лежащих в мире.
//
// Принцип: alpha-dilation. Для каждого texel'а семплим альфу соседей в радиусе _OutlineWidth (px).
// Если вокруг есть непрозрачное, а сам пиксель прозрачный (around - self > 0) — это край → красим.
// Толщина в ПИКСЕЛЯХ ТЕКСТУРЫ (_MainTex_TexelSize), не в мире: визуально стабильна при разном scale.
Shader "Mmogick/SpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (1, 0.92, 0.4, 1)
        _OutlineWidth ("Outline Width (px)", Float) = 3
        _Alpha ("Alpha", Float) = 1
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _Alpha;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float self = tex2D(_MainTex, i.uv).a;

                float2 d = _MainTex_TexelSize.xy * _OutlineWidth;
                // 8 семплов вокруг (cross + диагонали) — достаточно для тонкого ровного контура.
                float around = 0;
                around = max(around, tex2D(_MainTex, i.uv + float2( d.x,    0)).a);
                around = max(around, tex2D(_MainTex, i.uv + float2(-d.x,    0)).a);
                around = max(around, tex2D(_MainTex, i.uv + float2(   0,  d.y)).a);
                around = max(around, tex2D(_MainTex, i.uv + float2(   0, -d.y)).a);
                around = max(around, tex2D(_MainTex, i.uv + float2( d.x,  d.y)).a);
                around = max(around, tex2D(_MainTex, i.uv + float2(-d.x,  d.y)).a);
                around = max(around, tex2D(_MainTex, i.uv + float2( d.x, -d.y)).a);
                around = max(around, tex2D(_MainTex, i.uv + float2(-d.x, -d.y)).a);

                // край = там, где сосед непрозрачный, а сам пиксель прозрачный
                float edge = saturate(around - self);

                fixed4 col = _OutlineColor;
                col.a = edge * _Alpha * _OutlineColor.a;
                return col;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
