shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled;

uniform float charge : hint_range(0.0, 1.0) = .2;
uniform float pulse : hint_range(0.0, 1.0) = 0.0;
uniform float age = 0.0;

void vertex() {
    mat4 facing = mat4(
        INV_VIEW_MATRIX[0] * length(MODEL_MATRIX[0].xyz),
        INV_VIEW_MATRIX[1] * length(MODEL_MATRIX[1].xyz),
        INV_VIEW_MATRIX[2] * length(MODEL_MATRIX[2].xyz),
        MODEL_MATRIX[3]);
    MODELVIEW_MATRIX = VIEW_MATRIX * facing;
}

float soft_arc(float offset, float width) {
    return exp(-pow(abs(offset) / width, 2.0));
}

void fragment() {
    vec2 uv = (UV - .5) * 2.0;
    uv.y = -uv.y;
    float size = mix(.22, 1.0, smoothstep(0.0, 1.0, charge));
    vec2 p = uv / size;
    float r = length(p);
    float longitude = atan(p.y, p.x);
    float front = sqrt(max(0.0, 1.0 - pow(r / .52, 2.0)));
    float core = 1.0 - smoothstep(.503, .524, r);

    float twist = longitude * 6.0 + front * 14.5 - age * (1.6 + charge * .8);
    float flow = sin(twist + sin(twist * .48 + p.y * 15.0) * 1.7);
    float strands = soft_arc(sin(twist * 1.72 + flow * .95), .29) * .48
        + soft_arc(sin(twist * 2.7 - p.x * 11.0), .22) * .23;
    float light = smoothstep(-.57, .72, dot(vec3(p * 1.9, front), normalize(vec3(-.55, .65, .78))));
    float rim = soft_arc(r - .511, .019);
    float hot_spot = soft_arc(length(p - vec2(-.13, .15)), .18);
    float ripple = .7 + .3 * sin(longitude * 9.0 + age * 3.6 + r * 24.0);
    float corona = soft_arc(r - .54, .14) * (.45 + .55 * ripple);
    float flare = soft_arc(r - .69 - (1.0 - pulse) * .13, .038) * pulse * (.4 + .6 * ripple);

    vec3 body = mix(vec3(.15, .002, .019), vec3(.85, .045, .10), light);
    body += vec3(1.0, .25, .23) * (strands * (.48 + charge * .36) + hot_spot * .19);
    vec3 exterior = vec3(1.0, .025, .10) * corona * .7 + vec3(1.0, .15, .075) * flare;
    vec3 surface = mix(exterior, body, core);
    surface += vec3(1.0, .46, .36) * rim * (.54 + charge * .36);
    float envelope = .54 + charge * .46;
    ALBEDO = surface * envelope;
    ALPHA = max(core, rim * .75 + corona * .77 + flare * .7) * (1.0 - smoothstep(.87, .997, length(uv)));
}
