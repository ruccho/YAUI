#ifndef YAUI_INCLUDED
#define YAUI_INCLUDED

// The shared code of YAUI shaders (planning/design-core.md). Uber.shader uses it as is; custom shaders for
// YauiElement.Material include it, write their own fragment function on top of Varyings (or call FragImpl), and
// declare two passes like Uber.shader: "Overlay" (LightMode YauiOverlay, vertex VertOverlay) and "World" (no
// LightMode, vertex VertWorld). The data is bound as global shader properties prefixed with _Yaui.
//
// Each primitive is a quad of 4 vertices, indexed 6 times (quad = vertex / 4, corner = vertex % 4):
//   draw order (_YauiOrder) -> primitive (_YauiPrimitives, + _YauiExts) -> node (_YauiNodes) -> clip (_YauiClips)
// Colors are premultiplied in the fragment shader (Blend One OneMinusSrcAlpha).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Must match Yaui.Rendering.PrimitiveFlags.
#define YAUI_FLAG_TEXT 1u
#define YAUI_FLAG_BORDER 2u
#define YAUI_FLAG_SHADOW 4u
#define YAUI_FLAG_TEXT_BOLD 8u
#define YAUI_FLAG_IMAGE 32u
#define YAUI_FLAG_RADIAL_FILL 64u
// In primitives, the upper 16 bits of the flags are a texture id; the vertex shader replaces them with the slot of
// the texture in the draw (0..7) at bit 8 of the flags it passes on.
#define YAUI_TEXTURE_SLOT_SHIFT 8u
#define YAUI_TEXTURE_ID_SHIFT 16u

// The features compiled into the shader (the flags above, without the u suffix for the preprocessor). A shader that
// defines YAUI_FEATURES before the include leaves out the others, with their varyings; primitives that use them are
// drawn without them. A shader that defines YAUI_FEATURE_KEYWORDS instead takes them from the keywords YAUI_TEXT,
// YAUI_IMAGE, YAUI_BORDER (with radial fills) and YAUI_SHADOW, like Uber.shader, whose variants the renderer picks
// per draw from the flags of its primitives (Yaui.Rendering.ShaderFeatures). Otherwise every feature is compiled.
#define YAUI_FEATURE_TEXT 1
#define YAUI_FEATURE_BORDER 2
#define YAUI_FEATURE_SHADOW 4
#define YAUI_FEATURE_IMAGE 32
#define YAUI_FEATURE_RADIAL_FILL 64
#if !defined(YAUI_FEATURES) && defined(YAUI_FEATURE_KEYWORDS)
    #if defined(YAUI_TEXT)
        #define YAUI_KEYWORD_TEXT 1
    #else
        #define YAUI_KEYWORD_TEXT 0
    #endif
    #if defined(YAUI_IMAGE)
        #define YAUI_KEYWORD_IMAGE 1
    #else
        #define YAUI_KEYWORD_IMAGE 0
    #endif
    #if defined(YAUI_BORDER)
        #define YAUI_KEYWORD_BORDER 1
    #else
        #define YAUI_KEYWORD_BORDER 0
    #endif
    #if defined(YAUI_SHADOW)
        #define YAUI_KEYWORD_SHADOW 1
    #else
        #define YAUI_KEYWORD_SHADOW 0
    #endif
    #define YAUI_FEATURES (YAUI_KEYWORD_TEXT * YAUI_FEATURE_TEXT + YAUI_KEYWORD_IMAGE * YAUI_FEATURE_IMAGE + \
        YAUI_KEYWORD_BORDER * (YAUI_FEATURE_BORDER + YAUI_FEATURE_RADIAL_FILL) + \
        YAUI_KEYWORD_SHADOW * YAUI_FEATURE_SHADOW)
#endif
#ifndef YAUI_FEATURES
#define YAUI_FEATURES 127
#endif
#define YAUI_HAS_UV (YAUI_FEATURES & (YAUI_FEATURE_TEXT | YAUI_FEATURE_IMAGE))
#define YAUI_HAS_BORDER_COLOR (YAUI_FEATURES & (YAUI_FEATURE_BORDER | YAUI_FEATURE_RADIAL_FILL))
#define YAUI_HAS_SHADOW (YAUI_FEATURES & YAUI_FEATURE_SHADOW)
// Flags with the features left out cleared, so that the compiler folds their branches.
#define YAUI_FLAG_MASK ((uint)YAUI_FEATURES | YAUI_FLAG_TEXT_BOLD | 0xffffff00u)

