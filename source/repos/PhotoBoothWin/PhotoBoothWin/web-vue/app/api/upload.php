<?php
/**
 * 拍貼機上傳 API：接收前端送來的合成圖（base64）與影片（base64），存檔並回傳可下載網址。
 * 前端以 POST JSON 傳入：{ "imageData": "data:image/jpeg;base64,...", "videoData": "data:video/mp4;base64,..." }
 * 回傳：{ "url": "https://...", "videoUrl": "https://..." }
 */

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(204);
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    echo json_encode(['error' => 'Method Not Allowed']);
    exit;
}

$raw = file_get_contents('php://input');
$input = json_decode($raw, true);
if (!is_array($input)) {
    http_response_code(400);
    echo json_encode(['error' => 'Invalid JSON']);
    exit;
}

// 存檔目錄：與 api 同層的 uploads，請確保可寫入且網頁可讀（或改為你實際的公開目錄）
$uploadDir = __DIR__ . '/uploads';
if (!is_dir($uploadDir)) {
    if (!mkdir($uploadDir, 0755, true)) {
        http_response_code(500);
        echo json_encode(['error' => 'Cannot create upload directory']);
        exit;
    }
}

// 基底網址（供回傳下載連結用，可改為你的網域或留空用相對路徑）
$baseUrl = (isset($_SERVER['HTTPS']) && $_SERVER['HTTPS'] === 'on' ? 'https' : 'http')
    . '://' . ($_SERVER['HTTP_HOST'] ?? 'localhost');
$uploadBasePath = rtrim(dirname($_SERVER['SCRIPT_NAME'] ?? ''), '/');
$uploadBaseUrl = $baseUrl . $uploadBasePath . '/uploads';

$url = null;
$videoUrl = null;

// 處理圖片：imageData 為 data:image/xxx;base64,... 格式
if (!empty($input['imageData']) && is_string($input['imageData'])) {
    if (preg_match('/^data:image\/(\w+);base64,(.+)$/', $input['imageData'], $m)) {
        $ext = strtolower($m[1]);
        if ($ext === 'jpeg') $ext = 'jpg';
        $bin = base64_decode($m[2], true);
        if ($bin !== false) {
            $name = 'img_' . date('YmdHis') . '_' . substr(md5($bin), 0, 8) . '.' . $ext;
            $path = $uploadDir . '/' . $name;
            if (file_put_contents($path, $bin) !== false) {
                $url = $uploadBaseUrl . '/' . $name;
            }
        }
    }
    if ($url === null) {
        http_response_code(400);
        echo json_encode(['error' => 'Invalid imageData']);
        exit;
    }
}

// 處理影片：videoData 為 data:video/xxx;base64,... 格式（瀏覽器可能帶 codecs 如 video/webm;codecs=vp9;base64,...）
// 用 strpos 切出 base64，避免整段 payload 用正則造成回溯／記憶體問題
if (!empty($input['videoData']) && is_string($input['videoData'])) {
    $videoData = $input['videoData'];
    $base64Marker = ';base64,';
    $idx = strpos($videoData, $base64Marker);
    if ($idx !== false && strpos($videoData, 'data:video/') === 0) {
        $prefix = substr($videoData, 0, $idx);  // e.g. "data:video/webm;codecs=vp9"
        $b64 = substr($videoData, $idx + strlen($base64Marker));  // base64 本體
        if (preg_match('/^data:video\/(\w+)/', $prefix, $extM)) {
            $ext = strtolower($extM[1]);
            $bin = base64_decode($b64, true);
            if ($bin !== false) {
                $name = 'video_' . date('YmdHis') . '_' . substr(md5($bin), 0, 8) . '.' . $ext;
                $path = $uploadDir . '/' . $name;
                if (file_put_contents($path, $bin) !== false) {
                    $videoUrl = $uploadBaseUrl . '/' . $name;
                }
            }
        }
    }
    if (!empty($input['videoData']) && $videoUrl === null) {
        http_response_code(400);
        echo json_encode(['error' => 'Invalid videoData', 'hint' => 'Expected data:video/xxx;base64,... (e.g. video/webm;codecs=vp9;base64,...)']);
        exit;
    }
}

// 前端 upload 取 .url（圖），upload_video 取 .url（影片），故僅影片時也回傳 url
echo json_encode([
    'url' => $url ?? $videoUrl,
    'videoUrl' => $videoUrl,
]);
