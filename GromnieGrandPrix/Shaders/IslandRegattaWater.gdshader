shader_type spatial;
render_mode cull_back, unshaded, shadows_disabled;
#include "res://Shaders/TrackWater.gdshaderinc"
#include "res://Shaders/RegattaSurface.gdshaderinc"

uniform sampler2D native_water : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D native_deep_water : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D native_surf : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D native_sand : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D shore_distance : hint_default_white, filter_linear_mipmap, repeat_disable;
uniform sampler2D island_reflection : hint_default_black, filter_linear, repeat_disable;
uniform bool reflection_active = false;
uniform bool native_water_only = false;
uniform vec3 reflection_eye;
uniform vec2 shore_origin;
uniform vec2 shore_extent;
uniform vec3 sunlight : source_color = vec3(1.0, .91, .77);
uniform float sun_energy = 1.0;
varying vec3 world_point;
varying float wave_lift;
varying float fluid_lift;
varying vec2 fluid_gradient;

void vertex() {
    world_point = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    // Far cells become wider than a wave, so fade their geometric displacement.
    float detail = regatta_geometry_strength(world_point.xz, CAMERA_POSITION_WORLD.xz);
    fluid_lift = regatta_fluid_height(world_point.xz) * detail;
    fluid_gradient = regatta_fluid_slope(world_point.xz) * detail;
    wave_lift = regatta_wind_height(world_point.xz) * detail + fluid_lift;
    VERTEX.y += wave_lift;
    world_point.y += wave_lift;
}

vec3 island_sky(vec3 ray, vec3 horizon, vec3 zenith, float time) {
    float up = clamp(ray.y, 0.0, 1.0);
    vec3 sky = mix(horizon * .57, zenith * .95, smoothstep(0.0, .68, pow(up, .64)));
    vec2 sky_pos = ray.xz * 4.8 / (up + .32) + vec2(.019, -.013) * time;
    float clouds = water_noise(sky_pos) * .64 + water_noise(sky_pos * 2.37 + 2.81) * .36;
    return mix(sky, horizon * .80, smoothstep(.54, .83, clouds) * .30);
}

