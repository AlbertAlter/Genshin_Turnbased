Shader "UI/GoldenDissolveFragments"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card Alpha Snapshot", 2D) = "white" {}
        _Gold ("Gold", Color) = (1,.68,.18,1)
        _Highlight ("Highlight", Color) = (1,.96,.7,1)
        _Roughness ("Irregularity", Range(0,1)) = .35
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
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float4 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float4 uv:TEXCOORD0; float4 mask:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _Gold,_Highlight,_ClipRect;
            float _Roughness,_UIMaskSoftnessX,_UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;o.uv=v.uv;
                float2 pixel=o.vertex.w;pixel/=abs(mul((float2x2)UNITY_MATRIX_P,_ScreenParams.xy));
                float4 rect=clamp(_ClipRect,-2e10,2e10);
                o.mask=float4(v.vertex.xy*2-rect.xy-rect.zw,.25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixel)));
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float phase=frac(dot(i.uv.zw,float2(127.1,311.7)))*6.2831853;
                float theta=atan2(i.uv.y,i.uv.x), radius=length(i.uv.xy);
                float boundary=.78+saturate(_Roughness)*(.13*sin(theta*3+phase)+.09*sin(theta*5-phase));
                float shape=1-smoothstep(boundary*.55,boundary,radius);
                float4 gold=lerp(_Highlight,_Gold,saturate(radius/max(.001,boundary)));
                // A fragment only exists where the original card/frame composite had alpha.
                float alpha=shape*tex2D(_MainTex,i.uv.zw).a*i.color.a*gold.a;
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask=saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw);
                alpha*=mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-.001);
                #endif
                return float4(gold.rgb*i.color.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
