shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform float charge : hint_range(0.0, 1.0) = 0.0;
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

float line_glow(float distance_from_line, float width) {
    float across = distance_from_line / width;
    return exp(-across * across);
}

float radial_tick(float radius, float degrees, float center, float extent, float count) {
    float segment = (fract(degrees * count / TAU + .5) - .5) * TAU / count;
    return line_glow(segment * center, .007)
        * (1.0 - smoothstep(extent * .72, extent, abs(radius - center)));
}

void fragment() {
    vec2 p = (UV - .5) * 2.0;
    float radius = length(p);
    float turn = atan(p.y, p.x);
    float size = .65 + .31 * charge + pulse * .08;
    float r = radius / size;
    float circle = line_glow(r - .65, .009) + line_glow(r - .71, .006) * .72;
    float engravings = radial_tick(r, turn - age * .22, .59, .027, 24.0);
    engravings += radial_tick(r, turn + .12 + age * .16, .76, .020, 12.0) * .90;

    // A rotating engraved star inside the stationary seal. Its crossing strokes read as a knot at race speed.
    float star_turn = abs(mod(turn + age * .31 + PI / 4.0, PI / 2.0) - PI / 4.0);
    float star = (.26 + .25 * pow(max(0.0, cos(star_turn * 2.0)), 4.0));
    float knot = line_glow(r - star, .010);
    float gem_turn = mod(turn - age * .28 + PI / 4.0, PI / 2.0) - PI / 4.0;
    vec2 jewel = vec2(r * cos(gem_turn) - .68, r * sin(gem_turn));
    float stones = 1.0 - smoothstep(.034, .055, abs(jewel.x) + abs(jewel.y));

    float bloom = exp(-r * r * 15.0) * (.36 + .25 * charge + .16 * pulse);
    float spark = exp(-r * r * 111.0) * (.78 + .22 * cos(age * 4.9));
    vec2 flares = abs(p * mat2(vec2(.87, -.49), vec2(.49, .87)));
    float rays = (line_glow(flares.x, .012) * exp(-flares.y * 5.6)
        + line_glow(flares.y, .012) * exp(-flares.x * 5.6)) * .48;
    float halo = line_glow(r - .68, .064) * .34;
    float wave_radius = .68 + .31 * (1.0 - pulse);
    float burst = line_glow(radius - wave_radius, .035) * pulse;

    vec3 gold = vec3(1.0, .62, .20);
    vec3 pale = vec3(1.0, .91, .65);
    vec3 cyan = vec3(.29, .84, 1.0);
    float energy = .41 + .58 * charge + .47 * pulse;
    vec3 light = gold * (halo + circle * .76 + bloom) + pale * (stones * .90 + knot * .73 + spark + rays)
        + cyan * (engravings * .72 + burst * .9);
    ALBEDO = clamp(light * energy * 1.39, vec3(0.0), vec3(1.0));
    ALPHA = clamp((circle + engravings + knot + stones + spark + bloom + rays + halo + burst) * energy, 0.0, 1.0);
}
