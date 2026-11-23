global uniform float dereth_wetness;
global uniform float dereth_rain;
global uniform vec3 dereth_horizon;
global uniform vec3 dereth_zenith;
global uniform vec3 dereth_light;

float water_hash(vec2 p) {
    vec3 q = fract(vec3(p.xyx) * .1031);
    q += dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}

float water_noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f*f*(3.0-2.0*f);
    return mix(mix(water_hash(cell), water_hash(cell + vec2(1,0)), f.x),
        mix(water_hash(cell + vec2(0,1)), water_hash(cell + vec2(1,1)), f.x), f.y);
}

vec3 water_sky(vec3 ray, float time) {
    vec3 sky = mix(dereth_horizon, dereth_zenith, smoothstep(0.0, .83, max(0.0, ray.y)));
    float cloud = water_noise(ray.xz * 9.0 / max(ray.y + .18, .22) + vec2(.11, -.06) * time);
    return mix(sky, dereth_horizon * 1.17, smoothstep(.42, .79, cloud) * .57);
}

vec3 river_normal(vec2 p, float time) {
    float x = sin(dot(p, vec2(.64, .24)) - time * .83) * .021
        + sin(dot(p, vec2(1.41, -.78)) - time * 1.32) * .010;
    float z = sin(dot(p, vec2(-.29, .71)) - time * 1.09) * .018
        + cos(dot(p, vec2(.93, .58)) - time * 1.85) * .011;
    vec2 drops = vec2(water_noise(p * 2.6 + time), water_noise(p * 2.1 - time)) - .5;
    return normalize(vec3(x + drops.x * dereth_rain * .037, 1.0, z + drops.y * dereth_rain * .037));
}

// Accumulation rises from scattered shallow depressions and drains off steep banks.
void track_water_surface(vec2 point, float road, float catchment, vec3 ground_up, vec3 eye, float time,
        inout vec3 albedo, out vec3 reflected, out float film, out float pool, out vec3 normal) {
    reflected = vec3(0.0);
    film = 0.0;
    pool = 0.0;
    normal = ground_up;
    if (dereth_wetness <= .005 || road <= .01) return;
    float wet = dereth_wetness * road;
    film = wet;
    albedo *= 1.0 - wet * .23;
    float basin = water_noise(point * vec2(.49, .36)) * .72 + water_noise(point * .96 + vec2(17,-8)) * .28;
    float level_ground = smoothstep(.972, .997, ground_up.y);
    pool = smoothstep(.475, .605, basin + wet * .235 + (catchment - .5) * .55 - .14)
        * level_ground * smoothstep(.19, .54, wet);
    if (pool <= .005) return;

    // The smooth water surface picks up the sky and the brightest part of the cloud deck.
    // Short travelling ripples break the reflection as rain falls on the pool.
    vec2 cell = floor(point * 1.85);
    float seed = water_hash(cell);
    vec2 drop = fract(point * 1.85) - (.26 + vec2(seed, water_hash(cell + vec2(63.0, 15.0))) * .48);
    float age = fract(time * 1.72 + seed);
    float drop_distance = length(drop);
    float wave_distance = drop_distance - age * .26;
    float ring = sin(wave_distance * 53.0) * (1.0 - smoothstep(.027, .13, abs(wave_distance)));
    float ripple = ring * sin(age * PI) * (1.0 - smoothstep(.18, .43, drop_distance)) * (.009 + dereth_rain * .030);
    normal = normalize(vec3(sin(point.x * 5.3 + time * 2.2) * .012 + ripple, 1.0,
        sin(point.y * 6.4 - time * 1.8) * .013 - ripple));
    vec3 ray = reflect(-eye, normal);
    vec3 sky = water_sky(ray, time);
    float glare = pow(max(0.0, dot(ray, dereth_light)), 48.0);
    float fresnel = pow(1.0 - clamp(dot(eye, normal), 0.0, 1.0), 3.1);
    albedo = mix(albedo, albedo * .28, pool);
    reflected = (sky * mix(.14, .50, fresnel) + glare * dereth_horizon * .40) * pool;
}

void track_water(vec2 point, float road, float catchment, vec3 ground_up, vec3 eye, float time,
        inout vec3 albedo, out vec3 reflected) {
    float film;
    float pool;
    vec3 normal;
    track_water_surface(point, road, catchment, ground_up, eye, time, albedo, reflected, film, pool, normal);
}
