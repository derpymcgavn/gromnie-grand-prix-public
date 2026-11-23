shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform sampler2D clouds : source_color, repeat_disable, filter_linear_mipmap;
uniform float arm_phase = 0.0;
uniform float drift = 1.0;
uniform float strength = .82;
uniform float battle_progress : hint_range(0.0, 1.0) = 0.0;

void vertex() {
    mat4 billboard = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    MODELVIEW_MATRIX = VIEW_MATRIX * billboard;
}

vec2 rotate_dust(vec2 v, float angle) {
    float c = cos(angle);
    float s = sin(angle);
    return vec2(v.x * c - v.y * s, v.x * s + v.y * c);
}

void fragment() {
    vec2 disk = (UV - .5) * 2.0;
    float radius = length(disk);
    float spiral = log(radius + .19);
    float motion = TIME * .054 * drift;
    float angle = atan(disk.y, disk.x) + 2.45 * spiral + motion + arm_phase;
    vec4 cloud = texture(clouds, rotate_dust(disk, .42 * spiral + motion) * .42 + .5);
    vec4 fine = texture(clouds, rotate_dust(disk, -.72 * spiral - motion * 1.19) * .48 + vec2(.49, .53));
    float dust = clamp(max(cloud.a * 1.95, fine.a * 1.42), 0.0, 1.0);

    float folds = .5 + .5 * cos(angle * 3.0 + fine.a * 3.7);
    float arms = smoothstep(.34, .97, folds);
    float threads = smoothstep(.55, .995, folds) * smoothstep(.26, .64, dust);
    float rim = 1.0 - smoothstep(.49, .96, radius);
    float core = (1.0 - smoothstep(.04, .43, radius)) * (.46 + dust * .54);

    // Adjacent spell colors overlap in the dusty space between the arms.
    vec3 weights = .5 + .5 * cos(angle + vec3(-2.0944, 0.0, 2.0944));
    weights *= weights * weights;
    weights /= max(dot(weights, vec3(1.0)), .001);
    vec3 crimson = vec3(.98, .105, .23);
    vec3 violet = vec3(.53, .18, .98);
    vec3 cyan = vec3(.11, .75, .99);
    vec3 gold = vec3(1.0, .72, .36);
    vec3 dawn = mix(cyan, gold, smoothstep(.23, .79, .5 + .5 * sin(angle * 2.0 - radius * 5.0 + dust * 3.0)));
    vec3 chroma = crimson * weights.x + violet * weights.y + dawn * weights.z;
    chroma = mix(chroma, vec3(.74, .45, 1.0), core * .55);
    chroma += threads * mix(vec3(.22, .19, .29), vec3(.45, .34, .28), battle_progress);

    float cloudlight = (.38 * arms + .15 * threads) * smoothstep(.16, .78, dust);
    ALBEDO = clamp(chroma, vec3(0.0), vec3(1.0));
    ALPHA = strength * rim * (cloudlight + core * .11) * mix(.82, 1.17, battle_progress);
}
