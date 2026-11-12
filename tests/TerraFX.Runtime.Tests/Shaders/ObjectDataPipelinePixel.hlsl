float4 PSMain(float4 position : SV_Position) : SV_Target0
{
    return float4(1.0f, 0.0f, 1.0f, saturate(position.w));
}
