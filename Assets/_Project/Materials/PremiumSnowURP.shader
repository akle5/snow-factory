Shader "SnowFactory/PremiumSnowURP"
{
    Properties
    {
        [Header(Snow Color and Subsurface)]
        _LitSnowColor ("Sunlit Snow Color", Color) = (0.98, 0.99, 1.0, 1.0)
        _ShadowSnowColor ("Glacier Shadow Color", Color) = (0.58, 0.73, 0.89, 1.0)
        _SubsurfaceColor ("Subsurface Scattering Tint", Color) = (0.42, 0.78, 0.96, 1.0)
        _SubsurfaceWrap ("Diffuse Wrap (Softness)", Range(0.0, 1.0)) = 0.55
        _SubsurfaceStrength ("Subsurface Glow Strength", Range(0.0, 2.0)) = 0.65

        [Header(Micro Relief and Dunes)]
        _MacroScale ("Macro Dune Scale", Float) = 0.85
        _MicroScale ("Micro Powder Scale", Float) = 6.5
        _BumpStrength ("Normal Bump Strength", Range(0.0, 1.5)) = 0.42

        [Header(Crystal Sparkles)]
        _SparkleScale ("Sparkle Crystal Density", Float) = 42.0
        _SparkleThreshold ("Sparkle Threshold", Range(0.70, 0.99)) = 0.86
        _SparkleIntensity ("Sparkle HDR Intensity", Range(0.0, 6.0)) = 2.6
        _SparkleColor ("Sparkle Color", Color) = (0.92, 0.98, 1.0, 1.0)

        [Header(Velvet Rim Light)]
        _RimColor ("Rim Glow Color", Color) = (0.82, 0.93, 1.0, 1.0)
        _RimPower ("Rim Power", Range(1.0, 8.0)) = 3.2
        _RimIntensity ("Rim Intensity", Range(0.0, 2.0)) = 0.48

        [Header(Sketchfab Snow PBR Textures)]
        [NoScaleOffset] _SnowAlbedoMap ("Snow BaseColor Texture", 2D) = "white" {}
        [NoScaleOffset] _SnowNormalMap ("Snow Normal Map", 2D) = "bump" {}
        [NoScaleOffset] _SnowMaskMap ("Snow MetallicRoughnessAO Map", 2D) = "white" {}
        _SnowTexTiling ("Triplanar World Tiling", Float) = 0.22
        _SnowTexBlend ("Snow Albedo Detail Strength", Range(0.0, 1.0)) = 0.65
        _SnowNormalStrength ("Snow Normal Map Strength", Range(0.0, 2.0)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SnowAlbedoMap);
            SAMPLER(sampler_SnowAlbedoMap);
            TEXTURE2D(_SnowNormalMap);
            SAMPLER(sampler_SnowNormalMap);
            TEXTURE2D(_SnowMaskMap);
            SAMPLER(sampler_SnowMaskMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _LitSnowColor;
                float4 _ShadowSnowColor;
                float4 _SubsurfaceColor;
                float _SubsurfaceWrap;
                float _SubsurfaceStrength;
                float _MacroScale;
                float _MicroScale;
                float _BumpStrength;
                float _SparkleScale;
                float _SparkleThreshold;
                float _SparkleIntensity;
                float4 _SparkleColor;
                float4 _RimColor;
                float _RimPower;
                float _RimIntensity;
                float _SnowTexTiling;
                float _SnowTexBlend;
                float _SnowNormalStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS        : SV_POSITION;
                float3 positionWS        : TEXCOORD0;
                float3 normalWS          : TEXCOORD1;
                float4 vertexCol         : TEXCOORD2;
                float  fogFactor         : TEXCOORD3;
                float3 positionOS_cached : TEXCOORD4;
            };

            // Fast 3D hash for procedural snow grains and ice crystal facets
            float3 Hash33(float3 p)
            {
                p = float3(
                    dot(p, float3(127.1, 311.7, 74.7)),
                    dot(p, float3(269.5, 183.3, 246.1)),
                    dot(p, float3(113.5, 271.9, 124.6))
                );
                return frac(sin(p) * 43758.5453123);
            }

            float Noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash33(i + float3(0, 0, 0)).x;
                float n100 = Hash33(i + float3(1, 0, 0)).x;
                float n010 = Hash33(i + float3(0, 1, 0)).x;
                float n110 = Hash33(i + float3(1, 1, 0)).x;
                float n001 = Hash33(i + float3(0, 0, 1)).x;
                float n101 = Hash33(i + float3(1, 0, 1)).x;
                float n011 = Hash33(i + float3(0, 1, 1)).x;
                float n111 = Hash33(i + float3(1, 1, 1)).x;

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);

                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);

                return lerp(nxy0, nxy1, f.z);
            }

            float SnowFBM(float3 p)
            {
                float v = 0.0;
                v += 0.55 * Noise3D(p);
                v += 0.30 * Noise3D(p * 2.13);
                v += 0.15 * Noise3D(p * 4.47);
                return v;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.vertexCol = input.color;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                output.positionOS_cached = posInputs.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);

                // 1. Procedural organic powder & wind-drift normal perturbation via finite differences
                float eps = 0.035;
                float3 macroPos = posWS * _MacroScale;
                float3 microPos = posWS * _MicroScale;

                float hCenter = SnowFBM(macroPos) * 0.65 + Noise3D(microPos) * 0.35;
                float hX = SnowFBM((posWS + float3(eps, 0, 0)) * _MacroScale) * 0.65 + Noise3D((posWS + float3(eps, 0, 0)) * _MicroScale) * 0.35;
                float hZ = SnowFBM((posWS + float3(0, 0, eps)) * _MacroScale) * 0.65 + Noise3D((posWS + float3(0, 0, eps)) * _MicroScale) * 0.35;

                float3 grad = float3(hX - hCenter, 0.0, hZ - hCenter) / eps;

                // 1b. Triplanar sampling of Sketchfab "Snow Material" PBR textures (cropping UV Y to [0.08..0.92] to avoid polar triangles)
                float3 absN = pow(abs(input.normalWS), 4.0);
                float3 triWeights = absN / max(dot(absN, float3(1.0, 1.0, 1.0)), 0.0001);

                float2 uvX = frac(posWS.zy * _SnowTexTiling);
                float2 uvY = frac(posWS.xz * _SnowTexTiling);
                float2 uvZ = frac(posWS.xy * _SnowTexTiling);
                uvX.y = lerp(0.08, 0.92, uvX.y);
                uvY.y = lerp(0.08, 0.92, uvY.y);
                uvZ.y = lerp(0.08, 0.92, uvZ.y);

                float3 texColX = SAMPLE_TEXTURE2D(_SnowAlbedoMap, sampler_SnowAlbedoMap, uvX).rgb;
                float3 texColY = SAMPLE_TEXTURE2D(_SnowAlbedoMap, sampler_SnowAlbedoMap, uvY).rgb;
                float3 texColZ = SAMPLE_TEXTURE2D(_SnowAlbedoMap, sampler_SnowAlbedoMap, uvZ).rgb;
                float3 snowAlbedoTex = texColX * triWeights.x + texColY * triWeights.y + texColZ * triWeights.z;

                float3 maskX = SAMPLE_TEXTURE2D(_SnowMaskMap, sampler_SnowMaskMap, uvX).rgb;
                float3 maskY = SAMPLE_TEXTURE2D(_SnowMaskMap, sampler_SnowMaskMap, uvY).rgb;
                float3 maskZ = SAMPLE_TEXTURE2D(_SnowMaskMap, sampler_SnowMaskMap, uvZ).rgb;
                float3 snowMaskTex = maskX * triWeights.x + maskY * triWeights.y + maskZ * triWeights.z;
                float texAO = lerp(1.0, saturate(snowMaskTex.r * 1.15), _SnowTexBlend);

                float3 nrmX = SAMPLE_TEXTURE2D(_SnowNormalMap, sampler_SnowNormalMap, uvX).rgb * 2.0 - 1.0;
                float3 nrmY = SAMPLE_TEXTURE2D(_SnowNormalMap, sampler_SnowNormalMap, uvY).rgb * 2.0 - 1.0;
                float3 nrmZ = SAMPLE_TEXTURE2D(_SnowNormalMap, sampler_SnowNormalMap, uvZ).rgb * 2.0 - 1.0;
                float3 triNormalOffset = float3(
                    nrmY.x * triWeights.y + nrmZ.x * triWeights.z,
                    nrmX.y * triWeights.x + nrmZ.y * triWeights.z,
                    nrmX.x * triWeights.x + nrmY.y * triWeights.y
                ) * _SnowNormalStrength;

                float3 normalWS = normalize(input.normalWS - grad * _BumpStrength + triNormalOffset * 0.42);

                // 2. Main Light + Soft Wrapped Diffuse + Subsurface Scattering
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                Light mainLight = GetMainLight(shadowCoord);

                float NdotL = dot(normalWS, mainLight.direction);
                float wrappedDiffuse = saturate((NdotL + _SubsurfaceWrap) / (1.0 + _SubsurfaceWrap));
                float shadowAtten = lerp(0.35, 1.0, mainLight.shadowAttenuation);

                // Ridge & slope contrast so 3D form and wind-swept dunes read with rich volume
                float slopeShade = saturate(normalWS.y * 0.65 + 0.35);
                float litFactor = saturate(wrappedDiffuse * shadowAtten * slopeShade);

                // Vertex color channels:
                // - R: excavation depth AO (1 = untouched surface, 0.25 = deep crater)
                // - G: exposed hardness layer tint (1.0 = Soft White Snow, 0.5 = Compacted Light-Blue Ice, 0.0 = Deep Glacier Blue Core)
                float scoopDepthAO = lerp(0.45, 1.0, input.vertexCol.r);
                float hardnessLayer = saturate(input.vertexCol.g);

                // Three-stop glacier-to-sunlit color ramp for strong artistic identity
                float3 deepGlacier = _ShadowSnowColor.rgb * 0.78;
                float3 midAlpine = lerp(_ShadowSnowColor.rgb, _LitSnowColor.rgb, 0.55);
                float rampT = saturate(litFactor * scoopDepthAO);
                float3 softSnowColor = rampT < 0.5
                    ? lerp(deepGlacier, midAlpine, rampT * 2.0)
                    : lerp(midAlpine, _LitSnowColor.rgb, (rampT - 0.5) * 2.0);

                // Layer 2: Compacted Frost Ice (Crisp Electric Cyan-Blue)
                float3 compactIceColor = lerp(float3(0.15, 0.56, 0.92), float3(0.45, 0.85, 1.0), rampT);
                // Layer 3: Deep Glacier Core (Rich Crystalline Cobalt/Sapphire Blue with inner glow)
                float3 glacierCoreColor = lerp(float3(0.03, 0.18, 0.65), float3(0.14, 0.52, 0.98), rampT);

                float3 baseSnow = hardnessLayer > 0.5
                    ? lerp(compactIceColor, softSnowColor, (hardnessLayer - 0.5) * 2.0)
                    : lerp(glacierCoreColor, compactIceColor, hardnessLayer * 2.0);

                // Blend the Sketchfab Snow Material BaseColor + AO detail into the snow/ice strata
                float3 normalizedSnowTex = saturate(snowAlbedoTex * 1.32);
                float3 texturedSnow = baseSnow * lerp(float3(1.0, 1.0, 1.0), normalizedSnowTex, _SnowTexBlend) * texAO;
                baseSnow = texturedSnow;

                // Boost crystal sparkles when glacier ice is exposed!
                float iceSparkleBoost = lerp(1.85, 1.0, hardnessLayer);

                // Subsurface scattering band at the terminator & backlit edges
                float sssBand = smoothstep(0.05, 0.55, wrappedDiffuse) * (1.0 - smoothstep(0.42, 0.88, saturate(NdotL)));
                float backLight = saturate(dot(viewDirWS, -mainLight.direction + normalWS * 0.45));
                float3 sssContribution = _SubsurfaceColor.rgb * (sssBand * 0.42 + pow(backLight, 3.0) * 0.45) * _SubsurfaceStrength;

                // 3. Warm Fireplace Hearth Glow (computed cleanly from hearth world position (4.5, 0.95, 8.2))
                float3 hearthPos = float3(4.5, 0.95, 8.2);
                float3 toHearth = hearthPos - posWS;
                float distSq = dot(toHearth, toHearth);
                float3 hearthDir = normalize(toHearth);
                float hearthWrap = saturate((dot(normalWS, hearthDir) + 0.35) / 1.35);
                float hearthAtten = saturate(1.0 / (1.0 + distSq * 0.14)) * saturate(1.0 - distSq / 85.0);
                float3 additionalLightsColor = float3(1.0, 0.48, 0.14) * (hearthWrap * hearthAtten * 0.95);

                // 4. View-Dependent Ice Crystal Micro-Sparkles (Circular organic glints in Object Space — no boxy grid cells!)
                float3 osPos = input.positionOS_cached * _SparkleScale;
                float3 cellId = floor(osPos);
                float3 cellLocal = frac(osPos) - 0.5;
                float3 rnd = Hash33(cellId);

                // Random jittered center inside the cell + circular radial falloff so glints are tiny round pinpricks
                float3 glintOffset = (rnd - 0.5) * 0.55;
                float distToGlint = length(cellLocal - glintOffset);
                float circularDot = 1.0 - smoothstep(0.04, 0.19, distToGlint);

                float3 crystalFacetDir = normalize(rnd * 2.0 - 1.0);
                float3 halfVec = normalize(mainLight.direction + viewDirWS * 1.45);
                float crystalAlign = abs(dot(crystalFacetDir, halfVec));
                float sparkleMask = circularDot * smoothstep(_SparkleThreshold, 0.995, crystalAlign);

                float totalSparkle = sparkleMask * saturate(0.25 + litFactor * 0.85) * iceSparkleBoost;
                float3 sparkleEmission = _SparkleColor.rgb * (totalSparkle * _SparkleIntensity);

                // 5. Velvet Fresnel Rim Glow for soft powdery silhouette
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
                float3 rimGlow = _RimColor.rgb * (fresnel * _RimIntensity * saturate(normalWS.y * 0.5 + 0.5));

                // Final composition (controlled below Bloom threshold except on crystal sparkles & sunlit crests)
                float3 finalColor = baseSnow * 0.86
                                  + sssContribution
                                  + additionalLightsColor
                                  + sparkleEmission
                                  + rimGlow * 0.45;

                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
