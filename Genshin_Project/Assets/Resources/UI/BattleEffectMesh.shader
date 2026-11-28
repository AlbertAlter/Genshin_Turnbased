Shader "UI/BattleEffectMesh"
{
    Properties
    {
        [PerRendererData] _MainTex("Texture", 2D) = "white" {}
        _Intensity("Intensity", Float) = 1.4
        _EffectTime("Effect time", Float) = 0
        _RegionCount("Actual field regions", Float) = 0
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="False" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float3 normal:NORMAL; float4 uv:TEXCOORD0; float4 coord:TEXCOORD1; float4 fx:TEXCOORD2; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float4 world:TEXCOORD0; float3 coord:TEXCOORD1; float4 fx:TEXCOORD2; float3 normal:TEXCOORD3; float2 uv:TEXCOORD4; };
            sampler2D _MainTex;
            float _Intensity, _EffectTime;
            float _RegionCount;
            float4 _Regions[5];
            float4 _ClipRect;
            float hash(float3 p) { return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453); }
            float noise3(float3 p)
            {
                float3 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(hash(i),hash(i+float3(1,0,0)),f.x),lerp(hash(i+float3(0,1,0)),hash(i+float3(1,1,0)),f.x),f.y),
                            lerp(lerp(hash(i+float3(0,0,1)),hash(i+float3(1,0,1)),f.x),lerp(hash(i+float3(0,1,1)),hash(i+float3(1,1,1)),f.x),f.y),f.z);
            }
            v2f vert(appdata v)
            {
                v2f o; o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.color=v.color; o.coord=v.coord.xyz; o.fx=v.fx; o.normal=v.normal; o.uv=v.uv.xy; return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float4 c=i.color*tex2D(_MainTex,i.uv);
                if(_RegionCount>0)
                {
                    float coverage=0;
                    [unroll] for(int region=0;region<5;region++) if(region<_RegionCount)
                    {
                        float2 p=(i.coord.xy-_Regions[region].xy)/max(_Regions[region].zw,float2(.01,.01));
                        coverage=max(coverage,1-smoothstep(.82,1.14,length(p)));
                    }
                    c.a*=coverage;
                }
                float n=noise3(i.coord*5.5+float3(.13,-.8,.2)*_EffectTime);
                c.a*=lerp(1,smoothstep(.2,.72,n)*max(.15,abs(i.normal.z)),saturate(i.fx.x));
                if(i.fx.y>=-.2)c.a*=saturate((noise3(float3(i.uv*13,0))-i.fx.y)*8);
                if(i.fx.w>.5)c.a*=saturate((i.fx.z-(i.fx.w<1.5?i.uv.x:i.uv.y))*24);
                c.rgb*=_Intensity;
                if(i.fx.x<-.5)
                {
                    float3 normal=normalize(i.normal+float3(0,0,.00001));
                    if(normal.z>0) normal=-normal;
                    float3 light=normalize(float3(-.30,.55,-.78));
                    float diffuse=.72+.28*saturate(dot(normal,light));
                    float shine=pow(saturate(dot(reflect(-light,normal),float3(0,0,-1))),20);
                    float rim=pow(1-saturate(abs(normal.z)),3);
                    c.rgb=c.rgb*diffuse+float3(.60,.85,1)*(.25*shine+.12*rim);
                }
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
