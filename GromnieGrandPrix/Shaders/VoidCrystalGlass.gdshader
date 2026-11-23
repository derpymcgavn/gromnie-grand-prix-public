shader_type spatial;
render_mode blend_premul_alpha, cull_back, depth_draw_never, fog_disabled;

uniform vec4 crystal_tint : source_color = vec4(.64, .80, 1.0, 1.0);
uniform bool use_vertex_tint = true;
uniform bool use_native_texture = false;
uniform bool trackside_amethyst = false;
uniform sampler2D native_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 aerbax_light;
uniform vec3 asheron_light;
varying vec3 world_position;
varying float crystal_pulse;

void vertex() {
    crystal_pulse = 0.0;
    if (trackside_amethyst) {
        float seed = fract(dot(MODEL_MATRIX[3].xyz, vec3(.073, .051, .037)));
        float phase = seed * TAU;
        float age = TIME * mix(.53, .74, seed) + phase;
        float bob = sin(age);
        vec3 drift = vec3(sin(age * .78 + phase), bob * mix(1.7, 2.7, seed), cos(age * .87 - phase) * 1.4);
        // The instance axes are orthogonal even though the prisms have different scales.
        mat3 axes = mat3(MODEL_MATRIX);
        vec3 axis_scale_squared = vec3(dot(axes[0], axes[0]), dot(axes[1], axes[1]), dot(axes[2], axes[2]));
        float yaw = .21 * sin(age * .71 - phase);
        float c = cos(yaw);
        float s = sin(yaw);
        mat3 turn = mat3(vec3(c, 0.0, -s), vec3(0.0, 1.0, 0.0), vec3(s, 0.0, c));
        VERTEX = (transpose(axes) * (turn * (axes * VERTEX) + drift)) / axis_scale_squared;
        NORMAL = normalize(transpose(axes) * (turn * (MODEL_NORMAL_MATRIX * NORMAL)));
        crystal_pulse = .5 + .5 * bob;
    }
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec3 normal = normalize(NORMAL);
    vec3 eye = normalize(VIEW);
    float facing = clamp(dot(normal, eye), 0.0, 1.0);
    float fresnel = pow(1.0 - facing, 4.5);
    vec3 reflected_ray = normalize(mat3(INV_VIEW_MATRIX) * reflect(-eye, normal));

    vec4 texel = use_native_texture ? texture(native_texture, UV) : vec4(1.0);
    vec3 facet_base = trackside_amethyst ? vec3(.105, .021, .22) : vec3(.09, .17, .25);
    vec3 facets = use_vertex_tint ? mix(facet_base, COLOR.rgb, .86) : crystal_tint.rgb;
    vec3 tint = mix(facets, facets * texel.rgb, .65);
    float facet_edge = 0.0;
    if (use_vertex_tint) {
        vec3 barycentric = vec3(UV, 1.0 - UV.x - UV.y);
        vec3 edge_width = clamp(fwidth(barycentric), vec3(.003), vec3(.034));
        vec3 inset = smoothstep(edge_width * .35, edge_width * 1.45, barycentric);
        facet_edge = 1.0 - min(inset.x, min(inset.y, inset.z));
    }
    float opacity = (.17 + .33 * fresnel + .12 * facet_edge) * texel.a;

    // The dim Void sky has limited radiance. Let the broad red and gold
    // spell lights read on a face only when it turns toward their reflection.
    vec3 red_direction = normalize(aerbax_light - world_position);
    vec3 gold_direction = normalize(asheron_light - world_position);
    float red = pow(max(0.0, dot(reflected_ray, red_direction)), 20.0);
    float gold = pow(max(0.0, dot(reflected_ray, gold_direction)), 20.0);
    float sky = pow(max(0.0, dot(reflected_ray, normalize(vec3(.28, .80, -.52)))), 3.0);
    float nebula_band = exp(-abs(dot(reflected_ray, normalize(vec3(.35, .92, .18)))) * 5.0);
    vec3 glints = vec3(.87, .14, .24) * red + vec3(.88, .62, .24) * gold
        + vec3(.30, .47, .92) * sky + vec3(.32, .18, .50) * nebula_band;

    ALBEDO = tint;
    METALLIC = 0.0;
    ROUGHNESS = .085;
    SPECULAR = .65;
    ALPHA = opacity;
    PREMUL_ALPHA_FACTOR = opacity;
    vec3 edge_tint = mix(tint, trackside_amethyst ? vec3(.68, .33, .94) : vec3(.53, .68, .91), .57);
    float glow = trackside_amethyst ? mix(.48, .69, crystal_pulse) : .29;
    EMISSION = tint * opacity * glow + glints * (.22 + .58 * fresnel) * texel.a
        + edge_tint * facet_edge * (.14 + .18 * fresnel);
}
