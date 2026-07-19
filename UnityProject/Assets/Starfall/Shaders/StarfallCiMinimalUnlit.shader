Shader "Starfall/CI/MinimalUnlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MainTex ("Legacy Texture", 2D) = "white" {}
        _Color ("Legacy Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "StarfallCiMinimal"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4x4 unity_ObjectToWorld;
            float4x4 unity_MatrixVP;
            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _BaseColor;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionWS = mul(unity_ObjectToWorld, input.positionOS);
                output.positionHCS = mul(unity_MatrixVP, positionWS);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return tex2D(_BaseMap, input.uv) * _BaseColor;
            }
            ENDHLSL
        }
    }
}
