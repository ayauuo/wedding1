using System.Text.Json.Serialization;

namespace PhotoBoothWin.Models
{
    /// <summary>表二：照片紀錄，上傳至 Google 工作表「拍貼機_4格窗細表」。</summary>
    public class PhotoDetail
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("time")]
        public string Time { get; set; } = "";

        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = "";

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("layoutType")]
        public string LayoutType { get; set; } = "";
    }
}