TEXTURE2D(_YauiTex0);
TEXTURE2D(_YauiTex1);
TEXTURE2D(_YauiTex2);
TEXTURE2D(_YauiTex3);
TEXTURE2D(_YauiTex4);
TEXTURE2D(_YauiTex5);
TEXTURE2D(_YauiTex6);
TEXTURE2D(_YauiTex7);
SAMPLER(sampler_YauiTex0);

// Per texture slot. x: width in texels, y: distance field spread in texels, z: height in texels.
float4 _YauiAtlasParams[8];

// Texture ids bound to slots 0..3 and 4..7 of the draw (-1: none).
float4 _YauiTexIds0;
float4 _YauiTexIds1;

// Canvas units per screen pixel (overlay).
float _YauiPixelSize;

// World space panels: canvas units to world.
float4x4 _YauiPanelMatrix;

// First draw position of the draw call (draws are split where the material changes).
uint _YauiOrderOffset;

// Must match Yaui.Rendering.NodeGpuData.
struct NodeData
{
    float4 m;
    float2 translation;
    // Low 16 bits: opacity (half), high 16 bits: clip record.
    uint opacityClip;
    // RGBA8: multiplies the colors of the node's own primitives.
    uint tint;
};

// Must match Yaui.Rendering.ClipGpuData.
struct ClipData
{
    float4 rect;
    // xy: center, zw: half size, in the local space of the rounded clip (identity transform only).
    float4 rounded;
    float4 roundedRadii;
    float4 reserved;
};

// Must match Yaui.Rendering.PrimitiveData.
struct PrimitiveData
{
    float4 rect;
    uint2 uvRect;
    uint2 color;
    uint2 borderColor;
    uint2 radii;
    uint node;
    uint flags;
    uint borderWidthAndSkew;
    uint ext;
};

// Must match Yaui.Rendering.PrimitiveExt.
struct PrimitiveExt
{
    uint2 shadowColor;
    uint2 shadow;
    uint2 reserved0;
    uint2 reserved1;
};

StructuredBuffer<uint> _YauiOrder;
StructuredBuffer<PrimitiveData> _YauiPrimitives;
StructuredBuffer<PrimitiveExt> _YauiExts;
StructuredBuffer<NodeData> _YauiNodes;
StructuredBuffer<ClipData> _YauiClips;

struct Varyings
{
    float4 positionCS : SV_POSITION;
    #if YAUI_HAS_UV
    float2 uv : TEXCOORD0;
    #endif
    // xy: position relative to the shape center (local space), zw: half size of the shape.
    float4 local : TEXCOORD1;
    nointerpolation float4 color : COLOR;
    // Corner radii: top-left, top-right, bottom-right, bottom-left.
    nointerpolation float4 radii : TEXCOORD2;
    // x: largest radius, y: flags, z: border width, w: texels per local unit (text).
    nointerpolation float4 params : TEXCOORD3;
    #if YAUI_HAS_BORDER_COLOR
    nointerpolation float4 borderColor : TEXCOORD4;
    #endif
    #if YAUI_HAS_SHADOW
    nointerpolation float4 shadowColor : TEXCOORD5;
    // xy: offset, z: blur sigma, w: spread.
    nointerpolation float4 shadow : TEXCOORD6;
    #endif
    // xy: canvas position (for per-pixel clips), z: local units per screen pixel (overlay), w: per-pixel clip.
    float4 canvasMisc : TEXCOORD7;
    #if defined(YAUI_PIXEL_CLIP)
    // Per-pixel clip (rotated quads, rounded clips): rect (min, max), rounded (center, half size), radii.
    // Only with YAUI_PIXEL_CLIP (a keyword of Uber.shader, enabled for panels that need it): the varyings cost
    // about a quarter of the GPU time of a typical screen on mobile.
    nointerpolation float4 clipRect : TEXCOORD8;
    nointerpolation float4 clipRounded : TEXCOORD9;
    nointerpolation float4 clipRadii : TEXCOORD10;
    #endif
};

float2 UnpackHalf2(uint v)
{
    return float2(f16tof32(v & 0xffffu), f16tof32(v >> 16));
}

float4 UnpackHalf4(uint2 v)
{
    return float4(UnpackHalf2(v.x), UnpackHalf2(v.y));
}

float4 UnpackUnorm8x4(uint v)
{
    return float4(v & 0xffu, (v >> 8) & 0xffu, (v >> 16) & 0xffu, v >> 24) * (1.0 / 255.0);
}

