Shader "UI/ElementAuraIcon"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _GlowColor ("Element", Color) = (1,1,1,1)
        _Glow ("Glow", Range(0,1)) = 0
        _BrightnessAmount ("Brightness", Range(0,1)) = .35
        _SpriteUV ("Sprite UV", Vector) = (0,0,1,1)
        _PixelUV ("UV per UI pixel", Vector) = (1,1,0,0)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _PixelUV, _SpriteUV, _GlowColor, _ClipRect, _TextureSampleAdd;
            float _Glow, _BrightnessAmount;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o;
            }
            float4 Read(float2 uv)
            {
                float inside=step(_SpriteUV.x,uv.x)*step(_SpriteUV.y,uv.y)*step(uv.x,_SpriteUV.z)*step(uv.y,_SpriteUV.w);
                return (tex2D(_MainTex,clamp(uv,_SpriteUV.xy,_SpriteUV.zw))+_TextureSampleAdd)*inside;
            }
            float4 frag(v2f i):SV_Target
            {
                float4 base=Read(i.uv);
                float halo=0;
                if (_Glow>0)
                {
                    // 小图标的三圈邻域采样，只在脉冲时执行。留白UV之外强制为透明，避免边缘拉丝。
                    [unroll] for(int ring=1;ring<=3;ring++)
                    [unroll] for(int n=0;n<8;n++)
                    {
                        float angle=n*0.785398163;
                        float2 offset=float2(cos(angle),sin(angle))*_PixelUV.xy*ring*(1+_Glow*2);
                        halo+=Read(i.uv+offset).a*(4-ring)/48;
                    }
                }
                float outer=halo*_Glow*.7*(1-base.a);
                float alpha=base.a+outer;
                float3 rgb=(base.rgb*_GlowColor.rgb*(1+_Glow*_BrightnessAmount)*base.a+_GlowColor.rgb*outer)/max(alpha,.0001);
                float4 color=float4(rgb,alpha)*i.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a-.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
