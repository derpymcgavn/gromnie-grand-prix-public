shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform vec4 tint : source_color = vec4(.37, .28, .72, 1.0);
uniform float seed = 0.0;

float strand(float across, float width) {
    float line = across / width;
    return exp(-line * line);
}

void fragment() {
    float along = UV.x;
    float across = UV.y * 2.0 - 1.0;
    float wind = sin(along * 14.5 + seed * 4.7 + TIME * .14) * .24;
    float arc = strand(across - wind, .12);
    arc += strand(across + wind * .7 - .27, .085) * .69;
    arc += strand(across + wind * .6 + .31, .16) * .41;
    float breaks = .26 + .74 * smoothstep(-.76, -.04, sin(along * 18.3 + seed * 9.4) + sin(along * 58.1) * .2);
    float tail = smoothstep(.0, .16, along) * (1.0 - smoothstep(.77, 1.0, along));
    float ripple = .64 + .36 * (sin(along * 95.0 + seed * 6.5 - TIME * .85) * .5 + .5);
    ALBEDO = tint.rgb * (arc * .64 + .36);
    ALPHA = clamp((arc * .51 + strand(across, .43) * .09) * breaks * tail * ripple, 0.0, .66);
}
