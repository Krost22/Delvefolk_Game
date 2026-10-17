// Sombreado plano (normales por cara) con colores por vértice, para URP (Forward y Forward+).
// Equivale a los MeshLambertMaterial/MeshBasicMaterial del original: Lambert + ambiente hemisférico
// + luces puntuales, niebla lineal, emisión y destello al recibir golpes (_FlashColor, vía MaterialPropertyBlock).
Shader "Delvefolk/Flat"
{
    Properties
    {
        [MainTexture] _BaseMap ("Textura", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
        _EmissionMap ("Mapa de emisión", 2D) = "white" {}
        [HDR] _EmissionColor ("Emisión", Color) = (0,0,0,0)
        [HDR] _FlashColor ("Destello", Color) = (0,0,0,0)
        _Recolor ("Recolorear (rgb tinte, a cantidad)", Color) = (1,1,1,0)
        _UseVertexColor ("Usar color de vértice", Float) = 1
        _FlatShading ("Sombreado plano", Float) = 1
        _Unlit ("Sin iluminación", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap);  SAMPLER(sampler_EmissionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _FlashColor;
            half4 _Recolor;
            half _UseVertexColor;
            half _FlatShading;
            half _Unlit;
        CBUFFER_END

        // Ambiente del juego (luz ambiente + hemisférica del original), fijado desde C# con Shader.SetGlobal*
        half4 _DfAmbient;
        half4 _DfHemiSky;
        half4 _DfHemiGround;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half4 color       : TEXCOORD3;
                half fogFactor    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = lerp(half4(1, 1, 1, 1), v.color, _UseVertexColor);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 Lambert(Light light, half3 n)
            {
                return light.color * (light.distanceAttenuation * light.shadowAttenuation) * saturate(dot(n, light.direction));
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 albedo = tex * _BaseColor * i.color;
                // Recolorear: sustituye el tono por el tinte conservando la luminancia (slimes con modelos de otro color)
                half lum = dot(albedo.rgb, half3(0.299, 0.587, 0.114));
                albedo.rgb = lerp(albedo.rgb, saturate(lum * 1.7) * _Recolor.rgb, _Recolor.a);

                half3 color;
                if (_Unlit > 0.5)
                {
                    color = albedo.rgb;
                }
                else
                {
                    float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half3 n = normalize(i.normalWS);
                    if (_FlatShading > 0.5)
                    {
                        float3 fn = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                        n = (half3)fn;
                    }
                    // Normal siempre hacia el observador (caras dobles y convenciones de ddx/ddy)
                    if (dot(n, viewDir) < 0) n = -n;

                    half3 ambient = _DfAmbient.rgb + lerp(_DfHemiGround.rgb, _DfHemiSky.rgb, n.y * 0.5 + 0.5);
                    #if defined(_SCREEN_SPACE_OCCLUSION)
                        float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                        AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(suv);
                        ambient *= ao.indirectAmbientOcclusion;
                    #endif

                    Light mainLight = GetMainLight();
                    half3 lit = ambient + Lambert(mainLight, n);

                    #if defined(_ADDITIONAL_LIGHTS)
                        InputData inputData = (InputData)0;
                        inputData.positionWS = i.positionWS;
                        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                        uint pixelLightCount = GetAdditionalLightsCount();
                        #if USE_CLUSTER_LIGHT_LOOP
                        [loop] for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                        {
                            Light dl = GetAdditionalLight(li, i.positionWS);
                            lit += Lambert(dl, n);
                        }
                        #endif
                        LIGHT_LOOP_BEGIN(pixelLightCount)
                            Light l = GetAdditionalLight(lightIndex, i.positionWS);
                            lit += Lambert(l, n);
                        LIGHT_LOOP_END
                    #endif
                    color = albedo.rgb * lit;
                }

                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                color += emission + _FlashColor.rgb;
                color = MixFog(color, i.fogFactor);
                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct AttributesD { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct VaryingsD { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            VaryingsD vertDepth(AttributesD v)
            {
                VaryingsD o = (VaryingsD)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half fragDepth(VaryingsD i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct AttributesN { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct VaryingsN { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            VaryingsN vertDN(AttributesN v)
            {
                VaryingsN o = (VaryingsN)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 fragDN(VaryingsN i) : SV_Target
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 nws = normalize(i.normalWS);
                    float2 oct = saturate(PackNormalOctQuadEncode(nws) * 0.5 + 0.5);
                    return half4(PackFloat2To888(oct), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