float4 UnpackUnorm16x4(uint2 v)
{
    return float4(v.x & 0xffffu, v.x >> 16, v.y & 0xffffu, v.y >> 16) * (1.0 / 65535.0);
}

uint TextureSlot(uint flags)
{
    return (flags >> YAUI_TEXTURE_SLOT_SHIFT) & 7u;
}

// world: world space panel (a compile time constant from the pass wrappers below).
Varyings VertImpl(uint vertexId, bool world)
{
    uint corner = vertexId & 3u;
    float2 t = float2(corner & 1u, corner >> 1);

    PrimitiveData p = _YauiPrimitives[_YauiOrder[(vertexId >> 2) + _YauiOrderOffset]];
    NodeData n = _YauiNodes[p.node];
    ClipData clip = _YauiClips[n.opacityClip >> 16];
    float opacity = f16tof32(n.opacityClip & 0xffffu);
    float4 tint = UnpackUnorm8x4(n.tint);
    float4 clipRect = clip.rect;

    float4 rect = p.rect;
    float4 uvRect = UnpackUnorm16x4(p.uvRect);
    float4 color = UnpackHalf4(p.color);
    float4 borderColor = UnpackHalf4(p.borderColor);
    float4 radii = UnpackHalf4(p.radii);
    float2 widthAndSkew = UnpackHalf2(p.borderWidthAndSkew);
    uint featureFlags = p.flags & YAUI_FLAG_MASK;
    float4 shadowColor = 0.0;
    float4 shadow = 0.0;
    #if YAUI_HAS_SHADOW
    [branch] if (p.ext != 0u)
    {
        PrimitiveExt x = _YauiExts[p.ext];
        shadowColor = UnpackHalf4(x.shadowColor);
        shadow = UnpackHalf4(x.shadow);
    }
    #endif

    tint.a *= opacity;
    color *= tint;
    // A radial fill keeps its parameters in the border color.
    borderColor *= (featureFlags & YAUI_FLAG_RADIAL_FILL) ? 1.0 : tint;
    shadowColor *= tint;

    // Expand the quad to cover the drop shadow.
    float2 extent = (featureFlags & YAUI_FLAG_SHADOW) ? abs(shadow.xy) + shadow.z * 3.0 + shadow.w : 0.0;
    float2 halfSize = rect.zw * 0.5;
    float2 center = rect.xy + halfSize;
    float2 localMin = center - halfSize - extent;
    float2 localMax = center + halfSize + extent;

    float2x2 m = float2x2(n.m.xy, n.m.zw);
    float2 localPosition;
    float2 canvasPosition;
    bool axisAligned = n.m.y == 0.0 && n.m.z == 0.0;
    [branch] if (axisAligned)
    {
        // Axis-aligned: shrink the quad to the clip rect. A fully clipped quad collapses.
        float2 scale = float2(n.m.x, n.m.w);
        scale = (abs(scale) < 1e-6) ? 1e-6 : scale;
        float2 c0 = localMin * scale + n.translation;
        float2 c1 = localMax * scale + n.translation;
        float2 cMin = max(min(c0, c1), clipRect.xy);
        float2 cMax = max(min(max(c0, c1), clipRect.zw), cMin);
        canvasPosition = lerp(cMin, cMax, t);
        localPosition = (canvasPosition - n.translation) / scale;
    }
    else
    {
        // Rotated: the clip rect is tested per pixel.
        localPosition = lerp(localMin, localMax, t);
        canvasPosition = mul(m, localPosition) + n.translation;
    }

    // Horizontal skew (italic glyphs) around the bottom of the rect.
    canvasPosition += mul(m, float2(widthAndSkew.y * (rect.y + rect.w - localPosition.y), 0.0));

    // The slot of the primitive's texture in this draw.
    uint slot = 0u;
    #if YAUI_HAS_UV
    float textureId = (float)(p.flags >> YAUI_TEXTURE_ID_SHIFT);
    slot = (uint)(dot(float4(_YauiTexIds0 == textureId), float4(0.0, 1.0, 2.0, 3.0)) +
        dot(float4(_YauiTexIds1 == textureId), float4(4.0, 5.0, 6.0, 7.0)));
    #endif
    uint flags = (featureFlags & 0xffu) | (slot << YAUI_TEXTURE_SLOT_SHIFT);
    Varyings o;
    float localPixelSize = 0.0;
    if (world)
    {
        o.positionCS = mul(UNITY_MATRIX_VP, mul(_YauiPanelMatrix, float4(canvasPosition, 0.0, 1.0)));
    }
    else
    {
        o.positionCS = mul(UNITY_MATRIX_VP, float4(canvasPosition, 0.0, 1.0));
        localPixelSize = _YauiPixelSize / sqrt(max(abs(determinant(m)), 1e-12));
    }
    #if YAUI_HAS_UV
    o.uv = lerp(uvRect.xy, uvRect.zw, (localPosition - rect.xy) / max(rect.zw, 1e-4));
    #endif
    o.local = float4(localPosition - center, halfSize);
    o.color = color;
    o.radii = radii;
    // uvRect: UV at the min corner (xy) and at the max corner (zw) of the rect.
    o.params = float4(max(max(radii.x, radii.y), max(radii.z, radii.w)), flags, widthAndSkew.x,
                      (YAUI_FEATURES & YAUI_FEATURE_TEXT)
                          ? abs(uvRect.z - uvRect.x) * _YauiAtlasParams[slot].x / max(rect.z, 1e-4)
                          : 0.0);
    #if YAUI_HAS_BORDER_COLOR
    o.borderColor = borderColor;
    #endif
    #if YAUI_HAS_SHADOW
    o.shadowColor = shadowColor;
    o.shadow = shadow;
    #endif
    o.canvasMisc = float4(canvasPosition, localPixelSize, (!axisAligned || clip.rounded.z > 0.0) ? 1.0 : 0.0);
    #if defined(YAUI_PIXEL_CLIP)
    o.clipRect = clipRect;
    o.clipRounded = clip.rounded;
    o.clipRadii = clip.roundedRadii;
    #endif
    return o;
}