void fragment() {
    vec2 p = world_point.xz;
    vec2 map_uv = (p - shore_origin) / shore_extent;
    vec2 map_edge = min(map_uv, 1.0 - map_uv);
    float inside = smoothstep(0.0, .014, min(map_edge.x, map_edge.y));
    vec2 shoreline = texture(shore_distance, map_uv).rg;
    if (native_water_only && (min(map_edge.x, map_edge.y) < 0.0 || shoreline.g < .5)) discard;
    float shore = mix(96.0, shoreline.r * 96.0, inside);
    vec3 eye = normalize(CAMERA_POSITION_WORLD - world_point);
    float footprint = max(length(dFdx(p)), length(dFdy(p)));
    float distance_to_eye = length(VERTEX);

    vec2 drift = vec2(.141, -.073) * TIME;
    vec3 shoal = texture(native_water, p * .21 + drift).rgb;
    vec3 deep = texture(native_deep_water, p.yx * .116 - drift * .69).rgb;
    float surf_pattern = texture(native_surf, p * .66 + drift * 1.12).g;
    vec2 swells = vec2(water_noise(p * vec2(.11, .21) + drift * .55),
        water_noise(p.yx * vec2(.17, .086) - drift * .41 + 17.7)) - .5;
    vec2 running_uv = p * .54 + swells * .27 - drift * .8;
    vec2 native_slope = vec2(
        texture(native_surf, running_uv + vec2(.035, 0.0)).g - texture(native_surf, running_uv - vec2(.035, 0.0)).g,
        texture(native_surf, running_uv + vec2(0.0, .035)).g - texture(native_surf, running_uv - vec2(0.0, .035)).g);
    vec2 slope = swells * .19 + native_slope * .75 * (1.0 - smoothstep(.25, 2.2, footprint));
    slope += vec2(surf_pattern - shoal.g, deep.b - shoal.b) * .095 *
        (1.0 - smoothstep(.5, 2.8, footprint));
    if (dereth_rain > .01) {
        slope += (vec2(water_noise(p * 2.8 - TIME * 4.1), water_noise(p * 3.8 + TIME * 3.1)) - .5)
            * dereth_rain * .12 * (1.0 - smoothstep(.12, .92, footprint));
    }
    float near_surface = 1.0 - smoothstep(20.0, 118.0, distance_to_eye);
    slope *= mix(0.075, 1.0, near_surface);
    slope += regatta_wave_slope(p) * (1.0 - smoothstep(40.0, 148.0, distance_to_eye));
    slope += fluid_gradient;
    vec3 normal = normalize(vec3(-slope.x, 1.0, -slope.y));
    vec3 reflected_ray = reflect(-eye, normal);
    // Average the Fresnel term over unresolved slopes. Point-sampling nearly grazing
    // waves repeatedly switches between full reflection and transmission at a distance.
    float view_cosine = clamp(mix(eye.y, dot(eye, normal), near_surface * .77), 0.0, 1.0);
    float fresnel = .024 + .86 * pow(1.0 - view_cosine, 5.0);

    // The DAT ocean floor is flat. Estimate depth from the real coastline so
    // the shallow texture and underwater sand give way to the open sea.
    float depth = .16 + 10.8 * pow(shore / 96.0, 1.54);
    float wet_daylight = clamp(dot(dereth_horizon, vec3(.29, .58, .13)), .17, 1.0);
    float day = max(sun_energy, .25) * mix(.60, 1.0, wet_daylight);
    vec3 water_tint = mix(vec3(.014, .203, .191), vec3(.009, .072, .119), smoothstep(5.0, 86.0, shore));
    vec3 native_blue = mix(shoal, deep, smoothstep(11.0, 83.0, shore));
    vec3 transmission = exp(-vec3(.74, .36, .21) * depth / max(view_cosine, .47));
    vec3 sand = texture(native_sand, p * .64 + slope * .30).rgb;
    float caustic = pow(.5 + .5 * sin(dot(p, vec2(3.38, 2.39)) + TIME * 2.4 + shoal.g * 8.0) *
        sin(dot(p, vec2(-2.61, 3.16)) - TIME * 1.99), 9.0);
    vec3 seabed = (vec3(.12, .077, .035) + sand * vec3(.56, .51, .36))
        * day * (.78 + caustic * .52 * (1.0 - smoothstep(.45, 1.8, footprint)));
    float surface_grain = .88 + .14 * surf_pattern + .16 * shoal.b;
    vec3 body = (water_tint * (.73 + native_blue * .77) * surface_grain +
        vec3(.004, .018, .027) * (.5 + .5 * dot(normal, dereth_light))) * day;
    vec3 transmitted = mix(body, seabed, transmission);

    vec3 horizon = dereth_horizon;
    vec3 zenith = dereth_zenith;
    if (!OUTPUT_IS_SRGB) {
        horizon = pow(horizon, vec3(2.2));
        zenith = pow(zenith, vec3(2.2));
    }
    vec3 reflected = island_sky(reflected_ray, horizon, zenith, TIME);
    if (reflection_active && distance(CAMERA_POSITION_WORLD, reflection_eye) < .35) {
        vec3 delta = mat3(VIEW_MATRIX) * (normal - vec3(0.0, 1.0, 0.0));
        vec2 mirror_uv = vec2(SCREEN_UV.x, 1.0 - SCREEN_UV.y) + delta.xy * vec2(.046, -.055);
        vec2 image_edge = min(mirror_uv, 1.0 - mirror_uv);
        float available = smoothstep(.0, .063, min(image_edge.x, image_edge.y))
            * (1.0 - smoothstep(255.0, 760.0, distance_to_eye));
        vec2 stagger = vec2(.0042, -.0064) * (1.0 + footprint * .30);
        vec3 mirror = texture(island_reflection, clamp(mirror_uv, vec2(.002), vec2(.998))).rgb * .50;
        mirror += texture(island_reflection, clamp(mirror_uv + stagger, vec2(.002), vec2(.998))).rgb * .25;
        mirror += texture(island_reflection, clamp(mirror_uv - stagger, vec2(.002), vec2(.998))).rgb * .25;
        reflected = mix(reflected, mirror * vec3(.61, .74, .79), available * .86);
    }
    float facing_sun = max(0.0, dot(reflected_ray, normalize(dereth_light)));
    float polished = pow(facing_sun, mix(310.0, 73.0, smoothstep(.35, 2.0, footprint)));
    float glint = (polished * 1.42 + pow(facing_sun, 48.0) * .085)
        * sun_energy * (1.0 - dereth_rain * .72);

    // Advancing lines of wash use the native running-water and shallow-sea
    // detail to break apart before they reach the dry shoreline.
    float wash = .5 + .5 * cos(shore * .76 + wave_lift * 2.9 + TIME * 2.07 +
        sin(dot(p, vec2(.149, -.083))) * .69);
    float broken = smoothstep(.14, .45, surf_pattern + shoal.g * .12);
    float foam = smoothstep(.75, .98, wash) * broken *
        (1.0 - smoothstep(2.0, 12.8, shore));
    foam += smoothstep(.32, .62, surf_pattern) * .21 * (1.0 - smoothstep(.5, 1.8, shore));
    foam += smoothstep(.36, .62, wave_lift) * broken * .23;
    foam += smoothstep(.055, .22, fluid_lift) * smoothstep(.035, .18, length(fluid_gradient)) * broken * .36;
    vec3 surface = mix(transmitted, reflected, fresnel * .75);
    vec3 foam_color = mix(vec3(.38, .56, .55), vec3(.74, .82, .79), surf_pattern) * day;
    ALBEDO = mix(surface + sunlight * glint, foam_color, clamp(foam * .71, 0.0, .95));
}
