shader_type sky;

uniform vec3 zenith : source_color = vec3(.22, .48, .72);
uniform vec3 horizon : source_color = vec3(.78, .85, .89);
uniform vec3 sun_color : source_color = vec3(1.0, .92, .78);
uniform vec3 cloud_color : source_color = vec3(.94, .95, .97);
uniform vec3 sun_direction = vec3(.5, .72, .44);
uniform float sunlight = .82;
uniform float cloud_threshold = .53;
uniform float night = 0.0;
uniform vec2 wind = vec2(.006, .002);
uniform sampler2D cloud_noise : repeat_enable, filter_linear_mipmap;
uniform sampler2D moon_one : source_color, repeat_disable, filter_linear;
uniform sampler2D moon_two : source_color, repeat_disable, filter_linear;
uniform vec3 moon_one_direction = vec3(.2,.4,-.89);
uniform vec3 moon_two_direction = vec3(-.3,.2,-.93);
uniform bool has_moon_one = false;
uniform bool has_moon_two = false;

float sky_hash(vec2 p) {
    vec3 q = fract(vec3(p.xyx) * .1031);
    q += dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}

vec3 star_layer(vec3 direction) {
    vec2 sphere = vec2(atan(direction.z, direction.x) / TAU + .5, asin(clamp(direction.y, -1.0, 1.0)) / PI + .5);
    vec2 uv = sphere * vec2(600.0, 300.0);
    vec2 tile = floor(uv);
    float seed = sky_hash(tile);
    vec2 origin = vec2(sky_hash(tile + vec2(4,17)), sky_hash(tile + vec2(13,8))) * .68 + .16;
    float band = 1.0 - smoothstep(.03, .26, abs(dot(direction, normalize(vec3(.40, .55, .72)))));
    float point = (1.0 - smoothstep(.02, mix(.060, .145, seed), length(fract(uv)-origin)))
        * smoothstep(.87 - band * .04, .99, seed);
    vec3 starlight = mix(vec3(.52, .69, 1.0), vec3(1.0, .87, .66), sky_hash(tile + vec2(25,2)));
    float dust = textureLod(cloud_noise, sphere * vec2(4.0, 2.0), 1.0).r;
    return starlight * point * 1.05 + vec3(.097, .109, .16) * band * dust;
}

vec3 moon_disk(vec3 air, vec3 ray, vec3 center, float radius, sampler2D image, bool native, vec3 tint, float amount) {
    if (dot(ray, center) < 1.0 - radius * radius * 1.5) return air;
    vec3 right = normalize(cross(center, vec3(0.0, 1.0, 0.0)));
    vec3 up = cross(right, center);
    vec2 disk = vec2(dot(ray, right), -dot(ray, up)) / radius;
    float edge = 1.0 - smoothstep(.965, 1.01, length(disk));
    if (edge <= 0.0) return air;
    vec4 texel = native ? textureLod(image, disk * .49 + .5, 0.0) : vec4(tint, 1.0);
    vec3 body = texel.rgb * .94 + tint * .10;
    return mix(air, body, amount * edge * texel.a);
}

float shell_distance(float height, float elevation) {
    const float radius = 52.0;
    float lift = height * (2.0 * radius + height);
    float radial = radius * elevation;
    return lift / (sqrt(radial * radial + lift) + radial);
}

float cloud_mass(vec2 position, float height, float mip) {
    // Interpolate between offset slices of the noise so the cloud has depth,
    // while keeping the density field small enough for the Compatibility renderer.
    float slice = height * 1.8;
    vec2 offset = floor(slice) * vec2(.319, .437);
    vec2 p = position + offset;
    float body = mix(textureLod(cloud_noise, p, mip).r,
        textureLod(cloud_noise, p + vec2(.319, .437), mip).r, smoothstep(0.0, 1.0, fract(slice)));
    float regions = textureLod(cloud_noise, position * .43 + vec2(.27, .82), mip).r;
    float height_profile = smoothstep(0.0, .21, height) * (1.0 - smoothstep(.56, 1.0, height));
    float shape = mix(body, regions, .48);
    float threshold = cloud_threshold * .66 + .173 + (1.0 - height_profile) * .095;
    if (shape < threshold - .072) return 0.0;
    float detail = textureLod(cloud_noise, position * 2.36 + vec2(.83 + height * .10, .24 - height * .06), mip).r;
    shape += (detail - .5) * .080;
    return smoothstep(threshold - .032, threshold + .100, shape) * height_profile;
}

