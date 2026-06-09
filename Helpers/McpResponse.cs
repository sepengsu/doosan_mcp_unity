using Newtonsoft.Json;

namespace Mcp.Helpers
{
    public static class McpResponse
    {
        public static string Success(object result)
        {
            return JsonConvert.SerializeObject(new { status = "success", result });
        }

        public static string Error(string message, object detail = null)
        {
            return JsonConvert.SerializeObject(new { status = "error", error = message, detail });
        }
    }
}
