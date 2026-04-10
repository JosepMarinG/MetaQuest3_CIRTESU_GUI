Shader "Unlit/ROS/Point"
{
    Properties
    {
        _ColorMin ("Intensity min", Color) = (0, 0, 0, 0)
        _ColorMax ("Intensity max", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local COLOR_INTENSITY COLOR_RGB COLOR_Z

            #include "UnityCG.cginc"
            #include "Assets/Scripts/PointCloud/PointHelper.cginc"

            struct v2f
            {
                float4 pos: SV_POSITION;
                float4 color: COLOR0;
            };

            struct pointdata
            {
                float3 position;
                float intensity;
            };

            StructuredBuffer<float3> _Positions;
            StructuredBuffer<pointdata> _PointData;

            uniform uint _BaseVertexIndex;
            uniform float _PointSize;
            uniform float4x4 _ObjectToWorld;
            uniform float4 _ColorMin;
            uniform float4 _ColorMax;

            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;
                float3 pos = _PointData[instanceID].position;
                float4 wpos = mul(_ObjectToWorld, float4(pos, 1.0));
                float2 quad = _Positions[_BaseVertexIndex + vertexID].xy * _PointSize;
                float4 vpos = mul(UNITY_MATRIX_V, wpos);
                vpos.xy += quad;

                o.pos = mul(UNITY_MATRIX_P, vpos);

                #ifdef COLOR_INTENSITY
                    o.color = lerp(_ColorMin, _ColorMax, _PointData[instanceID].intensity);
                #elif defined(COLOR_RGB)
                    o.color = UnpackRGBA(_PointData[instanceID].intensity);
                #elif defined(COLOR_Z)
                    o.color = lerp(_ColorMin, _ColorMax, (pos.z + 1.0) * 0.5);
                #else
                    o.color = float4(1, 1, 1, 1);
                #endif

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
