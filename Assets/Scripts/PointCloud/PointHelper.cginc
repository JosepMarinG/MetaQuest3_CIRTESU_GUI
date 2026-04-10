float4 UnpackRGBA(float rgba)
{
    int4 unpackedColor;
    int unpacked = asint(rgba);

    unpackedColor.r = unpacked >> 16 & 0xFF;
    unpackedColor.g = unpacked >> 8 & 0xFF;
    unpackedColor.b = unpacked & 0xFF;
    unpackedColor.a = 255;

    return unpackedColor / 255.0f;
}
