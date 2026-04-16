using System.Text.Json;

namespace PhotoBoothWin.Bridge
{
    public class RpcRequest
    {
        public string id { get; set; } = "";
        public string cmd { get; set; } = "";
        public JsonElement data { get; set; }
    }
}
