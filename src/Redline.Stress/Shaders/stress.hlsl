// Must match Redline.Core.Stress.StressKernel.Element exactly.
RWStructuredBuffer<uint> Data : register(u0);
RWStructuredBuffer<uint> Result : register(u1);   // [0] checksum, [1] verify errors
RWStructuredBuffer<uint4> Src : register(u2);     // memory-bandwidth phase
RWStructuredBuffer<uint4> Dst : register(u3);
cbuffer Params : register(b0) { uint Seed; uint Iter; uint N; uint RowWidth; };

uint Element(uint i)
{
    uint x = (i * 2654435761u) ^ Seed;
    for (uint k = 0; k < Iter; k++)
    {
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        x = x * 1664525u + 1013904223u;
    }
    return x;
}

[numthreads(256, 1, 1)]
void Compute(uint3 id : SV_DispatchThreadID)
{
    uint i = id.y * RowWidth + id.x;
    if (i >= N) return;
    uint x = Element(i);
    Data[i] = x;
    InterlockedAdd(Result[0], x);
}

[numthreads(256, 1, 1)]
void Verify(uint3 id : SV_DispatchThreadID)
{
    uint i = id.y * RowWidth + id.x;
    if (i >= N) return;
    if (Data[i] != Element(i)) InterlockedAdd(Result[1], 1u);
}

[numthreads(1, 1, 1)]
void Reset(uint3 id : SV_DispatchThreadID)
{
    Result[0] = 0;
    Result[1] = 0;
}

// Memory-bandwidth phase: stream a large buffer through video memory (one read + one write per element).
// Too-high memory clocks on GDDR6X show up as lower bandwidth (the card retries its own errors), or as errors here.
uint4 Pattern(uint i)
{
    uint b = i * 2654435761u;
    return uint4(b, b ^ 0xA5A5A5A5u, b + 0x9E3779B9u, ~b);
}

[numthreads(256, 1, 1)]
void StreamInit(uint3 id : SV_DispatchThreadID)
{
    uint i = id.y * RowWidth + id.x;
    if (i >= N) return;
    Src[i] = Pattern(i);
}

[numthreads(256, 1, 1)]
void Stream(uint3 id : SV_DispatchThreadID)
{
    uint i = id.y * RowWidth + id.x;
    if (i >= N) return;
    Dst[i] = Src[i] ^ Seed;
}

[numthreads(256, 1, 1)]
void StreamVerify(uint3 id : SV_DispatchThreadID)
{
    uint i = id.y * RowWidth + id.x;
    if (i >= N) return;
    if (any(Dst[i] != (Pattern(i) ^ Seed))) InterlockedAdd(Result[1], 1u);
}
