using Mcp.Helpers;
using UnityEngine;

namespace Mcp.Network
{
    public static class McpAutoStart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Init()
        {
            MainThreadDispatcher.Ensure();
            McpTcpServer.Start();
            Application.quitting += McpTcpServer.Stop;
        }
    }
}
