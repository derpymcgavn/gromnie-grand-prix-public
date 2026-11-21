shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled;

uniform vec4 tint : source_color = vec4(1.0, .28, .20, 1.0);
uniform float impact_active : hint_range(0.0, 1.0) = 0.0;
uniform float pulse : hint_range(0.0, 1.0) = 0.0;
uniform float phase = 0.0;
uniform int spell = 0;

void fragment() {
    vec2 border_px = min(UV, 1.0 - UV) / max(fwidth(UV), vec2(.001));
    float edge = 1.0 - smoothstep(1.3, 3.0, min(border_px.x, border_px.y));
    float ribbon = 1.0 - smoothstep(.035, .128, min(UV.x, 1.0 - UV.x));
    float stripe = 1.0 - smoothstep(.19, .33, abs(fract(UV.y * 7.5 - UV.x * 3.0 - phase * 1.1) - .5));
    float c = stripe;
    if (spell == 1)
        c = max(stripe * .45, 1.0 - smoothstep(.072, .17, abs(fract(UV.y * 6.0) - .5)));
    if (spell == 2)
        c = 1.0 - smoothstep(.21, .35, abs(fract(UV.y * 5.0 + abs(UV.x - .5) * 5.0 - phase * 1.8) - .5));
    float lines = max(edge, c * mix(.55, .86, impact_active));
    vec3 warning_color = mix(tint.rgb, vec3(1.0, .85, .63), .35);
    vec3 energy = mix(warning_color, tint.rgb, impact_active);
    ALBEDO = mix(energy * mix(.52, 1.35, impact_active), vec3(1.0, .91, .75), edge * .49);
    ALPHA = clamp((.15 + .24 * c + .28 * ribbon + .42 * lines) * (.64 + pulse * .36) + impact_active * .23, 0.0, .93);
}
