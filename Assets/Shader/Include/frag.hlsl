float4 frag(in_frag i) : SV_Target
{
    float4 main_color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
    //AlphaTest(main_color);
    main_color *= _MainColor;
    float4 color = main_color;
    
    float3 pos_w = i.pos_w;
    float2 uv = i.uv;
    float3 normal_w = i.normal_w;
    float3 normal = i.normal;
    
    float3 v_view = GetWorldSpaceNormalizeViewDir(pos_w);
    //float3 v_view = ViewDirection(pos_w);
    float3 light_color = 0;
    float3 v_light = LightDirection(pos_w, light_color);

    float4 pack1 = SAMPLE_TEXTURE2D(_Packing1Tex, sampler_Packing1Tex, uv);
    float4 pack2 = SAMPLE_TEXTURE2D(_Packing2Tex, sampler_Packing2Tex, uv);
    float shadow_mask = 1;
    
    float3 totalLight = 1;
    totalLight = PointLight(totalLight, pos_w, i.vertex, normal_w);
    
    color.rgb = ShadowColor(color.rgb, uv, pack1,  pos_w, v_light, normal_w, shadow_mask);
    float3 debug_ramp = main_color.rgb;
    color.rgb = Ramp(color.rgb, uv, pack1, pack2, normal_w, v_view, debug_ramp);
    
    //color.rgb = dot(normal_w, v_light);
    //color.rgb = 1 - dot(v_view, normal_w) + pack1.r;
    //return color;
   
    color.rgb *= light_color;
     
    color.rgb = RimLight(color.rgb, uv, pack1, normal_w, v_view, v_light);
    color.rgb *= totalLight;
    //color.rgb = totalLight;
    
    return color;
}