Shader "UI/FrozenShards"
{
    Properties
    {
        [PerRendererData] _MainTex ("Ice", 2D) = "white" {}
        _ClipTex ("Full card silhouette", 2D) = "white" {}
        _SurfaceTex ("Fixed intact ice facets", 2D) = "black" {}
        _SurfaceOpacity ("Fixed surface opacity", Range(0,1)) = 0.65
        _CrackTex1 ("First added cracks", 2D) = "black" {}
        _CrackTex2 ("Second added cracks", 2D) = "black" {}
        _Crack1Amount ("First crack visibility", Range(0,1)) = 0
        _Crack2Amount ("Second crack visibility", Range(0,1)) = 0
        _Color ("Tint", Color) = (1,1,1,1)
        _IceColor ("Ice body", Color) = (0.24,0.62,0.85,1)
        _DetailColor ("Ice lines", Color) = (0.76,0.95,1,1)
        _OutlineColor ("Ice outline", Color) = (0.2,0.68,0.9,1)
        _BodyOpacity ("Body opacity", Range(0,1)) = 0.25
        _DetailOpacity ("Line opacity", Range(0,1)) = 0.9
        _DetailWidthPixels ("Line expansion in screen pixels", Float) = 0.9
        _OutlineOpacity ("Outline opacity", Range(0,1)) = 0.85
        _OutlineWidthPixels ("Outline width in screen pixels", Float) = 2
        _TextureContrast ("Ice facet contrast", Float) = 2.4
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
            sampler2D _MainTex, _ClipTex, _CrackTex1, _CrackTex2, _SurfaceTex;
            fixed4 _Color, _TextureSampleAdd;
            fixed4 _IceColor, _DetailColor, _OutlineColor;
            float _BodyOpacity, _DetailOpacity, _DetailWidthPixels, _OutlineOpacity, _OutlineWidthPixels, _TextureContrast;
            float _Crack1Amount, _Crack2Amount;
            float _SurfaceOpacity;
            float4 _ClipRect;
            v2f vert(appdata v)
            {
                v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color*_Color; return o;
            }
            float IceSignal(float2 uv)
            {
                fixed4 sampled = tex2D(_MainTex, uv) + _TextureSampleAdd;
                #ifndef UNITY_COLORSPACE_GAMMA
                sampled.rgb = LinearToGammaSpace(sampled.rgb);
                #endif
                return sampled.r * saturate(sampled.a * 4);
            }
            float CrackSignal(float2 uv)
            {
                float first = tex2D(_CrackTex1,uv).a * _Crack1Amount;
                float second = tex2D(_CrackTex2,uv).a * _Crack2Amount;
                return first + second * (1-first);
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 ice = tex2D(_MainTex,i.uv) + _TextureSampleAdd;
                // Derivatives use original UVs, so batching, shake and shard motion do not change the sampling space.
                float2 pixel = max(fwidth(i.uv), float2(0.000001,0.000001));
                float2 detailStep = pixel * _DetailWidthPixels;
                float signal = IceSignal(i.uv);
                float peak = max(CrackSignal(i.uv), max(max(CrackSignal(i.uv + float2(detailStep.x,0)),
                    CrackSignal(i.uv - float2(detailStep.x,0))), max(CrackSignal(i.uv + float2(0,detailStep.y)),
                    CrackSignal(i.uv - float2(0,detailStep.y)))));
                float detailAlpha = peak * _DetailOpacity;
                float facet = saturate((signal - 0.5) * _TextureContrast + 0.5);
                float3 body = _IceColor.rgb * lerp(0.65,1.15,facet);
                // The intact surface keeps its authored light/dark facets at an independent strength.
                // Only the two damage overlays change with clicks; the body and surface never switch.
                fixed4 surface = tex2D(_SurfaceTex,i.uv);
                float surfaceAlpha = surface.a * _SurfaceOpacity;
                float alpha = _BodyOpacity * (1-surfaceAlpha) + surfaceAlpha;
                float3 premultiplied = body * _BodyOpacity * (1-surfaceAlpha) + surface.rgb * surfaceAlpha;
                alpha = alpha * (1-detailAlpha) + detailAlpha;
                premultiplied = premultiplied * (1-detailAlpha) + _DetailColor.rgb * detailAlpha;

                float silhouette = tex2D(_ClipTex,i.uv).a;
                float2 outlineStep = pixel * _OutlineWidthPixels;
                float inset = min(min(tex2D(_ClipTex,i.uv + float2(outlineStep.x,0)).a,
                    tex2D(_ClipTex,i.uv - float2(outlineStep.x,0)).a),
                    min(tex2D(_ClipTex,i.uv + float2(0,outlineStep.y)).a,
                    tex2D(_ClipTex,i.uv - float2(0,outlineStep.y)).a));
                float outlineAlpha = (1-inset) * _OutlineOpacity;
                premultiplied = premultiplied * (1-outlineAlpha) + _OutlineColor.rgb * outlineAlpha;
                alpha = alpha * (1-outlineAlpha) + outlineAlpha;
                fixed4 c = fixed4(premultiplied / max(alpha,0.00001) * i.color.rgb,
                    alpha * ice.a * silhouette * i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
