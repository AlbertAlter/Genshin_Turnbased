Shader "UI/GoldenDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Image", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Progress", Range(0,1)) = 0
        _NoiseScale ("Noise Scale", Float) = 12
        _NoiseOffset ("Noise Offset", Vector) = (0,0,0,0)
        _EffectRect ("Local Rect", Vector) = (0,0,100,100)
        _InputPremultiplied ("Premultiplied Snapshot", Float) = 0
        _UseSourceBounds ("Use Source Bounds", Float) = 0
        _EdgeWidth ("Gold Edge Width", Float) = .055
        _EdgeSoftness ("Edge Softness", Float) = .012
        _EdgeColor ("Gold", Color) = (1,.68,.18,1)
        _EdgeHighlight ("Gold Highlight", Color) = (1,.96,.7,1)
        _Direction ("Dissolve Direction", Vector) = (0,1,0,0)
        _DirectionWeight ("Direction Weight", Range(0,1)) = .2
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" "PreviewType"="Plane" }
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
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 cardLocal:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 local:TEXCOORD1; float4 mask:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _Color, _TextureSampleAdd, _ClipRect, _EffectRect, _NoiseOffset, _Direction, _EdgeColor, _EdgeHighlight;
            float _Progress, _NoiseScale, _EdgeWidth, _EdgeSoftness, _DirectionWeight, _UIMaskSoftnessX, _UIMaskSoftnessY;
            float _InputPremultiplied, _UseSourceBounds;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex);
                // POSITION may already be Canvas-batched; TEXCOORD1 remains Graphic-local.
                o.local=v.cardLocal; o.uv=v.uv; o.color=v.color*_Color;
                float2 pixelSize=o.vertex.w;
                pixelSize/=abs(mul((float2x2)UNITY_MATRIX_P,_ScreenParams.xy));
                float4 rect=clamp(_ClipRect,-2e10,2e10);
                o.mask=float4(v.vertex.xy*2-rect.xy-rect.zw,
                    .25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixelSize)));
                return o;
            }
            float hash(float2 p)
            {
                p=frac(p*float2(123.34,456.21));
                p+=dot(p,p+45.32);
                return frac(p.x*p.y);
            }
            float noise(float2 p)
            {
                float2 cell=floor(p), t=frac(p);
                t=t*t*(3-2*t);
                return lerp(lerp(hash(cell),hash(cell+float2(1,0)),t.x),
                    lerp(hash(cell+float2(0,1)),hash(cell+float2(1,1)),t.x),t.y);
            }
            float4 frag(v2f i):SV_Target
            {
                float2 uv=(i.local-_EffectRect.xy)/max(_EffectRect.zw,float2(.001,.001));
                float4 color=tex2D(_MainTex,_UseSourceBounds>0.5 ? uv : i.uv)+_TextureSampleAdd;
                if (_InputPremultiplied>0.5) color.rgb=color.a>0.00001 ? color.rgb/color.a : 0;
                color*=i.color;
                if (_UseSourceBounds>0.5)
                    color.a*=step(0,uv.x)*step(0,uv.y)*step(uv.x,1)*step(uv.y,1);
                // Exact endpoints: no gold/no clipping at zero, no surviving specks at one.
                if (_Progress>=1) return 0;
                if (_Progress>0)
                {
                    // Noise uses local coordinates, not sprite-atlas UVs; holes keep their aspect ratio.
                    float2 p=uv*float2(_EffectRect.z/_EffectRect.w,1)*max(.1,_NoiseScale)+_NoiseOffset.xy;
                    float field=(noise(p)*4+noise(p*2.03+17.1)*2+noise(p*4.07+31.7))/7;
                    float directed=saturate(.5+dot(uv-.5,_Direction.xy)/max(.001,abs(_Direction.x)+abs(_Direction.y)));
                    field=lerp(field,directed,saturate(_DirectionWeight));
                    float soft=max(.001,_EdgeSoftness), width=max(0,_EdgeWidth);
                    float threshold=lerp(-width-2*soft,1+2*soft,_Progress);
                    float coverage=smoothstep(threshold-soft,threshold+soft,field);
                    float edge=1-smoothstep(threshold+soft,threshold+soft+max(width,.00001),field);
                    if (width<=0) edge=0;
                    float rimPosition=saturate((field-threshold-soft)/max(width,.00001));
                    float4 rim=lerp(_EdgeHighlight,_EdgeColor,rimPosition);
                    color.rgb=lerp(color.rgb,rim.rgb,edge*rim.a);
                    color.a*=coverage;
                }
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask=saturate((_ClipRect.zw-_ClipRect.xy-abs(i.mask.xy))*i.mask.zw);
                color.a*=mask.x*mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a-.001);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
}
