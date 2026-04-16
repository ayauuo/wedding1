// 使用「試算表 ID」寫入，避免 getActiveSpreadsheet() 寫到別本
// 使用方式：把下面 SPREADSHEET_ID 換成你的試算表 ID（網址中間那串）
// 例如：https://docs.google.com/spreadsheets/d/這裡就是ID/edit
var SPREADSHEET_ID = "1rtcshGeComl9AHR8d8bt0ydnk5VloccPFQ3_io1z1F";

function doPost(e) {
  try {
    var ss = SPREADSHEET_ID
      ? SpreadsheetApp.openById(SPREADSHEET_ID)
      : SpreadsheetApp.getActiveSpreadsheet();
    var payload = JSON.parse(e.postData.contents);
    var targetName = payload.targetSheet;
    var data = payload.data;

    var sheet = ss.getSheetByName(targetName) || ss.insertSheet(targetName);

    // 新工作表時加標題列
    if (sheet.getLastRow() === 0) {
      if (targetName === "日總表" || targetName === "拍貼機_4格窗核銷表") {
        sheet.appendRow(["日期", "專案名稱", "機器名稱", "單價", "今日銷售量", "今日銷售額"]);
      } else if (targetName === "拍貼機_4格窗細表") {
        sheet.appendRow(["日期", "時間", "機器名稱", "相片檔案名稱", "版型"]);
      }
    }

    // 日總表、拍貼機_4格窗核銷表：data 是陣列（多筆 SummaryReport）
    var debugInfo = "";
    if (targetName === "日總表" || targetName === "拍貼機_4格窗核銷表") {
      var rows = Array.isArray(data) ? data : [data];
      debugInfo = "筆數:" + rows.length;
      if (rows.length > 0) {
        var r0 = rows[0];
        debugInfo += " 第一筆keys:" + Object.keys(r0).join(",");
        debugInfo += " date=" + (r0.date != null ? r0.date : "undefined");
        debugInfo += " projectName=" + (r0.projectName != null ? r0.projectName : "undefined");
      }
      rows.forEach(function(r) {
        sheet.appendRow([r.date, r.projectName, r.machineName, r.unitPrice, r.dailySalesCount, r.dailyRevenue]);
      });
    }
    // 拍貼機_4格窗細表：data 是單一 PhotoDetail
    else if (targetName === "拍貼機_4格窗細表") {
      debugInfo = "date=" + data.date + " fileName=" + (data.fileName != null ? data.fileName : "undefined");
      sheet.appendRow([data.date, data.time, data.machineName, data.fileName, data.layoutType]);
    }

    return ContentService.createTextOutput("成功存入：" + targetName + " " + debugInfo).setMimeType(ContentService.MimeType.TEXT);
  } catch (f) {
    return ContentService.createTextOutput("Error: " + f.toString()).setMimeType(ContentService.MimeType.TEXT);
  }
}
