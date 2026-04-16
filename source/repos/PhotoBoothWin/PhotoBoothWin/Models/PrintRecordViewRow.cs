using System.Text.Json.Serialization;

namespace PhotoBoothWin.Models
{
    /// <summary>觀看資料庫畫面用：單筆列印紀錄。</summary>
    public class PrintRecordViewRow
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("time")]
        public string Time { get; set; } = "";

        [JsonPropertyName("isTest")]
        public bool IsTest { get; set; }

        [JsonPropertyName("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("copies")]
        public int Copies { get; set; }

        [JsonPropertyName("projectName")]
        public string ProjectName { get; set; } = "";

        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = "";

        [JsonPropertyName("templateName")]
        public string TemplateName { get; set; } = "";
    }
}
