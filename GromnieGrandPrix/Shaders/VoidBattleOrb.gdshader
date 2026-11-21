shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled;

uniform vec4 tint : source_color = vec4(1.0, .1, .14, 1.0);
uniform float phase = 0.0;

void fragment() {
    vec2 n = UV * vec2(25.0, 12.0);
    float winding = n.x + sin(n.y * .82 + phase * 11.0) * 1.25 + phase * 7.5;
    float ridges = pow(.5 + .5 * sin(winding * 1.2 + .66 * cos(n.y + winding * .72)), 5.0);
    float center = max(dot(normalize(NORMAL), normalize(VIEW)), 0.0);
    float rim = pow(1.0 - center, 2.2);
    vec3 body = tint.rgb * (.42 + center * .57 + ridges * .86);
    ALBEDO = mix(body, vec3(1.0, .86, .71), clamp(rim * .57 + ridges * .28, .0, .83));
}
