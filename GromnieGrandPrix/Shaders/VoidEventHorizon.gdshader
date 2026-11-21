shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, fog_disabled;

uniform float surge : hint_range(0.0, 1.0) = .4;
uniform float binding_strength : hint_range(0.0, 1.0) = .0;

void vertex() {
    // Keep the optical projection readable from every side of the circuit.
    mat4 facing = mat4(
        INV_VIEW_MATRIX[0] * length(MODEL_MATRIX[0].xyz),
        INV_VIEW_MATRIX[1] * length(MODEL_MATRIX[1].xyz),
        INV_VIEW_MATRIX[2] * length(MODEL_MATRIX[2].xyz),
        MODEL_MATRIX[3]);
    MODELVIEW_MATRIX = VIEW_MATRIX * facing;
}

float soft_line(float across, float width) {
    float t = across / width;
    return exp(-t * t);
}

void fragment() {
    vec2 p = (UV - .5) * 2.0 * (1.0 + binding_strength * .62);
    p.y = -p.y;
    p = mat2(vec2(.981, .194), vec2(-.194, .981)) * p;
    float radius = length(p);
    float orbit = atan(p.y, p.x);
    float live = .93 + surge * .12;
    float dark = 1.0 - smoothstep(.235, .248, radius);
    float photon_ring = soft_line(radius - .252, .0075);
    float halo = soft_line(radius - .27, .075) * (1.0 - dark);

    vec2 disk_position = vec2(p.x, (p.y + .032) * 4.0);
    float disk_radius = length(disk_position);
    float disk_angle = atan(disk_position.y, disk_position.x);
    float shear = 56.0 * disk_radius - disk_angle * 3.0 + TIME * 1.8;
    float filaments = .34 + .34 * (sin(shear + .58 * sin(disk_angle * 9.0 - TIME * .8)) + 1.0) * .5
        + .32 * pow((sin(shear * .71 + disk_angle * 4.0) + 1.0) * .5, 3.0);
    float disk = smoothstep(.210, .29, disk_radius) * (1.0 - smoothstep(.34, .87, disk_radius));
    disk *= filaments * (p.y < -.018 ? 1.0 : 1.0 - dark) * live;
    float hot_band = soft_line(disk_radius - .302, .048) * (p.y < -.018 ? 1.0 : 1.0 - dark) * filaments;

    // The upper and lower images curl around the shadow like lensed disk light.
    float stretch = clamp(p.x / .43, -.999, .999);
    float curve = sqrt(max(0.0, 1.0 - stretch * stretch));
    float arc_mask = 1.0 - smoothstep(.28, .44, abs(p.x));
    float arc_flow = .70 + .30 * sin(orbit * 17.0 + TIME * 2.3);
    float arc = soft_line(p.y - (.08 + .228 * curve), .016) * arc_mask * arc_flow;
    arc += soft_line(p.y + (.10 + .205 * curve), .024) * arc_mask * .43;
    arc *= (1.0 - dark) * live;

    float hot_side = smoothstep(-.16, .48, -p.x);
    vec3 violet = vec3(.43, .22, .93);
    vec3 amber = vec3(1.0, .55, .26);
    vec3 white_hot = vec3(1.0, .92, .80);
    vec3 color = vec3(.002, .001, .007);
    color += violet * (halo * .59 + disk * .80 + arc * .76);
    color += mix(amber, vec3(.34, .62, 1.0), binding_strength)
        * (disk * hot_side * .62 + arc * hot_side * .69 + hot_band * .71);
    color += white_hot * (photon_ring * (.46 + hot_side * .54) + hot_band * hot_side * .67);
    ALBEDO = color;
    ALPHA = max(dark, clamp(photon_ring + disk * .82 + arc * .82 + halo * .36, 0.0, 1.0));
}
