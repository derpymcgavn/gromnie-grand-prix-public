shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D native_stone : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
varying float course_fraction;

void vertex() {
    course_fraction = COLOR.r;
}

void fragment() {
    float depth = smoothstep(.1, .79, course_fraction) * (1. - smoothstep(.90, .98, course_fraction));
    vec2 grid = vec2(UV.x * 6., UV.y);
    grid.x += sin(grid.y * .49 + 4.5 * UV.x) * depth * .32;
    grid.y += sin(grid.x * 1.55 + grid.y * .12) * depth * .27;
    vec2 soft_cell = smoothstep(vec2(.47), vec2(.53), fract(grid * .5));
    float checker = abs(soft_cell.x - soft_cell.y);
    vec3 stone = texture(native_stone, grid * .46).rgb;
    vec3 pale = mix(vec3(.57, .56, .44), vec3(.20, .14, .29), depth);
    vec3 dark = mix(vec3(.10, .14, .15), vec3(.027, .025, .067), depth);
    float edge = smoothstep(.92, .985, abs(2. * UV.x - 1.));
    float seam = min(min(fract(grid.x), 1. - fract(grid.x)), min(fract(grid.y), 1. - fract(grid.y)));
    vec3 guiding = mix(vec3(.21, .76, .63), vec3(.81, .27, .53), depth);
    float mortar = mix(.61, 1., smoothstep(.018, .056, seam));
    ALBEDO = mix(mix(dark, pale, checker) * mix(vec3(.66), stone, .46) * mortar, guiding * .5, edge);
    ROUGHNESS = mix(.81, .53, depth);
    METALLIC = depth * .16;
    EMISSION = ALBEDO * .20 + guiding * (edge * .65);
}
