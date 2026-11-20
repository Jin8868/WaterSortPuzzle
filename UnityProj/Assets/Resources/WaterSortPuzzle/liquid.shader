Shader "WaterSortPuzzle/Liquid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Bottle Interior Mask", 2D) = "white" {}
        _FillAmount ("Filled Layers", Range(0, 4)) = 0
        _Color1 ("Layer 1", Color) = (0,0,0,0)
        _Color2 ("Layer 2", Color) = (0,0,0,0)
        _Color3 ("Layer 3", Color) = (0,0,0,0)
        _Color4 ("Layer 4", Color) = (0,0,0,0)
        _SurfaceColor ("Surface Color", Color) = (0,0,0,0)
        _TexLayerCount ("Hidden Layers", Float) = 0
        _TexLayerSwitchProgress ("Reveal Progress", Range(0, 1)) = 0
        _WaveAmplitude ("Wave Amplitude", Float) = 0
        _EllipseHeight ("Surface Highlight Height", Float) = 0.02
        _Rotation ("Bottle Rotation", Float) = 0

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="False"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct VertexInput
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct VertexOutput
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 position : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _ClipRect;
            float _FillAmount;
            half4 _Color1;
            half4 _Color2;
            half4 _Color3;
            half4 _Color4;
            half4 _SurfaceColor;
            float _TexLayerCount;
            float _TexLayerSwitchProgress;
            float _WaveAmplitude;
            float _EllipseHeight;
            float _Rotation;

            static const float WidthRatio = 132.0 / 380.0;
            static const float BottomY = -0.45;
            static const float LayerHeight = 0.845 / 4.0;

            VertexOutput Vert(VertexInput input)
            {
                VertexOutput output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                output.uv = input.uv;
                output.position = input.vertex.xy;
                return output;
            }

            float SegmentDistance(float2 position, float2 start, float2 finish)
            {
                float2 segment = finish - start;
                float progress = saturate(dot(position - start, segment) / dot(segment, segment));
                return length(position - start - segment * progress);
            }

            float QuestionMark(float2 position)
            {
                // 用圆弧、短线和圆点描画每层的问号。
                float2 arc = position - float2(0.0, 0.17);
                float stroke = abs(length(arc) - 0.19);
                float arcMask = step(-0.18, arc.y) * (1.0 - step(arc.x, 0.0) * step(arc.y, 0.0));
                stroke = lerp(1.0, stroke, arcMask);
                stroke = min(stroke, SegmentDistance(position, float2(0.12, 0.02), float2(0.0, -0.11)));
                stroke = min(stroke, SegmentDistance(position, float2(0.0, -0.11), float2(0.0, -0.19)));
                stroke = min(stroke, length(position - float2(0.0, -0.34)));
                float edge = max(fwidth(stroke), 0.001);
                return 1.0 - smoothstep(0.05 - edge, 0.05 + edge, stroke);
            }

            half4 Frag(VertexOutput input) : SV_Target
            {
                // 使用与实际宽高比一致的坐标计算重力方向，轮廓仍采样原始纹理。
                float2 local = (input.uv - 0.5) * float2(WidthRatio, 1.0);
                float sine;
                float cosine;
                sincos(radians(_Rotation), sine, cosine);
                float2 horizontal = float2(
                    local.x * cosine - local.y * sine,
                    local.x * sine + local.y * cosine);
                float surface = BottomY + _FillAmount * LayerHeight;
                surface += sin(horizontal.x * 65.0 + _Time.y * 3.0) * _WaveAmplitude;
                float edge = max(fwidth(horizontal.y), 0.0001);
                float fill = (1.0 - smoothstep(surface - edge, surface + edge, horizontal.y));
                fill *= step(0.001, _FillAmount);

                // 用连续液面裁剪每一层，并保留各层的颜色与最上方高光。
                float layerPosition = (horizontal.y - BottomY) / LayerHeight;
                float layer = clamp(floor(layerPosition), 0.0, 3.0);
                half4 color = layer == 0.0 ? _Color1 : layer == 1.0 ? _Color2 : layer == 2.0 ? _Color3 : _Color4;
                float highlight = smoothstep(surface - _EllipseHeight - edge, surface - _EllipseHeight, horizontal.y);
                highlight *= step(0.001, _EllipseHeight);
                color.rgb = lerp(color.rgb, _SurfaceColor.rgb, highlight * _SurfaceColor.a);

                // 只在最上方隐藏层应用揭晓进度，其余隐藏层保持问号。
                float hidden = 1.0 - step(_TexLayerCount, layer);
                float reveal = step(_TexLayerCount - 1.0, layer) * _TexLayerSwitchProgress;
                float2 symbol = float2(horizontal.x / LayerHeight, layerPosition - layer - 0.5);
                half3 hiddenColor = lerp(half3(0.25, 0.28, 0.31), half3(0.95, 0.97, 1.0), QuestionMark(symbol));
                color.rgb = lerp(color.rgb, hiddenColor, hidden * (1.0 - reveal));

                color *= input.color;
                color.a *= fill * tex2D(_MainTex, input.uv).a * 0.88;
                #ifdef UNITY_UI_CLIP_RECT
                    color.a *= UnityGet2DClipping(input.position, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
