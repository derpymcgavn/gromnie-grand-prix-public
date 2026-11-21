shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, fog_disabled;

void fragment() {
    vec3 normal = normalize(NORMAL);
    float rim = pow(1.0 - abs(dot(normal, normalize(VIEW))), 2.0);
    float above = smoothstep(-.25, .95, normal.y);
    ALBEDO = mix(vec3(.53, .15, .19), vec3(.85, .36, .41), rim);
    ALPHA = (.07 + .22 * above + .57 * rim) * .62;
}
