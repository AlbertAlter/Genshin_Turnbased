Shader "UI/CardShield"
{
    Properties
    {
        [PerRendererData] _MainTex ("PSD membrane", 2D) = "white" {}
        _MaskTex ("PSD elliptical mask in alpha", 2D) = "white" {}
        _BorderTex ("PSD shieldbar", 2D) = "white" {}
        _RevealProgress ("Center-out reveal", Range(0,1)) = 1
        _RevealSoftness ("Reveal feather / half width", Range(0,1)) = 0.06
        _RevealEdgeWidth ("Reveal light / half width", Range(0,1)) = 0.04
        _RevealEdgeOpacity ("Reveal light opacity", Range(0,1)) = 0.65
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex, _MaskTex, _BorderTex;
            float4 _ProjectWhite;
            float4 _ReferenceSize, _MembraneRect, _MaskRect, _TintRect, _TintColor, _SweepColor, _ClipRect;
            float _MembraneOpacity, _TintOpacity, _SweepProgress, _SweepAngle, _SweepWidth, _SweepSoftness, _SweepOpacity;
            float _RevealProgress, _RevealSoftness, _RevealEdgeWidth, _RevealEdgeOpacity;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.local=v.vertex; o.color=v.color; o.uv=v.uv; return o;
            }
            float inside(float2 uv) { return step(0,uv.x)*step(0,uv.y)*step(uv.x,1)*step(uv.y,1); }
            float2 layerUV(float2 p,float4 rect) { return (p-rect.xy)/rect.zw+.5; }
            float4 frag(v2f i):SV_Target
            {
                float2 p=(i.uv-.5)*_ReferenceSize.xy;
                float2 uv=layerUV(p,_MembraneRect), maskUV=layerUV(p,_MaskRect);
                float4 membrane=tex2D(_MainTex,uv);
                // Outside the saved mask bounds Photoshop's mask background is white (255).
                float mask=lerp(1,tex2D(_MaskTex,maskUV).a,inside(maskUV));
                float face=membrane.a*mask;
                float border=tex2D(_BorderTex,uv).a;
                // Photoshop clips the element colour to the combined shield alpha.
                float2 tintUV=layerUV(p,_TintRect);
                float tint=inside(tintUV)*_TintOpacity;
                float3 rgb=lerp(_ProjectWhite.rgb,_TintColor.rgb,tint);
                float slope=tan(radians(_SweepAngle))*_TintRect.z/_TintRect.w;
                float reach=.5+.5*abs(slope)+_SweepWidth;
                float center=lerp(reach,-reach,_SweepProgress);
                float distance=abs((tintUV.y-.5)-slope*(tintUV.x-.5)-center);
                float halfWidth=_SweepWidth*.5;
                float light=(1-smoothstep(halfWidth*(1-_SweepSoftness),halfWidth+.0001,distance))*_SweepOpacity;
                // Reflect on the fill silhouette, even where the tint fades out at the centre.
                // Composite the unchanged border in front; it cannot generate a highlight by itself.
                float reflection=saturate(light*membrane.a*inside(tintUV));
                float a=border+(1-border)*(reflection+(1-reflection)*face);
                rgb=lerp(rgb,_SweepColor.rgb,reflection*(1-border)/max(a,.00001));
                // UVs remain unchanged: reveal the full PSD stack symmetrically without stretching/splitting it.
                if (_RevealProgress < 1)
                {
                    float feather=max(_RevealSoftness,.0001);
                    float boundary=saturate(_RevealProgress)*(1+feather);
                    float distanceFromCenter=abs(i.uv.x-.5)*2;
                    float reveal=1-smoothstep(boundary-feather,boundary,distanceFromCenter);
                    float front=abs(distanceFromCenter-(boundary-feather*.5));
                    float lightWidth=max(_RevealEdgeWidth,.0001);
                    float edge=(1-smoothstep(0,lightWidth,front))*_RevealEdgeOpacity;
                    // Brightness only inside the existing membrane/border silhouette; no rectangular halo.
                    edge*=smoothstep(0,.08,_RevealProgress)*(1-smoothstep(.75,1,_RevealProgress));
                    edge*=step(.00001,_RevealEdgeWidth);
                    float shape=saturate(border+(1-border)*face);
                    float litAlpha=edge*shape;
                    float combined=a+(1-a)*litAlpha;
                    rgb=lerp(rgb,_SweepColor.rgb,litAlpha/max(combined,.00001));
                    a=combined*reveal;
                }
                a*=inside(uv)*_MembraneOpacity*i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(a-.001);
                #endif
                return float4(rgb*i.color.rgb,a);
            }
            ENDHLSL
        }
    }
}
