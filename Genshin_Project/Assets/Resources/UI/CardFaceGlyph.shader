Shader "UI/CardFaceGlyph"
{
    Properties
    {
        [PerRendererData] _MainTex ("Original packed glyph atlas", 2D) = "black" {}
        _CoreColor ("Core color", Color) = (1,1,1,1)
        _CoreOpacity ("Core opacity", Range(0,1)) = 1
        _GlowOpacity ("Glow opacity", Range(0,1)) = 0.7
        _UseAlpha ("Use original sprite alpha instead of packed channels", Float) = 0
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 local:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _ClipRect, _CoreColor;
            float _CoreOpacity, _GlowOpacity, _UseAlpha;
            v2f vert(appdata v)
            {
                v2f o; o.local=v.vertex.xy; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float4 source=tex2D(_MainTex,i.uv);
                float3 packed=source.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                // These are coverage channels, not source RGB colors; undo sRGB sampling for masks.
                packed=LinearToGammaSpace(packed);
                #endif
                float coreMask=packed.r, glowMask=packed.g;
                if (_UseAlpha > .5)
                {
                    // Existing ability sprites retain their exact alpha and holes. A small UI-only
                    // halo shares the glyph shader; packed Lisa coverage is unchanged.
                    coreMask=source.a;
                    float2 d=_MainTex_TexelSize.xy*2;
                    glowMask=coreMask;
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv+float2(d.x,0)).a);
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv-float2(d.x,0)).a);
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv+float2(0,d.y)).a);
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv-float2(0,d.y)).a);
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv+d*.707).a);
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv-d*.707).a);
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv+float2(d.x,-d.y)*.707).a);
                    glowMask=max(glowMask,tex2D(_MainTex,i.uv+float2(-d.x,d.y)*.707).a);
                }
                float core=coreMask*_CoreOpacity, glow=glowMask*_GlowOpacity;
                float coverage=max(core,glow);
                float coreWeight=core/max(coverage,.00001);
                float3 color=lerp(i.color.rgb,_CoreColor.rgb,coreWeight);
                float alpha=coverage*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                alpha*=UnityGet2DClipping(i.local,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-.001);
                #endif
                return float4(color,alpha);
            }
            ENDCG
        }
    }
}
