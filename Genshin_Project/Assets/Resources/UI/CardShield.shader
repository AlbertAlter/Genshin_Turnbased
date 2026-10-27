Shader "UI/CardShield"
{
    Properties
    {
        [PerRendererData] _MainTex ("PSD membrane", 2D) = "white" {}
        _MaskTex ("PSD elliptical mask in alpha", 2D) = "white" {}
        _CardMask ("Card silhouette", 2D) = "white" {}
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
            sampler2D _MainTex, _MaskTex, _CardMask;
            float4 _ReferenceSize, _MembraneRect, _MaskRect, _TintRect, _TintColor, _SweepColor, _ClipRect;
            float _MembraneOpacity, _TintOpacity, _SweepProgress, _SweepAngle, _SweepWidth, _SweepSoftness, _SweepOpacity;
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
                float a=membrane.a*inside(uv)*mask*_MembraneOpacity;
                float3 premul=membrane.rgb*a;
                // PSD stack: normal membrane, then the independent normal colour rectangle.
                float tint=inside(layerUV(p,_TintRect))*_TintOpacity;
                premul=premul*(1-tint)+_TintColor.rgb*tint; a=a*(1-tint)+tint;
                float slope=tan(radians(_SweepAngle))*_ReferenceSize.x/_ReferenceSize.y;
                float reach=.5+.5*abs(slope)+_SweepWidth;
                float center=lerp(reach,-reach,_SweepProgress);
                float distance=abs((i.uv.y-.5)-slope*(i.uv.x-.5)-center);
                float halfWidth=_SweepWidth*.5;
                float light=(1-smoothstep(halfWidth*(1-_SweepSoftness),halfWidth+.0001,distance))*_SweepOpacity;
                premul=premul*(1-light)+_SweepColor.rgb*light; a=a*(1-light)+light;
                float3 rgb=premul/max(a,.00001);
                a*=tex2D(_CardMask,i.uv).a*i.color.a;
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
