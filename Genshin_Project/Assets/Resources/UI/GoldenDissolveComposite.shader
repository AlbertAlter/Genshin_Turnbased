Shader "UI/GoldenDissolveComposite"
{
    Properties { _MainTex ("Layer",2D)="white" {} _ClipTex ("Card opening",2D)="white" {} _Tint ("Tint",Color)=(1,1,1,1) }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        // Correct premultiplied source-over alpha on a transparent RenderTexture.
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float2 clipUV:TEXCOORD1; float4 color:COLOR; };
            float4x4 _CompositeMVP, _CompositeLocal;
            sampler2D _MainTex, _ClipTex;
            float4 _Tint, _CardRect;
            v2f vert(appdata v) { v2f o; o.vertex=mul(_CompositeMVP,v.vertex);o.uv=v.uv;o.color=v.color*_Tint;
                o.clipUV=(mul(_CompositeLocal,v.vertex).xy-_CardRect.xy)/_CardRect.zw;return o; }
            float4 frag(v2f i):SV_Target { float4 c=tex2D(_MainTex,i.uv)*i.color;c.a*=tex2D(_ClipTex,i.clipUV).a;c.rgb*=c.a;return c; }
            ENDHLSL
        }
    }
}
