// Occlusore invisibile: scrive SOLO profondita', nessun colore.
//
// A cosa serve: nelle scene-area il bosco lo si vede come Gaussian Splatting, ma il pass LCC
// disegna dopo tutto il resto e la profondita' che scrive (SetZDepth) non basta a far sparire
// dietro i tronchi la geometria che aggiungiamo noi — i segni di misura si vedevano anche
// attraverso gli alberi. La mesh OBJ del rilievo, che in scena c'e' gia' per i collider, e'
// allineata agli splat e contiene i tronchi: disegnandola con questo materiale riempie il
// depth buffer senza comparire, e i segni dietro un fusto vengono occlusi da Unity, per pixel,
// senza dipendere da come l'SDK ordina i propri pass.
//
// Le tre righe che contano:
//  - ColorMask 0  : non tinge un solo pixel, e' un fantasma che esiste solo in profondita';
//  - Cull Off     : la mesh delle aree e' SPECCHIATA (scala X = -1), quindi il winding e'
//                   invertito e con il culling normale meta' delle facce non verrebbe disegnata
//                   (stesso motivo per cui serve Physics.queriesHitBackfaces per i raggi);
//  - Offset 2, 2  : spinge la profondita' un filo PIU' LONTANO. Senza, l'occlusore e la nuvola
//                   che rappresenta sono complanari e l'occlusore nasconderebbe gli splat
//                   stessi. Se in visore compaiono buchi nella nuvola, alza questi due numeri;
//                   se i segni continuano a passare attraverso i tronchi, abbassali.
//
// Queue Geometry-100: disegna PRIMA della geometria opaca ordinaria, cosi' quando i segni si
// disegnano il depth buffer contiene gia' i tronchi.
//
// Il materiale che usa questo shader va assegnato in scena: solo cosi' lo shader entra nella
// build Android (uno shader non referenziato viene rimosso — la lezione piu' generale del
// progetto).
Shader "Artemis/DepthOnlyOccluder"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry-100"
        }

        Pass
        {
            Name "DepthOnlyOccluder"

            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off
            Offset 2, 2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;   // mai visibile: ColorMask 0 scarta comunque questo valore
            }
            ENDHLSL
        }
    }

    Fallback Off
}
