Shader "UI/PsdHealthLayer"
{
    Properties
    {
        [PerRendererData] _MainTex ("Individual PSD layer", 2D) = "white" {}
        _BaseTex ("PSD red shape for clipping and Soft Light", 2D) = "white" {}
        _OverrideBaseColor ("Element-colored base", Float) = 0
        _BaseColor ("Element base color", Color) = (1,1,1,1)
        _LayerBounds ("Layer bounds in PSD canvas, bottom-left", Vector) = (0,0,176,19)
        _Mode ("Normal 0 / Multiply 1 / SoftLight 2", Float) = 0
        _VectorCircle ("Analytic shared solid circle", Float) = 0
        _VectorBase ("Analytic PSD capsule base and clipping", Float) = 0
        _Clipped ("Clip to red shape", Float) = 0
        _NormalizeWithBase ("Follow base fractional normalization without clipping", Float) = 0
        _CanvasSize ("PSD canvas", Vector) = (176,19,0,0)
        _BaseBounds ("Base shape bounds", Vector) = (3,7,168,11)
        _PsdBaseBounds ("Original fractional PSD base bounds", Vector) = (3,7,168,11)
        _UnderA ("First underlying PSD multiply layer", 2D) = "white" {}
        _UnderB ("Second underlying PSD multiply layer", 2D) = "white" {}
        _HasUnder ("Underlying multiply layers", Float) = 0
        _UnderBounds ("Underlying PSD bounds", Vector) = (0,0,1,1)
        _SrcBlend ("Source blend", Float) = 5
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
        Blend [_SrcBlend] OneMinusSrcAlpha, One OneMinusSrcAlpha
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
            sampler2D _MainTex, _BaseTex, _UnderA, _UnderB;
            float4 _LayerBounds, _ClipRect, _CanvasSize, _BaseBounds, _UnderBounds, _PsdBaseBounds;
            float _Mode, _Clipped, _HasUnder, _VectorCircle, _VectorBase;
            float _OverrideBaseColor;
            float _NormalizeWithBase;
            float4 _BaseColor;
            float capsuleAlpha(float2 uv, float2 size)
            {
                float radius = min(size.x, size.y) * .5;
                float2 q = abs((uv-.5)*size) - (size*.5-radius);
                float distance = length(max(q,0)) + min(max(q.x,q.y),0) - radius;
                return saturate(.5-distance/max(fwidth(distance),.0001));
            }
            v2f vert(appdata v) { v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            float inside(float2 uv) { return step(0,uv.x)*step(0,uv.y)*step(uv.x,1)*step(uv.y,1); }
            float3 softLight(float3 b, float3 s)
            {
                float3 d=lerp(((16*b-12)*b+4)*b,sqrt(max(b,0)),step(.25,b));
                return lerp(b-(1-2*s)*b*(1-b),b+(2*s-1)*(d-b),step(.5,s));
            }
            float4 frag(v2f i):SV_Target
            {
                if (_VectorCircle>.5)
                {
                    float4 circle=tex2D(_MainTex,float2(.5,.5))*i.color;
                    circle.a*=capsuleAlpha(i.uv,float2(1,1));
                    #ifdef UNITY_UI_CLIP_RECT
                    circle.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(circle.a-.001);
                    #endif
                    return circle;
                }
                float2 p=i.uv*_CanvasSize.xy;
                // Inverse of the base's vector-normalization transform. Sample all
                // clipped raster effects in their original PSD coordinates, including
                // the layers used to reconstruct the Soft Light backdrop.
                // Normalized Bezier PNG fallbacks need the same mapping as SDF bases.
                float2 sourceP=_PsdBaseBounds.xy+(p-_BaseBounds.xy)*_PsdBaseBounds.zw/_BaseBounds.zw;
                float2 uv=((_Clipped>.5 || _NormalizeWithBase>.5 ? sourceP : p)-_LayerBounds.xy)/_LayerBounds.zw;
                // Exported alpha already includes PSD Opacity AND Fill Opacity.
                // Do not multiply these static values again here or in Image.color.
                float4 c=tex2D(_MainTex,saturate(uv));
                c.a*=inside(uv)*i.color.a;
                float2 buv=(p-_BaseBounds.xy)/_BaseBounds.zw;
                float4 b=tex2D(_BaseTex,saturate(buv));
                b.rgb=lerp(b.rgb,_BaseColor.rgb,_OverrideBaseColor);
                if (_VectorBase>.5)
                {
                    b=tex2D(_BaseTex,float2(.5,.5));
                    b.rgb=lerp(b.rgb,_BaseColor.rgb,_OverrideBaseColor);
                    b.a*=capsuleAlpha(buv,_BaseBounds.zw);
                    if (_Mode<.5 && _Clipped<.5)
                        c=float4(b.rgb,b.a*i.color.a);
                }
                if (_Clipped>.5) c.a*=inside(buv)*b.a;
                if (_Mode<.5 && _Clipped<.5) c.rgb=lerp(c.rgb,_BaseColor.rgb,_OverrideBaseColor);
                if (_Mode>1.5)
                {
                    // Upper/lower soft-light do not overlap in these PSD templates.
                    // Both latest PSD templates have two Multiply layers below the soft lights.
                    float3 backdrop=b.rgb;
                    if (_HasUnder>.5)
                    {
                        float2 u=(sourceP-_UnderBounds.xy)/_UnderBounds.zw;
                        // The same baked alpha is required for the Soft Light backdrop;
                        // using raw PSD channel alpha here would reintroduce dark shadows.
                        float4 a=tex2D(_UnderA,saturate(u)), d=tex2D(_UnderB,saturate(u));
                        backdrop*=lerp(1,a.rgb,a.a*inside(u)*b.a);
                        backdrop*=lerp(1,d.rgb,d.a*inside(u)*b.a);
                    }
                    #ifndef UNITY_COLORSPACE_GAMMA
                    c.rgb=GammaToLinearSpace(softLight(LinearToGammaSpace(backdrop),LinearToGammaSpace(c.rgb)));
                    #else
                    c.rgb=softLight(backdrop,c.rgb);
                    #endif
                }
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                // Multiply: src*dst*a + dst*(1-a); Normal/SoftLight use ordinary alpha blend.
                if (_Mode>.5 && _Mode<1.5) c.rgb*=c.a;
                return c;
            }
            ENDCG
        }
    }
}
