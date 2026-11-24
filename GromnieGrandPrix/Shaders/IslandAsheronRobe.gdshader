shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D cloth_texture : source_color, hint_default_white, filter_linear_mipmap_anisotropic;

void fragment() {
    vec3 dyed = texture(cloth_texture, UV).rgb;
    float lum = dot(dyed, vec3(0.2126, 0.7152, 0.0722));
    float chroma = max(max(dyed.r, dyed.g), dyed.b) - min(min(dyed.r, dyed.g), dyed.b);
    float ornament = smoothstep(0.07, 0.22, chroma);
    vec3 linen = vec3(0.92, 0.88, 0.80) * (0.62 + 0.38 * smoothstep(0.0, 0.56, sqrt(lum)));
    ALBEDO = mix(linen, dyed * 1.3, ornament * 0.86);
    ROUGHNESS = 0.88;
    SPECULAR = 0.23;
}
