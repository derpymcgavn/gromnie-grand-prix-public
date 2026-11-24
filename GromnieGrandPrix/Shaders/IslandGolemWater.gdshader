shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D native_water : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;

void fragment() {
    vec2 drift = vec2(0.04, -0.11) * TIME;
    vec3 ripples = texture(native_water, UV * 1.45 + drift).rgb;
    float bands = sin(UV.y * 28.0 + UV.x * 11.0 - TIME * 4.7);
    float foam = smoothstep(0.34, 0.68, ripples.b * 0.75 + bands * 0.13);
    float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.7);
    ALBEDO = mix(vec3(0.018, 0.18, 0.34), vec3(0.26, 0.73, 0.77), foam * 0.68 + rim * 0.26);
    EMISSION = vec3(0.08, 0.26, 0.34) * (rim * 0.33 + foam * 0.14);
    ROUGHNESS = 0.28;
    SPECULAR = 0.52;
}
