Shader "Rehear/Skybox/Opening Blue Silk"
{
    Properties
    {
        _MainTex("Blue Wave Artwork", 2D) = "black" {}
        _Exposure("Brightness", Range(0.5, 1.5)) = 1
        _MotionAmount("Ribbon Travel", Range(0, 0.06)) = 0.038
        _Speed("Ribbon Flow Speed", Range(0, 1)) = 0.6
        _Rotation("Fixed Heading", Range(0, 360)) = 0
        [HideInInspector] _Procedural("Resolution Independent Curves", Range(0,1)) = 0
        [HideInInspector] _PreviewTime("Preview Time", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Exposure, _MotionAmount, _Speed, _Rotation, _PreviewTime, _Procedural;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z=UNITY_RAW_FAR_CLIP_VALUE*output.positionCS.w;
                output.direction=input.positionOS.xyz;
                return output;
            }
            float Hermite(float x,float x0,float x1,float y0,float y1,float m0,float m1)
            {
                float u=saturate((x-x0)/(x1-x0)); float u2=u*u; float u3=u2*u;
                return (2*u3-3*u2+1)*y0+(u3-2*u2+u)*m0*(x1-x0)
                    +(-2*u3+3*u2)*y1+(u3-u2)*m1*(x1-x0);
            }
            float Line(float d,float width) { float v=d/max(width,0.0001); return exp2(-1.442695*v*v); }
            half3 Silk(float2 uv)
            {
                float x=uv.x;
                float curve=x<.30 ? Hermite(x,0,.30,.19,.393,1.15,0) :
                    x<.62 ? Hermite(x,.30,.62,.393,.285,0,0) : Hermite(x,.62,1,.285,.84,0,1.0);
                float d=uv.y-curve;
                float width=.0025+.0025*Line(x-.73,.16);
                // Pixel footprint softens sub-pixel cores without blurring the whole ribbon.
                float aa=max(fwidth(d),.0001);
                float core=Line(d,max(width,aa*.75));
                float glow=Line(d,.035);
                float broad=Line(d,.15);
                float body=(1-smoothstep(-.19,.012,d))*Line(d+.09,.24);
                half3 top=half3(.00015,.00035,.004);
                half3 bottom=half3(.0008,.007,.085);
                half3 color=lerp(bottom,top,smoothstep(0,1,uv.y));
                color+=half3(.0003,.004,.035)*Line(uv.y-.42,.38);
                color+=half3(0,.018,.40)*(broad*.32+body*.52);
                color+=half3(.001,.075,.8)*glow*.48;
                color+=half3(.42,.76,1)*core*.85;
                color+=half3(.26,.34,.38)*Line(d,max(width*.32,aa*.55))*.75;
                float back=Line(d-(.012+.008*sin(x*9)),.008);
                color+=half3(0,.038,.40)*back*.30;
                float secondary=.12+.10*Line(x-.72,.17)-.60*(x-.75)*(x-.75);
                float d2=uv.y-secondary;
                float strength=smoothstep(.32,.75,x);
                color+=half3(0,.024,.50)*Line(d2,.055)*strength*.35;
                color+=half3(.12,.40,.85)*Line(d2,max(.0018,aa)) * strength*.48;
                return color;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir=normalize(input.direction);
                float a=atan2(dir.x,dir.z)+radians(_Rotation);
                // A smooth mirrored wrap joins the non-panoramic artwork without a rear seam.
                float2 uv=float2(0.5+0.5*sin(a),0.5+asin(clamp(dir.y,-1.0,1.0))/1.78);
                float t=(_PreviewTime>=0?_PreviewTime:_Time.y)*_Speed;
                float edge=16*uv.x*(1-uv.x)*saturate(uv.y)*saturate(1-uv.y);
                // Deform the original artwork locally: keep its variable-width highlights,
                // with a visible travelling wave instead of rotating the VR environment.
                float2 ripple=float2(sin(uv.y*6.0+t)*0.35,
                    sin(uv.x*7.0-t)*0.7+sin(uv.x*3.0+t*0.63)*0.3);
                uv+=ripple*(_MotionAmount*edge);
                half3 color;
                color=SAMPLE_TEXTURE2D_BIAS(_MainTex,sampler_MainTex,saturate(uv),-0.35).rgb;
                // Blend the poles to navy: no pinching or stretched bright streaks overhead.
                float fade=smoothstep(0,0.12,uv.y)*(1-smoothstep(0.90,1.0,uv.y));
                half3 navy=half3(0.0004,0.0010,0.010);
                color=lerp(navy,color,fade);
                color*=(_Exposure*(1+0.015*sin(t*0.65+uv.x*5)));
                return half4(color,1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
