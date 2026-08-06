Shader "Custom/Animated Blue Wave Skybox"
{
    Properties
    {
        [HDR]_TopColor("Top Color", Color) = (0.0005, 0.0010, 0.0180, 1)
        [HDR]_MidColor("Middle Color", Color) = (0.0015, 0.0100, 0.1000, 1)
        [HDR]_BottomColor("Bottom Color", Color) = (0.0020, 0.0240, 0.2200, 1)

        [HDR]_BlueGlow("Blue Glow", Color) = (0.00, 0.10, 1.25, 1)
        [HDR]_CyanGlow("Cyan Glow", Color) = (0.02, 0.58, 2.40, 1)
        [HDR]_WhiteCore("White Core", Color) = (1.45, 1.85, 2.30, 1)

        _Horizon("Wave Vertical Position", Range(-0.65, 0.35)) = -0.34
        _WaveScale("Wave Height", Range(0.1, 1.5)) = 0.38
        _CoreWidth("Core Width", Range(0.002, 0.08)) = 0.0055
        _GlowWidth("Glow Width", Range(0.02, 0.35)) = 0.024

        _Speed("Motion Speed", Range(0, 1.5)) = 0.12
        _Breath("Breathing Amount", Range(0, 1.0)) = 0.12
        _FlowAmount("Horizontal Flow Amount", Range(0, 1.0)) = 0.045
        _VerticalMotion("Vertical Motion Amount", Range(0, 0.5)) = 0.026

        _SecondaryStrength("Secondary Wave", Range(0, 2)) = 0.78
        _BackgroundStrength("Background Strength", Range(0.05, 1.0)) = 0.36
        _GlowIntensity("Glow Intensity", Range(0, 2)) = 0.96
        _WhiteIntensity("White Core Intensity", Range(0, 1)) = 0.10
        _Exposure("Exposure", Range(0.2, 3)) = 0.90

        _SeamBlend("Seam Blend", Range(0.02, 0.5)) = 0.14
    }

    SubShader
    {
        Tags
        {
            // Draw before all scene geometry, including XR controller/hand meshes.
            "Queue"="Background-100"
            "RenderType"="Background"
            "RenderPipeline"="UniversalPipeline"
            "PreviewType"="Skybox"
            "ForceNoShadowCasting"="True"
            "IgnoreProjector"="True"
        }

        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend Off
        ColorMask RGB

        Pass
        {
            Name "ProceduralBlueWaveSky"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _MidColor;
                float4 _BottomColor;

                float4 _BlueGlow;
                float4 _CyanGlow;
                float4 _WhiteCore;

                float _Horizon;
                float _WaveScale;
                float _CoreWidth;
                float _GlowWidth;

                float _Speed;
                float _Breath;
                float _FlowAmount;
                float _VerticalMotion;

                float _SecondaryStrength;
                float _BackgroundStrength;
                float _GlowIntensity;
                float _WhiteIntensity;
                float _Exposure;
                float _SeamBlend;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);

                // Always place the sky at the platform-specific far plane.
                // Using z = w directly can produce an incorrect depth on
                // reversed-Z platforms and allow the sky pass to cover 3D geometry.
                output.positionCS.z =
                    UNITY_RAW_FAR_CLIP_VALUE * output.positionCS.w;

                output.directionOS = input.positionOS.xyz;

                return output;
            }

            float GaussianLine(float y, float center, float width)
            {
                float d = (y - center) / max(width, 0.0001);
                return exp(-d * d);
            }

            float SeamMask(float a)
            {
                float edgeStart = 3.14159265 - max(_SeamBlend, 0.001);
                return smoothstep(edgeStart, 3.14159265, abs(a));
            }

            float BlendAtSeam(float a, float rawValue, float edgeValue)
            {
                return lerp(rawValue, edgeValue, SeamMask(a));
            }

            float MainWaveRaw(float a, float t)
            {
                float aFlow =
                    a
                    + sin(t * 0.45) * _FlowAmount
                    + sin(2.0 * a - t * 0.55) * _FlowAmount * 0.16;

                float largeShape =
                      0.095 * sin(aFlow + 1.25)
                    - 0.040 * sin(2.0 * aFlow - 0.10);

                float centerDip =
                    -0.085 * exp(-pow((aFlow - 0.18) / 0.52, 2.0));

                float rightRise =
                    0.25 * smoothstep(0.42, 1.70, aFlow);

                    float smallCurves =
                    0.060 * sin(3.0 * aFlow + 0.70)
                  - 0.044 * sin(4.0 * aFlow - 0.30)
                  + 0.034 * sin(5.0 * aFlow + 0.25)
                  - 0.024 * sin(6.0 * aFlow - 0.80)
                  + 0.018 * sin(7.0 * aFlow + 0.35)
                  - 0.012 * sin(8.0 * aFlow - 0.55);

                float motion =
                      sin(t * 1.15 + a) * _VerticalMotion
                    + sin(t * 0.80 - 2.0 * a) * _VerticalMotion * 0.55
                    + sin(t * 1.60 + 3.0 * a) * _VerticalMotion * 0.34
                    + sin(t * 2.10 - 4.0 * a) * _VerticalMotion * 0.20
                    + sin(t * 1.30 + a) * _Breath * 0.008;

                return _Horizon
                    + (largeShape + centerDip + rightRise + smallCurves) * _WaveScale
                    + motion;
            }

            float MainWave(float a, float t)
            {
                float rawValue = MainWaveRaw(a, t);

                float plusEdge = MainWaveRaw(3.14159265, t);
                float minusEdge = MainWaveRaw(-3.14159265, t);
                float sharedEdge = (plusEdge + minusEdge) * 0.5;

                return BlendAtSeam(a, rawValue, sharedEdge);
            }

            float SecondaryWaveRaw(float a, float t)
            {
                float aFlow =
                    a
                    - sin(t * 0.35) * _FlowAmount * 0.45;

                return _Horizon - 0.27
                    + 0.11 * sin(aFlow - 0.70 + t * 0.42)
                    - 0.042 * sin(2.0 * aFlow + 0.05 - t * 0.25)
                    + 0.020 * cos(2.7 * aFlow + 0.45)
                    + sin(t * 0.90 + a * 0.75) * _VerticalMotion * 0.20;
            }

            float SecondaryWave(float a, float t)
            {
                float rawValue = SecondaryWaveRaw(a, t);

                float plusEdge = SecondaryWaveRaw(3.14159265, t);
                float minusEdge = SecondaryWaveRaw(-3.14159265, t);
                float sharedEdge = (plusEdge + minusEdge) * 0.5;

                return BlendAtSeam(a, rawValue, sharedEdge);
            }

            float MainWidthProfileRaw(float a, float t)
            {
                float leftCrest =
                    0.12 * exp(-pow((a + 1.55) / 0.65, 2.0));

                float centerTight =
                    -0.30 * exp(-pow((a - 0.12) / 0.38, 2.0));

                float bendThicken =
                    0.52 * exp(-pow((a - 0.92) / 0.32, 2.0));

                float rightTaper =
                    -0.18 * smoothstep(1.70, 2.45, a);

                float micro =
                    0.035 * sin(3.0 * a - t * 0.30);

                return clamp(
                    0.58
                    + leftCrest
                    + centerTight
                    + bendThicken
                    + rightTaper
                    + micro,
                    0.30,
                    1.20
                );
            }

            float MainWidthProfile(float a, float t)
            {
                float rawValue = MainWidthProfileRaw(a, t);

                float plusEdge = MainWidthProfileRaw(3.14159265, t);
                float minusEdge = MainWidthProfileRaw(-3.14159265, t);
                float sharedEdge = (plusEdge + minusEdge) * 0.5;

                return BlendAtSeam(a, rawValue, sharedEdge);
            }

            float MainEnergyProfileRaw(float a)
            {
                float bendBoost =
                    1.0 + 0.55 * exp(-pow((a - 0.90) / 0.48, 2.0));

                float rightFade =
                    1.0 - 0.18 * smoothstep(1.95, 2.60, a);

                return bendBoost * rightFade;
            }

            float MainEnergyProfile(float a)
            {
                float rawValue = MainEnergyProfileRaw(a);

                float plusEdge = MainEnergyProfileRaw(3.14159265);
                float minusEdge = MainEnergyProfileRaw(-3.14159265);
                float sharedEdge = (plusEdge + minusEdge) * 0.5;

                return BlendAtSeam(a, rawValue, sharedEdge);
            }

            float SeamSafeSine(float a, float frequency, float phase)
            {
                float rawValue = sin(frequency * a + phase);

                float plusEdge = sin(frequency * 3.14159265 + phase);
                float minusEdge = sin(frequency * -3.14159265 + phase);
                float sharedEdge = (plusEdge + minusEdge) * 0.5;

                return BlendAtSeam(a, rawValue, sharedEdge);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.directionOS);

                float a = atan2(dir.x, dir.z);
                float y = dir.y;
                float t = _Time.y * _Speed;

                float lowerBlend = smoothstep(-0.92, -0.08, y);
                float upperBlend = smoothstep(0.00, 0.82, y);

                float3 color = lerp(_BottomColor.rgb, _MidColor.rgb, lowerBlend);
                color = lerp(color, _TopColor.rgb, upperBlend);
                color *= _BackgroundStrength;

                float mainCenter = MainWave(a, t);
                float secondCenter = SecondaryWave(a, t);

                float horizonBloom =
                    exp(-pow((y - (_Horizon + 0.01)) / 0.26, 2.0));

                color +=
                    _BlueGlow.rgb
                    * horizonBloom
                    * 0.028
                    * _GlowIntensity;

                float widthProfile = MainWidthProfile(a, t);
                float energyProfile = MainEnergyProfile(a);

                float coreW =
                    _CoreWidth * widthProfile;

                float glowW =
                    _GlowWidth
                    * lerp(0.72, 0.96, saturate(widthProfile));

                float broad =
                    GaussianLine(y, mainCenter, glowW * 1.25);

                float glow =
                    GaussianLine(y, mainCenter, glowW * 0.72);

                float whiteBody =
                    GaussianLine(y, mainCenter, coreW * 1.45);

                float whiteCore =
                    GaussianLine(y, mainCenter, coreW * 0.68);

                float upperBandCenter =
                    mainCenter
                    + (0.014 + 0.010 * widthProfile)
                    + 0.008 * SeamSafeSine(a, 1.8, -t * 0.55);

                float upperBand =
                    GaussianLine(
                        y,
                        upperBandCenter,
                        coreW * 1.15
                    );

                float lowerBandCenter =
                    mainCenter
                    - (0.022 + 0.010 * widthProfile)
                    + 0.007 * SeamSafeSine(a, 1.35, t * 0.40);

                float lowerBand =
                    GaussianLine(
                        y,
                        lowerBandCenter,
                        glowW * 0.46
                    );

                float halo =
                    GaussianLine(
                        y,
                        mainCenter + 0.040 + 0.006 * widthProfile,
                        glowW * 1.10
                    );

                color +=
                    _BlueGlow.rgb
                    * broad
                    * 0.16
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _CyanGlow.rgb
                    * glow
                    * 0.28
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _WhiteCore.rgb
                    * whiteBody
                    * 0.12
                    * _WhiteIntensity
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _WhiteCore.rgb
                    * whiteCore
                    * 0.55
                    * _WhiteIntensity
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _BlueGlow.rgb
                    * upperBand
                    * 0.24
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _CyanGlow.rgb
                    * upperBand
                    * 0.16
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _BlueGlow.rgb
                    * lowerBand
                    * 0.22
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _CyanGlow.rgb
                    * lowerBand
                    * 0.11
                    * _GlowIntensity
                    * energyProfile;

                color +=
                    _BlueGlow.rgb
                    * halo
                    * 0.09
                    * _GlowIntensity
                    * energyProfile;

                float body =
                    smoothstep(
                        mainCenter + 0.02,
                        mainCenter - (0.18 + 0.03 * widthProfile),
                        y
                    );

                body *= smoothstep(-0.98, -0.02, y);

                color +=
                    _BlueGlow.rgb
                    * body
                    * (0.07 + 0.03 * widthProfile)
                    * _GlowIntensity;

                float sGlow =
                    GaussianLine(
                        y,
                        secondCenter,
                        _GlowWidth * 0.52
                    );

                float sCore =
                    GaussianLine(
                        y,
                        secondCenter,
                        _CoreWidth * 0.72
                    );

                color +=
                    _BlueGlow.rgb
                    * sGlow
                    * 0.26
                    * _SecondaryStrength
                    * _GlowIntensity;

                color +=
                    _CyanGlow.rgb
                    * sCore
                    * 0.22
                    * _SecondaryStrength
                    * _GlowIntensity;

                color *= _Exposure;

                return half4(max(color, 0.0), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