float4 SampleTexture(uint slot, float2 uv)
{
    // Separate textures cannot be indexed dynamically.
    float4 c;
    [branch] if (slot == 0u)
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex0, sampler_YauiTex0, uv, 0);
    else if (slot == 1u)
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex1, sampler_YauiTex0, uv, 0);
    else if (slot == 2u)
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex2, sampler_YauiTex0, uv, 0);
    else if (slot == 3u)
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex3, sampler_YauiTex0, uv, 0);
    else if (slot == 4u)
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex4, sampler_YauiTex0, uv, 0);
    else if (slot == 5u)
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex5, sampler_YauiTex0, uv, 0);
    else if (slot == 6u)
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex6, sampler_YauiTex0, uv, 0);
    else
        c = SAMPLE_TEXTURE2D_LOD(_YauiTex7, sampler_YauiTex0, uv, 0);
    return c;
}

// Radius of the corner in the quadrant of p (Y down).
float CornerRadius(float2 p, float4 radii)
{
    float2 r = p.x < 0.0 ? radii.xw : radii.yz;
    return p.y < 0.0 ? r.x : r.y;
}

float RoundedBoxDistance(float2 p, float2 halfSize, float radius)
{
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

float Coverage(float distance, float pixelSize)
{
    return saturate(0.5 - distance / pixelSize);
}

// The coverage of a clip at a canvas position: the rect (min, max), and the rounded clip (center, half size; none if
// zero) with its radii.
float YauiClipCoverage(float2 canvas, float4 clipRect, float4 clipRounded, float4 clipRadii)
{
    float coverage = all(canvas >= clipRect.xy) && all(canvas < clipRect.zw) ? 1.0 : 0.0;
    if (clipRounded.z > 0.0)
    {
        float2 cp = canvas - clipRounded.xy;
        float r = min(CornerRadius(cp, clipRadii), min(clipRounded.z, clipRounded.w));
        float canvasPixel = length(fwidth(canvas)) * 0.70710678;
        coverage *= Coverage(RoundedBoxDistance(cp, clipRounded.zw, r), max(canvasPixel, 1e-4));
    }

    return coverage;
}

// Abramowitz and Stegun 7.1.27.
float Erf(float x)
{
    float s = sign(x);
    float a = abs(x);
    float t = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
    t *= t;
    return s - s / (t * t);
}

// The coverage of the sector of a radial fill. fill: center (0..1 of the shape, Y down), start angle, signed sweep.
float RadialFillCoverage(float2 local, float2 halfSize, float4 fill, float pixelSize)
{
    float sweep = abs(fill.w);
    if (sweep >= 6.2831)
    {
        return 1.0;
    }

    if (sweep <= 0.0)
    {
        return 0.0;
    }

    // Angles grow clockwise on screen (Y down).
    float2 q = local - (fill.xy * 2.0 - 1.0) * halfSize;
    float angle = (atan2(q.y, q.x) - fill.z) * (fill.w < 0.0 ? -1.0 : 1.0);
    angle -= 6.2831853 * floor(angle / 6.2831853);
    // Signed angle to the nearest edge of the sector (positive inside), as a distance at this radius.
    float d = angle <= sweep ? min(angle, sweep - angle) : -min(angle - sweep, 6.2831853 - angle);
    float distance = length(q) * sin(clamp(d, -1.5707963, 1.5707963));
    return saturate(distance / pixelSize + 0.5);
}

float4 Premultiply(float4 c)
{
    return float4(c.rgb * c.a, c.a);
}

float4 ShadeShape(float2 local, float2 halfSize, float4 color, float radius, uint flags,
                  float borderWidth, float4 borderColor, float4 shadowColor, float4 shadow, float pixelSize)
{
    float distance = RoundedBoxDistance(local, halfSize, radius);
    float outer = Coverage(distance, pixelSize);
    float4 fill = Premultiply(color);
    float4 result = fill * outer;

    if (flags & YAUI_FLAG_BORDER)
    {
        float inner = Coverage(distance + borderWidth, pixelSize);
        result = fill * inner + Premultiply(borderColor) * (outer - inner);
    }

    if (flags & YAUI_FLAG_SHADOW)
    {
        // Blurred rounded box approximated by the Gaussian CDF of its distance field.
        float spread = shadow.w;
        float shadowDistance = RoundedBoxDistance(local - shadow.xy, halfSize + spread, radius + spread);
        float sigma = max(shadow.z, 1e-3);
        float s = 0.5 - 0.5 * Erf(shadowDistance / (sigma * 1.41421356));
        result += Premultiply(shadowColor) * s * (1.0 - result.a);
    }

    return result;
}

half4 FragImpl(Varyings i, bool world)
{
    uint flags = (uint)i.params.y & YAUI_FLAG_MASK;
    float2 local = i.local.xy;
    float2 halfSize = i.local.zw;
    #if YAUI_HAS_UV
    float2 uv = i.uv;
    #else
    float2 uv = 0.0;
    #endif
    #if YAUI_HAS_BORDER_COLOR
    float4 borderColor = i.borderColor;
    #else
    float4 borderColor = 0.0;
    #endif
    #if YAUI_HAS_SHADOW
    float4 shadowColor = i.shadowColor;
    float4 shadow = i.shadow;
    #else
    float4 shadowColor = 0.0;
    float4 shadow = 0.0;
    #endif
    // Perspective (world space): the pixel size varies over the quad.
    float pixelSize = world ? max(length(fwidth(local)) * 0.70710678, 1e-5) : i.canvasMisc.z;
    float4 color = i.color;

    float clipCoverage = 1.0;
    #if defined(YAUI_PIXEL_CLIP)
    // Clips the vertex shader cannot apply (rotated quads, rounded clips), in canvas space.
    [branch] if (i.canvasMisc.w > 0.0)
    {
        clipCoverage = YauiClipCoverage(i.canvasMisc.xy, i.clipRect, i.clipRounded, i.clipRadii);

        // Returns zero instead of discarding (premultiplied alpha): custom fragments can read the input afterwards.
        if (clipCoverage <= 0.0)
        {
            return 0;
        }
    }

    #endif

    if (flags & YAUI_FLAG_TEXT)
    {
        uint slot = TextureSlot(flags);
        float d = SampleTexture(slot, uv).a;
        // Synthesized bold dilates the glyph outline.
        float bias = (flags & YAUI_FLAG_TEXT_BOLD) ? 0.08 : 0.0;
        // Signed distance to the glyph outline in local units (positive inside).
        float units = (d - 0.5 + bias) * 2.0 * _YauiAtlasParams[slot].y / max(i.params.w, 1e-4);
        // The width of the outline (border), or how far a shadow dilates the glyph.
        float dilate = i.params.z;
        if (flags & YAUI_FLAG_SHADOW)
        {
            // A text shadow is a copy of the glyph, dilated and softened by the blur (the first radius).
            float softness = max(i.radii.x, pixelSize);
            return half4(Premultiply(color) * saturate((units + dilate) / softness + 0.5) * clipCoverage);
        }

        float fill = saturate(units / pixelSize + 0.5);
        float4 result = Premultiply(color) * fill;
        if (flags & YAUI_FLAG_BORDER)
        {
            float outer = saturate((units + dilate) / pixelSize + 0.5);
            result += Premultiply(borderColor) * (outer - fill);
        }

        return half4(result * clipCoverage);
    }

    if (flags & YAUI_FLAG_IMAGE)
    {
        color *= SampleTexture(TextureSlot(flags), uv);
    }

    if (flags & YAUI_FLAG_RADIAL_FILL)
    {
        color.a *= RadialFillCoverage(local, halfSize, borderColor, pixelSize);
    }

    float borderWidth = i.params.z;
    float maxRadius = min(i.params.x, min(halfSize.x, halfSize.y));

    // Pixels well inside the shape (and its shadow) skip the distance fields.
    float margin = max(maxRadius, borderWidth) + pixelSize;
    bool interior = all(abs(local) <= halfSize - margin);
    if (flags & YAUI_FLAG_SHADOW)
    {
        // Three sigmas inside the shadow, its coverage is above 99.8%.
        float shadowMargin = maxRadius + shadow.z * 3.0;
        interior = interior && all(abs(local - shadow.xy) <= halfSize - shadowMargin);
    }

    float4 result;
    if (interior)
    {
        result = Premultiply(color);
        if (flags & YAUI_FLAG_SHADOW)
        {
            result += Premultiply(shadowColor) * (1.0 - result.a);
        }
    }
    else
    {
        float radius = min(CornerRadius(local, i.radii), min(halfSize.x, halfSize.y));
        result = ShadeShape(local, halfSize, color, radius, flags, borderWidth, borderColor,
                            shadowColor, shadow, pixelSize);
    }

    return half4(result * clipCoverage);
}

// The shape of a mask (YauiMask) into the stencil: the coverage of the box (or the alpha of the image), without
// its color, border and shadow, thresholded at a half.
half4 FragMaskImpl(Varyings i, bool world)
{
    uint flags = (uint)i.params.y & ~(YAUI_FLAG_BORDER | YAUI_FLAG_SHADOW);
    i.params.y = flags;
    i.color = float4(1.0, 1.0, 1.0, 1.0);
    half4 coverage = FragImpl(i, world);
    clip(coverage.a - 0.5);
    return 0;
}

// Meshes of custom draws (YauiCustomDraw). Shaders for them declare the "Overlay" and "World" passes like Uber.shader
// with their own vertex functions on mesh attributes, which call YauiMeshVertex; the fragment functions multiply by
// YauiClipCoverage and premultiply.

// The node of the element, and the mesh to the local space of its box (canvas units, Y down). Per draw.
uint _YauiNode;
float4x4 _YauiMeshMatrix;

struct YauiMeshVertexData
{
    float4 positionCS;
    // Multiplies the colors of the mesh (straight alpha): the tint and the inherited opacity of the node.
    float4 color;
    float2 canvas;
    // The clip of the node, for YauiClipCoverage (pass them as nointerpolation varyings).
    float4 clipRect;
    float4 clipRounded;
    float4 clipRadii;
};

// world: world space panel (a compile time constant). The Z of the mesh is flattened onto the panel.
YauiMeshVertexData YauiMeshVertex(float3 positionOS, bool world)
{
    NodeData n = _YauiNodes[_YauiNode];
    ClipData clip = _YauiClips[n.opacityClip >> 16];
    float2 local = mul(_YauiMeshMatrix, float4(positionOS, 1.0)).xy;
    float2 canvas = mul(float2x2(n.m.xy, n.m.zw), local) + n.translation;

    YauiMeshVertexData o;
    o.positionCS = world
                       ? mul(UNITY_MATRIX_VP, mul(_YauiPanelMatrix, float4(canvas, 0.0, 1.0)))
                       : mul(UNITY_MATRIX_VP, float4(canvas, 0.0, 1.0));
    o.color = UnpackUnorm8x4(n.tint);
    o.color.a *= f16tof32(n.opacityClip & 0xffffu);
    o.canvas = canvas;
    o.clipRect = clip.rect;
    o.clipRounded = clip.rounded;
    o.clipRadii = clip.roundedRadii;
    return o;
}

Varyings VertOverlay(uint vertexId : SV_VertexID) { return VertImpl(vertexId, false); }
half4 FragOverlay(Varyings i) : SV_Target { return FragImpl(i, false); }
Varyings VertWorld(uint vertexId : SV_VertexID) { return VertImpl(vertexId, true); }
half4 FragWorld(Varyings i) : SV_Target { return FragImpl(i, true); }

half4 FragMaskOverlay(Varyings i) : SV_Target { return FragMaskImpl(i, false); }
half4 FragMaskWorld(Varyings i) : SV_Target { return FragMaskImpl(i, true); }

#endif
