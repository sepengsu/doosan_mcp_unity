namespace Mcp.Network
{
    public static class McpServerConfig
    {
        public const int Port = 6400;
        public const int BufferSize = 32768;
        public const int MaxFrameBytes = 10_000_000;
        public const int ConnectionTimeoutMs = 15000;
    }
}
