Shader "UI/FrozenShards"
{
    Properties
    {
        [PerRendererData] _MainTex ("Ice", 2D) = "white" {}
        _ClipTex ("Full card silhouette", 2D) = "white" {}
        _BackdropTex ("Premultiplied card backdrop", 2D) = "white" {}
        _SurfaceTex ("Fixed intact ice facets", 2D) = "black" {}
        _CrackTex1 ("First added cracks", 2D) = "black" {}
        _CrackTex2 ("Second added cracks", 2D) = "black" {}
        _Crack1Progress ("First elliptical reveal", Range(0,1)) = 0
        _Crack2Progress ("Second elliptical reveal", Range(0,1)) = 0
        _RevealCenter ("Reveal center", Vector) = (0.5,0.52,0,0)
        _RevealFeatherPixels ("Elliptical edge feather", Float) = 2
        _SecondRevealEasePower ("Second reveal ease-out power", Float) = 2
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
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
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            sampler2D _MainTex, _ClipTex, _BackdropTex, _CrackTex1, _CrackTex2, _SurfaceTex;
            fixed4 _Color, _TextureSampleAdd;
            float _Crack1Progress, _Crack2Progress, _RevealFeatherPixels, _SecondRevealEasePower;
            float4 _RevealCenter;
            float4 _ClipRect;
            v2f vert(appdata v)
            {
                v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float3 ToPSDColor(float3 rgb)
            {
                #ifdef UNITY_COLORSPACE_GAMMA
                return rgb;
                #else
                return LinearToGammaSpace(rgb);
                #endif
            }
            float3 FromPSDColor(float3 rgb)
            {
                #ifdef UNITY_COLORSPACE_GAMMA
                return rgb;
                #else
                return GammaToLinearSpace(rgb);
                #endif
            }
            float3 ColorDodge(float3 backdrop, float3 blend)
            {
                return saturate(backdrop / max(1-blend,0.000001));
            }
            float Reveal(float2 uv, float progress)
            {
                if (progress <= 0) return 0;
                if (progress >= 1) return 1;
                float2 center = _RevealCenter.xy;
                // A circle in normalized UV becomes the requested ellipse on the portrait card.
                float distanceFromCenter = length(uv-center);
                float maxRadius = length(max(center,1-center));
                float feather = max(fwidth(distanceFromCenter)*_RevealFeatherPixels,0.000001);
                float radius = (maxRadius+feather)*progress;
                return 1-smoothstep(radius-feather,radius,distanceFromCenter);
            }
            float4 frag(v2f i):SV_Target
            {
                float4 captured = tex2D(_BackdropTex,i.uv);
                float3 backdrop = captured.rgb / max(captured.a,0.000001);
                float3 result = ToPSDColor(backdrop);
                float4 body = tex2D(_MainTex,i.uv) + _TextureSampleAdd;
                float4 surface = tex2D(_SurfaceTex,i.uv);
                float4 first = tex2D(_CrackTex1,i.uv);
                float4 second = tex2D(_CrackTex2,i.uv);
                first.a *= Reveal(i.uv,_Crack1Progress);
                float secondProgress = 1-pow(1-_Crack2Progress,_SecondRevealEasePower);
                second.a *= Reveal(i.uv,secondProgress);
                // PSD layer/fill opacity has already been baked once into each PNG alpha.
                result = lerp(result,ToPSDColor(body.rgb),body.a);
                float3 screen = 1-(1-result)*(1-ToPSDColor(surface.rgb));
                result = lerp(result,screen,surface.a);
                result = lerp(result,ColorDodge(result,ToPSDColor(first.rgb)),first.a);
                result = lerp(result,ColorDodge(result,ToPSDColor(second.rgb)),second.a);
                result = FromPSDColor(result);

                // Emit only the ice as a source-over overlay, rather than an opaque copy of the card.
                // This keeps the underlying graphics visible and prevents character pictures on flying shards.
                float alpha = 1-(1-body.a)*(1-surface.a)*(1-first.a)*(1-second.a);
                float3 delta = result-backdrop;
                float3 requiredAlpha = max(max(delta,0)/max(1-backdrop,0.000001),
                    max(-delta,0)/max(backdrop,0.000001));
                alpha = saturate(max(alpha,max(requiredAlpha.r,max(requiredAlpha.g,requiredAlpha.b))));
                float3 overlay = saturate((result-backdrop*(1-alpha))/max(alpha,0.000001));
                float4 c = float4(overlay*i.color.rgb,alpha*tex2D(_ClipTex,i.uv).a*i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