vec3 cloud_volume(vec3 ray, vec3 air, float toward_sun) {
    const float base_height = .26;
    const float cloud_depth = .30;
    const int steps = 13;
    float start = shell_distance(base_height, ray.y);
    float end = shell_distance(base_height + cloud_depth, ray.y);
    float step_size = (end - start) / float(steps);
    vec2 flow = TIME * wind * .42 + vec2(.42, .37);
    // Keep the broad cloud shapes anchored to sky directions. Only the shorter
    // offset travels through their depth, so grazing rays don't sweep a noisy tile.
    vec2 column = ray.xz * (.52 / (ray.y + .51)) + flow;
    vec2 parallax = ray.xz * min(.13, (end - start) * .073);
    vec2 light_step = sun_direction.xz * .072;
    float light_height = max(.10, sun_direction.y * .36);
    float beer = step_size * 16.0;
    float mip = mix(2.1, .7, smoothstep(.1, .53, ray.y));
    float forward_scatter = pow(toward_sun, 6.0);
    float overcast = 1.0 - smoothstep(.30, .53, cloud_threshold);
    vec3 shadow_tint = mix(cloud_color * vec3(.66, .75, .89), horizon * .58, .38) * (1.0 - overcast * .18);
    vec3 sunlit = cloud_color * .98 + sun_color * (.11 + .13 * forward_scatter) * sunlight;
    vec3 total = vec3(0.0);
    float through = 1.0;
    for (int i = 0; i < steps; ++i) {
        float height = (float(i) + .5) / float(steps);
        vec2 position = column + parallax * (height - .5);
        float mass = cloud_mass(position, height, mip);
        if (mass > .004) {
            float occlusion = cloud_mass(position + light_step, min(1.0, height + light_height), mip + .35);
            float light = clamp(.20 + .52 * exp(-occlusion * 2.4) + .23 * height - overcast * .09, 0.0, 1.0);
            float rim = smoothstep(.08, .40, mass - occlusion) * .17;
            vec3 lit = mix(shadow_tint, sunlit, light + rim * (1.0 - light));
            lit += sun_color * rim * forward_scatter * sunlight * .16;
            lit = mix(lit, cloud_color * (.30 + light * .20), night);
            float opacity = 1.0 - exp(-mass * beer);
            total += lit * (through * opacity);
            through *= 1.0 - opacity;
            if (through < .025) break;
        }
    }
    float distance_fade = smoothstep(.065, .27, ray.y);
    return mix(air, total + air * through, distance_fade);
}

void sky() {
    vec3 direction = normalize(EYEDIR);
    float altitude = max(0.0, direction.y);
    float sky_curve = 1.0 - exp(-altitude * 2.35);
    vec3 air = mix(horizon, zenith, sky_curve);
    air = mix(air, horizon * .73, smoothstep(0.0, .36, -direction.y));

    float toward_sun = max(0.0, dot(direction, sun_direction));
    float glow = pow(toward_sun, 12.0) * .22 + pow(toward_sun, 38.0) * .20;
    float disk = smoothstep(.99965, .99988, toward_sun) * 1.2 * (1.0-night);
    air += sun_color * (glow + disk) * sunlight;
    if (!AT_CUBEMAP_PASS && altitude > .025) {
        if (night > .0) air += star_layer(direction) * night * night * smoothstep(.03, .32, altitude);
        float moonlight = mix(.29, .98, night);
        air = moon_disk(air, direction, moon_one_direction, .039, moon_one, has_moon_one, vec3(.77, .85, .94), moonlight);
        air = moon_disk(air, direction, moon_two_direction, .025, moon_two, has_moon_two, vec3(.94, .59, .44), moonlight);
        if (altitude > .065) air = cloud_volume(direction, air, toward_sun);
    }
    COLOR = air;
}
