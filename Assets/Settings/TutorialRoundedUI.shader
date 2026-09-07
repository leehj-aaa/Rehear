Shader "Rehear/UI/Rounded Translucent Panel"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _PanelSize("Panel size", Vector) = (1200,866,0,0)
        _Radius("Corner radius", Float) = 48
        _BorderWidth("White border", Float) = 2
        _UseBlur("Use background blur", Float) = 1
        _GlassTint("White glass tint", Range(0,1)) = 0.45
        _FillAlphaOffset("Fill alpha offset", Range(0,1)) = 0
        [HideInInspector] _BlurTex("Background blur", 2D) = "black" {}
        [HideInInspector] _CropRegion("Blur crop", Vector) = (0,0,1,1)
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [HideInInspector] _Color("Color", Color) = (1,1,1,1)
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment PanelFragment
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            sampler2D _MainTex;
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_BlurTex);
            float4 _CropRegion, _ClipRect;
            fixed4 _TextureSampleAdd;
            float4 _PanelSize;
            float _Radius, _BorderWidth, _UseBlur, _GlassTint, _FillAlphaOffset;
            struct VertexInput
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct VertexOutput
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 localPosition : TEXCOORD1;
                float4 screenPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            VertexOutput vert(VertexInput input)
            {
                VertexOutput output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.localPosition = input.vertex;
                output.color = input.color;
                output.uv = input.uv;
                output.screenPosition = ComputeNonStereoScreenPos(output.vertex);
                #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x > 0 && unity_MatrixVP[1][1] < 0)
                    output.screenPosition.y = output.screenPosition.w - output.screenPosition.y;
                #endif
                return output;
            }
            half4 PanelFragment(VertexOutput input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 foreground = (tex2D(_MainTex, input.uv) + _TextureSampleAdd) * input.color;
                foreground.a = max(0, foreground.a - _FillAlphaOffset);
                float radius = min(_Radius, min(_PanelSize.x, _PanelSize.y) * 0.5);
                float2 q = abs((input.uv - 0.5) * _PanelSize.xy) - _PanelSize.xy * 0.5 + radius;
                float distance = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
                float aa = max(fwidth(distance), 0.35);
                half coverage = 1 - smoothstep(-aa, aa, distance);
                half edge = _BorderWidth > 0 ? smoothstep(-_BorderWidth-aa, -_BorderWidth+aa, distance) : 0;
                half4 result = foreground;
                if (_UseBlur > 0.5)
                {
                    float2 screenUV = input.screenPosition.xy / input.screenPosition.w;
                    float2 blurUV = (screenUV - _CropRegion.xy) / max(_CropRegion.zw - _CropRegion.xy, 0.0001);
                    half3 background = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_BlurTex, UnityStereoTransformScreenSpaceTex(blurUV)).rgb;
                    result.rgb = lerp(background, foreground.rgb, _GlassTint);
                }
                result.rgb = lerp(result.rgb, half3(1,1,1), edge);
                result.a = lerp(result.a, 1, edge) * coverage;
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                result.rgb *= result.a;
                return result;
            }
            ENDCG
        }
    }
}
