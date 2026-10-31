Shader "UI/CardVariantCrossfade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Current card", 2D) = "white" {}
        _NextTex ("Next card", 2D) = "white" {}
        _Blend ("Blend", Range(0,1)) = 0
        _FromUV ("Current crop", Vector) = (0,0,1,1)
        _ToUV ("Next crop", Vector) = (0,0,1,1)
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
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 mask:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex, _NextTex;
            float4 _FromUV, _ToUV, _ClipRect;
            float _Blend, _UIMaskSoftnessX, _UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv;
                float2 pixel=o.vertex.w; pixel/=abs(mul((float2x2)UNITY_MATRIX_P,_ScreenParams.xy));
                float4 rect=clamp(_ClipRect,-2e10,2e10);
                o.mask=float4(v.vertex.xy*2-rect.xy-rect.zw,.25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixel)));
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float2 normalized=(i.uv-_FromUV.xy)/max(_FromUV.zw,float2(.00001,.00001));
                float4 a=tex2D(_MainTex,i.uv);
                float4 b=tex2D(_NextTex,_ToUV.xy+normalized*_ToUV.zw);
                // Mix premultiplied colours, then return straight alpha for standard UI blending.
                // Unchanged pixels stay unchanged; opaque cards never expose the background midway.
                float alpha=lerp(a.a,b.a,saturate(_Blend));
                float3 rgb=lerp(a.rgb*a.a,b.rgb*b.a,saturate(_Blend))/max(alpha,.00001);
                alpha*=i.color.a; rgb*=i.color.rgb;
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask=saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw); alpha*=mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha-.001);
                #endif
                return float4(rgb,alpha);
            }
            ENDHLSL
        }
    }
}
