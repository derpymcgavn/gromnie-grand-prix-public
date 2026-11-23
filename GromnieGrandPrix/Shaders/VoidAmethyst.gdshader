shader_type spatial;
render_mode blend_premul_alpha, cull_back, depth_prepass_alpha, fog_disabled;

varying vec3 mineral_position;

void vertex() {
    mineral_position = VERTEX;
    float seed = fract(dot(MODEL_MATRIX[3].xyz, vec3(.073, .051, .037)));
    float phase = seed * TAU;
    float age = TIME * mix(.53, .74, seed) + phase;
    vec3 drift = vec3(sin(age * .78 + phase), sin(age) * mix(1.7, 2.7, seed), cos(age * .87 - phase) * 1.4);
    // Work in world units so thin and tall stones drift and turn by the same amount.
    mat3 axes = mat3(MODEL_MATRIX);
    vec3 axis_scale_squared = vec3(dot(axes[0], axes[0]), dot(axes[1], axes[1]), dot(axes[2], axes[2]));
    float yaw = .21 * sin(age * .71 - phase);
    float c = cos(yaw);
    float s = sin(yaw);
    mat3 turn = mat3(vec3(c, 0.0, -s), vec3(0.0, 1.0, 0.0), vec3(s, 0.0, c));
    VERTEX = (transpose(axes) * (turn * (axes * VERTEX) + drift)) / axis_scale_squared;
    NORMAL = normalize(transpose(axes) * (turn * (MODEL_NORMAL_MATRIX * NORMAL)));
}

void fragment() {
    float fracture = UV2.x;
    float tip = UV2.y;
    vec3 p = mineral_position;
    // Broad mineral growth bands and smaller inclusions stay anchored inside the rock.
    float grain = sin(dot(p, vec3(31.0, 73.0, 47.0))) * sin(dot(p, vec3(-44.0, 57.0, 32.0)));
    float band = .5 + .5 * sin(dot(p, vec3(7.7, 9.2, -6.3)) + .55 * sin(dot(p, vec3(21.0, 3.0, -16.0))));
    float inclusions = smoothstep(.60, .94, grain) * (.23 + fracture * .50);
    vec3 violet = mix(vec3(.17, .008, .35), COLOR.rgb * vec3(.73, .35, .84), .66);
    violet *= mix(.73, 1.17, band) * (1.0 - inclusions * .40);
    vec3 rock = mix(vec3(.075, .052, .109), vec3(.21, .16, .24), band * .68 + inclusions * .24);
    vec3 stone = mix(violet, rock, fracture * .83);
    float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
    float opacity = 1.0 - smoothstep(.5, 1.0, tip) * (.027 + facing * .055);

    ALBEDO = stone;
    ROUGHNESS = mix(.055 + inclusions * .13, .56, fracture);
    METALLIC = .24 * (1.0 - fracture);
    SPECULAR = mix(.89, .26, fracture);
    EMISSION = stone * mix(.31, .06, fracture);
    ALPHA = opacity;
    PREMUL_ALPHA_FACTOR = opacity;
}
