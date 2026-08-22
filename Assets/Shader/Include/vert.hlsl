in_frag vert(in_vert v)
{
    in_frag o;
    float4 vertex_pos = v.vertex;
    
    o.pos_w = mul(unity_ObjectToWorld, vertex_pos).xyz;
    o.vertex = TransformObjectToHClip(vertex_pos.xyz);
    o.normal = v.normal.xyz;
    
    o.normal_w = TransformObjectToWorldNormal(o.normal);
    o.uv = v.uv;
    o.color = v.color;
    return o;
}