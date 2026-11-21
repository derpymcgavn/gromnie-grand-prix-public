shader_type canvas_item;
render_mode unshaded;

uniform float intensity : hint_range(0.0, 1.0) = .64;

float drop_hash(vec2 cell) {
    vec3 p = fract(vec3(cell.xyx) * .1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float rain_layer(vec2 uv, vec2 count, float speed, float slant, float density) {
    vec2 field = uv;
    field.x += field.y * slant;
    field.y -= TIME * speed;
    field *= count;
    vec2 cell = floor(field);
    float seed = drop_hash(cell);
    vec2 local = fract(field);
    float axis = .13 + seed * .73;
    float line = 1.0 - smoothstep(.015, .072, abs(local.x - axis));
    float length_mask = smoothstep(.03, .32, local.y) * (1.0 - smoothstep(.46 + seed * .10, .96, local.y));
    return line * length_mask * step(density, seed);
}

void fragment() {
    vec2 uv = UV * vec2(SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x, 1.0);
    float rain = rain_layer(uv, vec2(76.0, 27.0), 1.55, .23, .81) * .19;
    rain += rain_layer(uv + vec2(.33, .71), vec2(49.0, 15.0), 2.4, .28, .87) * .29;
    rain += rain_layer(uv + vec2(.87, .17), vec2(108.0, 42.0), .98, .18, .82) * .10;
    COLOR = vec4(.72, .84, .92, rain * intensity);
}
