using System.Text.Json.Serialization;

namespace PhotoBoothWin.Models
{
    /// <summary>每日彙總報表，上傳至「日總表」「拍貼機_4格窗核銷表」。</summary>
    public class SummaryReport
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("projectName")]
        public string ProjectName { get; set; } = "";

        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = "";

        [JsonPropertyName("unitPrice")]
        public int UnitPrice { get; set; }

        [JsonPropertyName("dailySalesCount")]
        public int DailySalesCount { get; set; }

        [JsonPropertyName("dailyRevenue")]
        public int DailyRevenue { get; set; }
    }
}
