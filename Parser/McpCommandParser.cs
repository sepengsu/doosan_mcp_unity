using System;
using System.Threading.Tasks;
using Mcp.Functions;
using Mcp.Helpers;
using Newtonsoft.Json.Linq;

namespace Mcp.Parser
{
    public static class McpCommandParser
    {
        public static async Task<string> Handle(string json)
        {
            try
            {
                JObject root = JObject.Parse(json);
                string type = root["type"]?.ToString()?.ToLowerInvariant();
                JObject parameters = root["params"] as JObject ?? root;

                object result = type switch
                {
                    "robot" => await RobotFunctions.Handle(parameters).ConfigureAwait(false),
                    "manage_robot" => await RobotFunctions.Handle(parameters).ConfigureAwait(false),
                    _ => throw new InvalidOperationException($"Unknown MCP command type '{type}'.")
                };

                return McpResponse.Success(result);
            }
            catch (Exception ex)
            {
                return McpResponse.Error(ex.Message);
            }
        }
    }
}
