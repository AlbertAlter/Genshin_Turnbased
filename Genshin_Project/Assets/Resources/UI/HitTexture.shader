Shader "UI/HitTexture"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
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
        Blend SrcAlpha [_DstBlend]
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
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 effect:TEXCOORD1; float4 style:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 effect:TEXCOORD1; float4 style:TEXCOORD2; float4 mask:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; o.effect=v.effect; o.style=v.style;
                float2 pixel=o.vertex.w; pixel/=abs(mul((float2x2)UNITY_MATRIX_P,_ScreenParams.xy));
                float4 rect=clamp(_ClipRect,-2e10,2e10);
                o.mask=float4(v.vertex.xy*2-rect.xy-rect.zw,.25/(.25*float2(_UIMaskSoftnessX,_UIMaskSoftnessY)+abs(pixel)));
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float4 texel=tex2D(_MainTex,i.uv);
                // Low-opacity glow uses a true spatial blur, not stacks of opaque coloured lines.
                if (i.style.x>0)
                {
                    float2 d=i.style.xy;
                    texel=(texel*4+tex2D(_MainTex,i.uv+float2(d.x,0))*2+tex2D(_MainTex,i.uv-float2(d.x,0))*2
                        +tex2D(_MainTex,i.uv+float2(0,d.y))*2+tex2D(_MainTex,i.uv-float2(0,d.y))*2
                        +tex2D(_MainTex,i.uv+d)+tex2D(_MainTex,i.uv-d)
                        +tex2D(_MainTex,i.uv+float2(d.x,-d.y))+tex2D(_MainTex,i.uv+float2(-d.x,d.y)))/16;
                }
                float edge=max(.001,i.effect.w);
                float visible=1-smoothstep(i.effect.y*(1+edge)-edge,i.effect.y*(1+edge),i.effect.x);
                float retained=smoothstep(i.effect.z*(1+edge)-edge,i.effect.z*(1+edge),i.effect.x);
                // Grayscale intensity avoids baking accidental coloured fringes into the common element palette.
                float light=dot(texel.rgb,float3(.299,.587,.114));
                float3 rgb=lerp(i.color.rgb,float3(1,1,1),saturate(light*i.style.z))*light;
                float alpha=texel.a*i.color.a*visible*retained;
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
