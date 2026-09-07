Shader "Rehear/UI/Button Outline Glow"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        _ButtonSize("Button size", Vector) = (340,56,0,0)
        _Padding("Glow padding", Float) = 24
        _GlowWidth("Glow spread", Float) = 10
        _OutlineWidth("Outline width", Float) = 2
        _GlowStrength("Glow strength", Range(0,1)) = 0.85
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            float4 _ButtonSize, _ClipRect;
            float _Padding, _GlowWidth, _OutlineWidth, _GlowStrength;
            struct Input
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Output
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float4 localPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Output vert(Input input)
            {
                Output output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.localPosition = input.vertex;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 frag(Output input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 size = max(_ButtonSize.xy, 1);
                float radius = min(size.x, size.y) * 0.5;
                float2 p = (input.uv - 0.5) * (size + 2 * _Padding);
                float2 q = abs(p) - size * 0.5 + radius;
                float d = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
                float aa = max(fwidth(d), 0.35);
                half ring = 1 - smoothstep(_OutlineWidth - aa, _OutlineWidth + aa, abs(d));
                float spread = max(_GlowWidth, 1);
                float falloff = max(d, 0) / spread;
                half halo = exp2(-3 * falloff * falloff);
                // No fill over the button or its label; fade smoothly before the mesh edge.
                half outside = smoothstep(-aa, aa, d);
                halo *= outside * (1 - smoothstep(_Padding * 0.7, _Padding, max(d, 0)));
                half alpha = saturate(ring * 0.8 + halo * 0.55) * _GlowStrength * input.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif
                half3 color = lerp(input.color.rgb, half3(0.65, 0.9, 1), ring * 0.7);
                return half4(color, alpha);
            }
            ENDCG
        }
    }
}
