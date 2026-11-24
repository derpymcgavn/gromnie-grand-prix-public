uniform float regatta_time;
uniform vec4 regatta_wave_x;
uniform vec4 regatta_wave_z;
uniform vec4 regatta_wave_frequency;
uniform vec4 regatta_wave_phase;
uniform vec4 regatta_wave_amplitude;
global uniform sampler2D regatta_fluid_displacement : filter_linear, repeat_disable, hint_default_black;
global uniform vec4 regatta_fluid_bounds;

vec4 regatta_phase_at(vec2 world_xz) {
    return regatta_wave_x * world_xz.x + regatta_wave_z * world_xz.y
        - regatta_wave_frequency * regatta_time + regatta_wave_phase;
}

float regatta_wind_height(vec2 world_xz) {
    return dot(regatta_wave_amplitude, sin(regatta_phase_at(world_xz)));
}

float regatta_fluid_height(vec2 world_xz) {
    float texel = regatta_fluid_bounds.w;
    if (texel <= 0.0) {
        return 0.0;
    }
    vec2 uv = (world_xz - regatta_fluid_bounds.xy) * regatta_fluid_bounds.z + texel * 0.5;
    vec2 margins = min(uv, 1.0 - uv);
    float edge = min(margins.x, margins.y);
    if (edge <= texel * 0.5) {
        return 0.0;
    }
    return textureLod(regatta_fluid_displacement, uv, 0.0).r * smoothstep(texel * 0.5, texel * 6.0, edge);
}

float regatta_wave_height(vec2 world_xz) {
    return regatta_wind_height(world_xz) + regatta_fluid_height(world_xz);
}

vec2 regatta_fluid_slope(vec2 world_xz) {
    if (regatta_fluid_bounds.z <= 0.0) {
        return vec2(0.0);
    }
    float cell = regatta_fluid_bounds.w / regatta_fluid_bounds.z;
    vec2 east = vec2(cell, 0.0);
    vec2 north = vec2(0.0, cell);
    return vec2(
        regatta_fluid_height(world_xz + east) - regatta_fluid_height(world_xz - east),
        regatta_fluid_height(world_xz + north) - regatta_fluid_height(world_xz - north)
    ) / (2.0 * cell);
}

vec2 regatta_wave_slope(vec2 world_xz) {
    vec4 derivative = regatta_wave_amplitude * cos(regatta_phase_at(world_xz));
    return vec2(dot(derivative, regatta_wave_x), dot(derivative, regatta_wave_z));
}

float regatta_geometry_strength(vec2 world_xz, vec2 camera_xz) {
    // Match the recentering of the water mesh in UpdateIslandWaterReflection.
    vec2 grid_center = floor(camera_xz / 8.0 + .5) * 8.0;
    return 1.0 - smoothstep(108.0, 175.0, length(world_xz - grid_center));
}
