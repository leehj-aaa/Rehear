Shader "Hidden/Rehear/Scene View Glass Blur"
{
    Properties { _MainTex("Source", 2D) = "white" {} }
    SubShader { Tags { "RenderPipeline"="UniversalPipeline" }
        Pass { ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;float4 _MainTex_TexelSize;float2 _RehearBlurDirection;
            half4 frag(v2f_img i):SV_Target
            {
                float2 d=_MainTex_TexelSize.xy*_RehearBlurDirection;
                half4 c=tex2D(_MainTex,i.uv)*.227027027;
                c+=(tex2D(_MainTex,i.uv+d*1.384615385)+tex2D(_MainTex,i.uv-d*1.384615385))*.316216216;
                c+=(tex2D(_MainTex,i.uv+d*3.230769231)+tex2D(_MainTex,i.uv-d*3.230769231))*.070270270;
                return c;
            }
            ENDCG
        }
    }
}
