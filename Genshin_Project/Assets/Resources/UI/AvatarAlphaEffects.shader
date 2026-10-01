Shader "UI/AvatarAlphaEffects"
{
    Properties
    {
        [PerRendererData] _MainTex ("Avatar", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _SpriteUV,_UvPerUnit,_ShadowOffset,_ShadowColor,_OutlineColor,_ClipRect;
            float _ShadowSize,_Spread,_OutlineWidth;
            v2f vert(appdata v) { v2f o; o.local=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;o.uv=v.uv;return o; }
            float4 sampleAvatar(float2 uv)
            {
                float valid=step(_SpriteUV.x,uv.x)*step(_SpriteUV.y,uv.y)*step(uv.x,_SpriteUV.z)*step(uv.y,_SpriteUV.w);
                return tex2D(_MainTex,clamp(uv,_SpriteUV.xy,_SpriteUV.zw))*valid;
            }
            float4 over(float4 top,float4 bottom) { return top+bottom*(1-top.a); }
            float4 frag(v2f i):SV_Target
            {
                float4 original=sampleAvatar(i.uv);
                float dilation=original.a;
                if (_OutlineWidth>0)
                {
                    [loop] for (int k=0;k<24;k++)
                    {
                        float angle=k*6.2831853/24;
                        float2 delta=float2(cos(angle),sin(angle))*_OutlineWidth*_UvPerUnit.xy;
                        dilation=max(dilation,sampleAvatar(i.uv+delta).a);
                        dilation=max(dilation,sampleAvatar(i.uv+delta*.5).a);
                    }
                }
                float shadow=0,weight=0;
                float2 center=i.uv-_ShadowOffset.xy*_UvPerUnit.xy;
                // Bounded Gaussian approximation of Photoshop size/spread, in UI units.
                [loop] for (int y=-4;y<=4;y++)
                [loop] for (int x=-4;x<=4;x++)
                {
                    float w=exp(-(x*x+y*y)/8.0);
                    float alpha=sampleAvatar(center+float2(x,y)*(_ShadowSize/4)*_UvPerUnit.xy).a;
                    shadow+=saturate(alpha/max(.001,1-_Spread))*w;weight+=w;
                }
                float sa=shadow/max(weight,.001)*_ShadowColor.a;
                float oa=max(0,dilation-original.a)*_OutlineColor.a;
                float4 effect=over(float4(_OutlineColor.rgb*oa,oa),float4(_ShadowColor.rgb*sa,sa));
                float4 result=over(float4(original.rgb*original.a,original.a),effect);
                result.rgb*=i.color.rgb*i.color.a;result.a*=i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                result*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a-.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
